# PlayMuse ⇔ MuseWiser プロセス間通信 実装メモ

本ドキュメントは、PlayMuse（WASAPI排他モード再生対応の音楽プレーヤー）と MuseWiser（レベルメーター・スペクトラムアナライザ表示アプリ）間の
プロセス間通信（Named Pipe IPC）の実装内容、対応した不具合、設計上のポイントを今後の改修・機能追加のためにまとめたものである。

## 1. 背景・目的

- PlayMuse はビットパーフェクト再生のため WASAPI **排他モード**で再生することができる。
- 排他モード中は OS の既定出力デバイスを PlayMuse が専有するため、MuseWiser が WASAPI ループバックキャプチャで
  音声データ（レベルメーター・スペクトラム表示用）を取得できなくなる。
- そこで、PlayMuse が排他モードで再生している間だけ、**Named Pipe** 経由でレベル・スペクトラム・出力デバイス情報を
  MuseWiser へブロードキャストし、MuseWiser 側はこれを優先的に採用する仕組みを実装した。
- PlayMuse が共有モードに戻った場合は、MuseWiser は従来通り WASAPI ループバックキャプチャに自動フォールバックする。
- 両プレーヤーの再生状態・モード切替は実行時に動的に変化するため、**実行中の自動切り替え（Auto フォールバック）**が
  成立することが必須要件である。

## 2. 全体構成

```
PlayMuse (排他モード再生中)
  └─ NamedPipeLevelMeterBroadcastService (PlayMuse.Core)
		・ActualShareModeChanged を監視し、Exclusive のときだけ NamedPipeServerStream を起動
		・33ms 周期（≒30fps、スペクトラムアナライザ表示と同期）でレベル/スペクトラムをブロードキャスト
		・接続時に DeviceInfo（デバイス名・サンプルレート・チャンネル数・排他フラグ）を送信
			  │
			  │ Named Pipe "PlayMuse.LevelMeter"
			  ▼
MuseWiser
  └─ AdaptiveAudioCaptureService (IAudioCaptureService 実装)
		・PlayMuseNamedPipeAudioCaptureService と WasapiLoopbackAudioService を「常時」両方 Start する
		・パイプの接続状態に応じて、どちらのデータを採用するか（DataAvailable / DeviceChanged の中継）を切り替える
		├─ PlayMuseNamedPipeAudioCaptureService
		│     ・NamedPipeClientStream でパイプに接続・再接続し続ける
		│     ・接続できない／切断された場合は ReconnectIntervalMilliseconds 間隔でリトライ
		└─ WasapiLoopbackAudioService
			  ・MMDeviceEnumerator + NAudio WasapiRecorder で既定出力デバイスをループバックキャプチャ
			  ・DefaultDeviceChanged、RecordingStopped を監視し自動復旧する
			  ・Suspend()/Resume() でパイプ接続中の不要なキャプチャ試行を止める（後述）
```

### 共有ライブラリ: `PlayMuse.LevelMeterIpc`

- `LevelMeterMessageProtocol.cs` に、パイプ上でやり取りするメッセージのシリアライズ/デシリアライズ処理を実装。
- メッセージ種別は先頭1バイト（`LevelMeterMessageType`: `DeviceInfo` = 0, `Frame` = 1）+ 4バイトのペイロード長 + ペイロード、という
  **固定ヘッダー形式**。複数スレッドからの書き込みが混在してもメッセージ単位でアトミックに送れるよう、1メッセージ=1回の `WriteAsync` で送信する。
- 受信側 `LevelMeterMessageReader.ReadExactlyAsync` はストリームが途中で終了（`read == 0`）した場合に `EndOfStreamException` を送出する。
  これは意図的な設計であり、**パイプ切断の検知に利用している**（§4-2 参照）。

## 3. 主要クラスと責務

| クラス | プロジェクト | 責務 |
|---|---|---|
| `NamedPipeLevelMeterBroadcastService` | PlayMuse.Core | 排他モード時のみ Named Pipe サーバーを起動し、レベル/スペクトラム/デバイス情報を送信する |
| `PlayMuseNamedPipeAudioCaptureService` | MuseWiser | Named Pipe クライアント。接続・再接続ループを保持し、`IAudioCaptureService` として振る舞う |
| `WasapiLoopbackAudioService` | MuseWiser | WASAPI ループバックキャプチャ。`IAudioCaptureService` 実装。デバイス変更・録音停止からの自動復旧、および `Suspend`/`Resume` を持つ |
| `AdaptiveAudioCaptureService` | MuseWiser | 上記2つを常時起動しつつ、パイプの接続状態に応じて採用元（データの中継元）を切り替える調停役 |

いずれも `IAudioCaptureService`（`DataAvailable` / `DeviceChanged` / `ConnectionStateChanged` イベントを持つ）を実装しており、
ViewModel 側からは実装を意識せず `AudioSourceMode.Auto` として扱える。

## 4. 対応した不具合・設計上のポイント

### 4-1. 共有 → 排他 → 共有の切替時にレベルメーターが復帰しない

- **事象**: 排他モードから共有モードに戻した際、WASAPI ループバックの再開に失敗し、MuseWiser 側のレベルメーターが止まったままになる。
- **原因**: `WasapiRecorder` の `RecordingStopped` イベントを監視しておらず、また、録音開始失敗時のリトライ機構がなかったため、
  一度キャプチャが止まると自動復旧できなかった。
- **対応**:
  - `WasapiLoopbackAudioService` に **レコーダー世代番号（`_recorderGeneration`）** を導入し、
	意図的な `Stop`/デバイス切替では世代を進めて古いイベントを無視するようにした。
  - `RecordingStopped` ハンドラ（`OnRecordingStopped`）で世代が一致する場合のみ再試行をスケジュールする。
  - 開始失敗時（`AudioDeviceInUseException` 等）も同様に再試行をスケジュールする（`ScheduleRestartLocked`）。
  - リトライは **固定間隔（`RecordingRetryDelayMilliseconds` = 1000ms）** とした。指数バックオフも検討したが、
	共有モード復帰の検知が遅くなるデメリットが大きく撤回した。固定1秒は「復帰の速さ」と「ログ量」のバランスを取った値である。

### 4-2. パイプ切断時のハンドリング

- パイプが切断されると、読み取り中の `LevelMeterMessageReader.ReadExactlyAsync` が `EndOfStreamException` を送出する。
- `PlayMuseNamedPipeAudioCaptureService.RunConnectionLoopAsync` はこれを握りつぶしログ(Debug)に留め、
  `SetConnected(false)` → `ConnectionStateChanged` で `Disconnected` を通知し、`ReconnectIntervalMilliseconds` 待機後に再接続を試みる。
- **Visual Studio のデバッグ出力で `EndOfStreamException` が1回の切断につき複数回（観測上は最大4回程度）表示されることがあるが、
  これは非同期コールスタック上の複数の await 継続ポイントで first-chance 例外が通知されるためであり、実際の例外送出は1回である。
  実害はない。**

### 4-3. 【重要】排他モード中に WASAPI ループバックの再試行ログが大量発生する問題

- **事象**: PlayMuse が排他モードでデバイスを専有している間、`WasapiLoopbackAudioService` が
  `AudioDeviceInUseException` を伴うキャプチャ開始の再試行を **1秒間隔で永続的に繰り返し**、ログが大量に出力される。
- **原因**: `AdaptiveAudioCaptureService` は設計上、パイプと WASAPI ループバックの**両方を常時 `Start`** しているが、
  パイプ接続中は WASAPI ループバック側のデータは採用されない（単に捨てられる）だけで、
  ループバック側のサービス自体は通常の「共有モード運用」と同じように動き続け、排他専有中のデバイスに対して
  開始を試み続けてしまっていた。
- **対応（本セッションでの主な変更点）**:
  - `WasapiLoopbackAudioService` に **`Suspend()` / `Resume()`** を追加。
	- `Suspend()`: `_suspended = true` にし、保留中のリトライ（`_restartCts`）をキャンセルし、現在のキャプチャを停止する。
	- `Resume()`: `_suspended = false` にし、`_started` であれば直ちに `StartCaptureOnDefaultDevice()` を呼んで即時復帰を試みる。
	- 冪等（何度呼んでも安全）になるよう、状態が変化しない場合は何もしない。
  - `_suspended` 中はキャプチャの新規開始・再試行・デバイス変更追従を一切行わないよう、以下の箇所にガードを追加:
	- `Start()` : `_suspended` なら `StartCaptureOnDefaultDevice()` を呼ばない（通常運用ではまず発生しないケースだが防御的に対応）。
	- `OnDefaultDeviceChanged()` : `_suspended` なら何もしない。
	- `ScheduleRestartLocked()` : `_suspended` ならリトライをスケジュールしない。
	- `ScheduleRestartAsync()` : 待機後の再開時点で `_suspended` になっていたら再開しない。
  - `AdaptiveAudioCaptureService.OnPipeConnectionStateChanged` で、パイプ接続状態の変化に応じて呼び出す:
	- パイプ **接続時** → `_loopbackService.Suspend()`（排他モード中の無駄なリトライを止める）
	- パイプ **切断時** → `_loopbackService.Resume()`（共有モードへの復帰を即座に試みる）
  - この対応により、排他モード中は `AudioDeviceInUseException` のログが発生しなくなり、共有モード復帰時は
	従来通り迅速にループバックキャプチャが再開される。

#### 設計上の注意点（今後の改修時に踏まえるべきこと）

- `AdaptiveAudioCaptureService.Start()` は **`_loopbackService.Start()` を先に呼んでから `_pipeService.Start()` を呼ぶ**。
  そのため、起動直後の一瞬だけ（パイプの接続イベントが発火するまでの間）ループバックが起動する可能性があるが、
  実害はないため許容している。
- `Suspend`/`Resume` は **`_gate`（WasapiLoopbackAudioService 内のロックオブジェクト）内で完結する**ように実装しており、
  他の開始経路（`Start`, `OnDefaultDeviceChanged`, `ScheduleRestartLocked`, `ScheduleRestartAsync`）と排他的に動作する。
  新たにキャプチャ開始経路を追加する場合は、必ず `_suspended` ガードを追加すること。
- `Stop()`/`Dispose()` 時に `_suspended` 状態が残っていても問題ない設計（`Stop` は無条件にキャプチャを止めるため）だが、
  再度 `Start()` する場合に `_suspended` の初期化（リセット）が必要かどうかは、呼び出し元のライフサイクル次第で再検討の余地がある。
  現状 `AdaptiveAudioCaptureService` は `Start`/`Stop` を1インスタンスにつき1回ずつ想定した作りであり、
  再利用する場合は `_suspended` の扱いを再確認すること。

### 4-4. PlayMuse 側のモード切替頻度について（既知の留意事項、未対応）

- 実機検証ログでは、短時間にパイプの接続・切断（＝排他⇔共有モードの切替）が繰り返されるケースが観測された。
- MuseWiser 側はこれに対して正しく追従・復旧できている（パイプ再接続、ループバック Suspend/Resume とも正常動作）ため、
  **MuseWiser 側の追加対応は行っていない**。
- もし PlayMuse 側で意図せず排他モードの取得・解放が頻発するようであれば、PlayMuse 側の排他モード初期化ロジック
  （リトライ間隔、デバイス初期化タイミング）を見直す価値がある。これは本セッションのスコープ外とした。

## 5. デバイス名・排他表示

- `LevelMeterDeviceInfoMessage` に `IsExclusiveMode` フラグを持たせ、PlayMuse 側は常に `true` を送信する
  （排他モードのときだけサーバーが起動するため）。
- MuseWiser 側 `PlayMuseNamedPipeAudioCaptureService.RaiseDeviceChanged` で、デバイス名に
  `「（排他モード）」` という接尾辞を付与してから `DeviceChanged` イベントを発火し、UI 表示に反映している。

## 6. テスト

- `PlayMuse.Tests/Services/NamedPipeLevelMeterBroadcastServiceTests.cs` にて、以下を検証済み:
  - 排他モード開始時にブロードキャストサーバーが起動すること
  - 共有モードに戻るとサーバーが例外なく停止すること
  - 起動時点で既に排他モードの場合も即座にサーバーが起動すること
  - `NormalizeLevel` のクランプ・スケーリングの境界値
- 本セッションでの変更（Suspend/Resume 追加）後も、PlayMuse.Tests 全110件が成功することを確認済み。
- MuseWiser 側の `AdaptiveAudioCaptureService` / `WasapiLoopbackAudioService` の Suspend/Resume に関する
  **専用の自動テストは未整備**。実機検証で動作確認済みだが、将来的にはモック化した `IAudioCaptureService` を用いた
  単体テストの追加を検討する余地がある。

## 7. 今後の改修・機能追加時の参考ポイント

1. **新しいデータ種別をパイプで追加送信したい場合**: `LevelMeterMessageType` に種別を追加し、
   `LevelMeterMessageWriter`/`LevelMeterMessageReader` の双方に対応するシリアライズ処理を追加する。
   ヘッダー形式（1バイト種別 + 4バイト長）を崩さないこと。
2. **採用元切替のロジックを変更する場合**: `AdaptiveAudioCaptureService.OnPipeConnectionStateChanged` が
   唯一の切替ポイントである。ここに手を入れる際は、`WasapiLoopbackAudioService` の `Suspend`/`Resume` との
   整合性（呼び出し漏れがないか）を必ず確認すること。
3. **WasapiLoopbackAudioService にキャプチャ開始経路を追加する場合**: 必ず `_suspended` ガードを追加すること
   （§4-3 参照）。ガード漏れがあると排他モード中にログが再発する。
4. **リトライ間隔を調整する場合**: `RecordingRetryDelayMilliseconds`（WasapiLoopbackAudioService）や
   `ReconnectIntervalMilliseconds`（PlayMuseNamedPipeAudioCaptureService、PlayMusePipeOptions）を変更する。
   ただし、間隔を伸ばすと復帰検知が遅くなるため、ログ量とのトレードオフを踏まえて調整すること。
5. **PlayMuse 側のブロードキャスト周期を変更する場合**: `NamedPipeLevelMeterBroadcastService.BroadcastIntervalMilliseconds`
   （現状33ms、スペクトラムアナライザ表示の30fpsに同期）を変更する。MuseWiser 側の描画フレームレートとの整合性に注意。
