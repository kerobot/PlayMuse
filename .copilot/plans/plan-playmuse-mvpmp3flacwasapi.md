# 🎯 PlayMuse 音楽プレイヤー MVP実装計画（MP3→FLAC→WASAPI排他モードの段階的実装）

## 背景・現状
- `PlayMuse.slnx` 配下は `PlayMuse`（WPF, net10.0-windows, UseWPF=true）1プロジェクトのみで、NuGet未導入、`MainWindow`/`App` は雛形のみ。
- `global.json` / `Directory.Build.props` / `.editorconfig` / CI設定は無し。
- ライセンスは Apache License 2.0（サードパーティ導入時の互換性は後日プロジェクト全体で調整する方針をユーザー承認済み）。

## 決定事項（ユーザー承認済み）
- **プロジェクト構成は3分割**：
  - `PlayMuse`（WPF, View/App起動のみ）
  - `PlayMuse.Core`（class library, net10.0。Models/ViewModels/Services、UIフレームワーク非依存）
  - `PlayMuse.Tests`（xUnit, net10.0。`PlayMuse.Core` を参照）
  - 参照関係: `PlayMuse` → `PlayMuse.Core` ← `PlayMuse.Tests`
- **ライブラリ選定は要件実現を優先**。ライセンス互換性（例: TagLibSharpはLGPL-2.1、本体はApache-2.0）は都度メモを残しつつ、最終調整は別タスクとして後回しにする。

## 技術方針・採用ライブラリ
| 目的 | ライブラリ | 備考 |
|---|---|---|
| MVVM | `CommunityToolkit.Mvvm` | `ObservableObject` / `[ObservableProperty]` / `[RelayCommand]`。`PlayMuse.Core` に配置しUI非依存を維持 |
| DI | `Microsoft.Extensions.DependencyInjection` | `App.xaml.cs` をコンポジションルート化 |
| 音声再生/WASAPI | `NAudio` | `AudioFileReader`（Volume/Seek標準搭載）+ `WasapiOut`（`AudioClientShareMode.Shared/Exclusive` 両対応） |
| メタデータ | `TagLibSharp` | ID3(mp3)/Vorbis Comment(flac) 統一取得。LGPL-2.1のためNOTICE追記が必要（後日対応） |
| ログ | `Microsoft.Extensions.Logging`（Debugプロバイダ） | Phase 4で最小限導入 |
| テスト | `xUnit` | 音声I/Oは対象外、純粋ロジックのみ |

## 重要な技術的前提（実装時に要検証）
- `NAudio.Wave.AudioFileReader` は拡張子未一致時にWindows Media Foundation (`MediaFoundationReader`) へ自動フォールバックする。Windows 10 1803以降はMedia FoundationがFLACをネイティブデコード可能なため、**mp3用に実装したパイプラインがFLACでもそのまま動く可能性が高い**。ただし本計画ではあえてPhase 1では拡張子許可リストで `.mp3` のみに制限し、Phase 2で明示的に解禁・検証する（スコープ管理とQAのため）。
- WASAPI排他モードは `MMDevice.AudioClient.IsFormatSupported(AudioClientShareMode.Exclusive, format)` によるフォーマット事前検証が必須。非対応時は共有モードへ自動フォールバックする。
- デバイス切替・共有/排他切替時、`WasapiOut` はホットスワップ非対応のため「停止→Dispose→再構築」を安全に行う統一ルールが必要（Phase 1のデバイス切替実装時に確立し、以降のPhaseで再利用）。

## VS2026 + GitHub Copilot 運用メモ
- 新規プロジェクト追加はVisual Studioの「新しいプロジェクトの追加」UIで実施（`.slnx`との整合性を優先）。
- NuGet追加は `pwsh.exe` から `dotnet add <proj> package <Name>` を使用し再現性を確保。
- 各ステップ〜フェーズ区切りで `dotnet build` によるビルド確認を実施。
- フェーズ完了ごとにGitコミット（例: `feat: mp3 mvp playback`）、可能ならタグ付け。
- Copilot Chat（Edit/Agentモード）へは本計画のステップ番号を指定して段階的に依頼する。

## 責務分離（インターフェース）
`IAudioPlaybackService` / `IAudioDeviceService` / `IPlaylistService` / `ISettingsService` / `IMetadataService`（すべて `PlayMuse.Core/Services` 配下）

## リスク・非対応事項（MVPスコープ外として明示）
- リピート/シャッフル再生は対象外（Next/Previousは境界で停止する単純な順次ロジックのみ）。
- ギャップレス再生、クロスフェードは対象外。
- スペアナ/背景画像/歌詞表示/オンライン歌詞連携は本計画ではPhase 5でインターフェースのみ用意し、実処理は実装しない。

---

**進行状況**: 80% [████████░░]

**最終更新**: 2026-01-27 15:00:00

## 📝 プランの手順
- ✅ **`PlayMuse.Core`（class library, net10.0）と `PlayMuse.Tests`（xUnitプロジェクト, net10.0）をソリューションに追加し、`PlayMuse`→`PlayMuse.Core`、`PlayMuse.Tests`→`PlayMuse.Core` の参照を設定する。**
- ✅ **`PlayMuse.Core` に `CommunityToolkit.Mvvm` を、`PlayMuse` に `Microsoft.Extensions.DependencyInjection` をNuGet追加する。**
- ✅ **`PlayMuse.Core/Services` にコアインターフェース（`IAudioPlaybackService` / `IAudioDeviceService` / `IPlaylistService` / `ISettingsService` / `IMetadataService`）と、支援用の列挙型（`PlaybackState`, `AudioShareMode`）・モデル（`Track`, `Playlist`, `AudioDeviceInfo`）を定義する。**
- ✅ **フォルダー構成を整備する：`PlayMuse.Core/Models`, `PlayMuse.Core/ViewModels`, `PlayMuse.Core/Services`、`PlayMuse/Views`, `PlayMuse/Resources/Themes`。**
- ✅ **`PlayMuse/App.xaml.cs` にDIコンテナ構築処理（`OnStartup` オーバーライドで `ServiceCollection` 構築）を実装し、`MainWindow` をDI経由（コンストラクタ注入で `MainViewModel` を受け取る）で起動するよう変更する。**
- ✅ **`dotnet build` でソリューション全体のビルドを確認し、`feat: solution scaffolding` としてコミットする。**
- ✅ **`PlayMuse.Core` に `NAudio` をNuGet追加する。**
- ✅ **`Track`（ファイルパス、表示タイトル＝ファイル名フォールバック、長さ）と `Playlist`（順序付きコレクション、現在インデックス）モデルを実装し、拡張子許可リストは現時点で `.mp3` のみとする。**
- ✅ **`AudioPlaybackService : IAudioPlaybackService` を実装する。`NAudio.Wave.AudioFileReader` を `NAudio.Wave.WasapiOut`（`AudioClientShareMode.Shared` 固定）に接続し、Play/Pause/Stop/Volume/Seek/再生状態通知を提供する。トラック切替・アプリ終了時の `Dispose` を確実に行う。**
- ✅ **`AudioDeviceService : IAudioDeviceService` を実装する。`NAudio.CoreAudioApi.MMDeviceEnumerator` でアクティブなレンダーデバイスを列挙し、既定デバイスを取得できるようにする。**
- ✅ **`PlaylistService : IPlaylistService` を実装する。追加/削除/クリア、現在トラック管理、Next/Previousの境界ロジック（MVPでは境界で停止、リピート/シャッフルなし）を提供する。**
- ✅ **`MainViewModel`（`CommunityToolkit.Mvvm` の `ObservableObject`）を実装する。Open/Play/Pause/Stop/Next/Previousの `[RelayCommand]`、現在トラック・再生状態・位置・長さ・音量・デバイス一覧/選択のバインド可能プロパティを持たせ、再生中は `DispatcherTimer`（約250ms間隔）で再生位置をポーリングする。**
- ✅ **`MainWindow.xaml` のUIを実装する：「ファイルを開く」導線（`OpenFileDialog`、フィルタは `*.mp3`）、プレイリスト表示（`ListBox`等）、再生系ボタン（Play/Pause/Stop/Next/Previous）、位置スライダー＋経過/総時間表示、音量スライダー、デバイス選択コンボボックス。**
- ✅ **シーク機能を実装する。スライダーのドラッグ終了（または値変更、ただしユーザー操作中でない場合のみ）で `AudioFileReader.CurrentTime` を更新し、ドラッグ中は位置ポーリングによる値上書きを止める。**
- ✅ **音量機能を実装する。音量スライダーを `AudioFileReader.Volume`（0.0〜1.0）へViewModel経由で双方向バインドする。**
- ✅ **デバイス切替を実装する。選択変更時に安全に再生停止→既存 `WasapiOut` を破棄→新しい `MMDevice` で再構築し、同一トラックを継続する際の再開ルール（同位置から再開 or 曲頭から再開のいずれかに統一）を明確化する。**
- ✅ **基本的な異常系処理を追加する：非対応拡張子はファイルを開く時点でユーザー通知して除外、デバイス列挙/初期化失敗はクラッシュさせずステータス表示または `MessageBox` で通知する。**
- ✅ **複数mp3ファイルでの結合動作を手動確認する（再生/一時停止/停止、プレイリスト両端でのNext/Previous、シーク、音量変更、再生中デバイス切替）。**
- ✅ **`feat: mp3 mvp playback` としてコミットし、任意で `v0.1-mp3-mvp` タグを付与する。**
- ✅ **拡張子許可リストと `OpenFileDialog` のフィルタに `.flac` を追加する（「対応音楽ファイル」統合フィルタ＋個別フィルタを用意）。**
- ✅ **フォーマット判定処理を小さなレジストリ/ヘルパー（例: `SupportedAudioFormats`）としてリファクタリングし、将来のフォーマット追加（wav, aac等）を1箇所の変更で済むようにする。**
- ✅ **既存の `AudioFileReader`→`WasapiOut` パイプラインでFLAC再生が成立するか検証する（Windows 10 1803以降のMedia Foundation内蔵FLACデコーダに依存）。再生失敗やフォーマット非対応が確認された場合は、`IAudioPlaybackService` の抽象を保ったまま、FLAC対応の代替デコード経路（別ライブラリ導入）をフォールバック戦略として検討・統合する。**
- ✅ **`TagLibSharp` を `PlayMuse.Core` に追加し、`MetadataService : IMetadataService` を実装してID3(mp3)/Vorbis Comment(flac)からタイトル/アーティスト/アルバム/長さを取得する。タグ欠落/読取失敗時はファイル名にフォールバックする。LGPL-2.1ライセンスである旨を `THIRD-PARTY-NOTICES`（プレースホルダー）に記録し、後日のライセンス調整対象として明示する。**
- ✅ **トラック読み込み処理に `MetadataService` を組み込み、プレイリスト/UIに解決済みのタイトル/アーティストを表示する（取得できない場合はファイル名表示のまま）。**
- ✅ **FLAC特有の異常系を確認する：特殊なビット深度/サンプルレート、破損ファイル、mp3/flac混在プレイリストでのフォーマットをまたぐNext/Previous遷移。**
- ✅ **`feat: flac support` としてコミットし、任意で `v0.2-flac` タグを付与する。**
- ✅ **設定/UIに再生モード（共有/排他）切替を追加し、最小限の `ISettingsService` 実装（例: `%AppData%` へのJSON保存）で永続化する。**
- ✅ **`IAudioPlaybackService`/`AudioDeviceService` を拡張し `AudioClientShareMode.Exclusive` を指定可能にする。排他モードで開く前に `MMDevice.AudioClient.IsFormatSupported(AudioClientShareMode.Exclusive, format)` でフォーマット対応可否を検証する。**
- ✅ **排他モードのフォーマット交渉に失敗した場合、自動的に共有モードへフォールバックし、ユーザーへ通知（ステータス表示）してクラッシュさせないようにする。**
- ✅ **トラック再生中の共有/排他モード切替・デバイス切替時に、Phase 1で確立した「停止→Dispose→再構築→再開」ルールを一貫して適用する安全な再初期化処理を実装する。**
- ✅ **排他モード特有の異常系（他アプリが既にデバイスを排他使用中 等）に対する明確なユーザー向けエラーメッセージを実装する。**
- ✅ **mp3/flac双方、可能であれば複数の出力デバイスで共有/排他モードの結合動作を確認する。**
- ✅ **`feat: wasapi exclusive mode` としてコミットし、任意で `v0.3-wasapi-exclusive` タグを付与する。**
- ✅ **楽曲情報表示（サンプリング情報、メタデータ）の実装と、ボタン群の整理、ビットパーフェクト判定機能を追加する。**
-  **プレイリストのローカル保存/読込機能を実装する。`IPlaylistService` に `SavePlaylist(string filePath)` / `LoadPlaylist(string filePath)` メソッドを追加し、トラックのファイルパスリストをJSON形式（例: `.plm` 拡張子）で保存/読込できるようにする。**
-  **プレイリスト保存/読込のUIを追加する：「プレイリストを保存」「プレイリストを開く」ボタンを配置し、`SaveFileDialog` / `OpenFileDialog` でファイル選択を行う。フィルタは `*.plm` とする。**
-  **プレイリストファイルの相対パス/絶対パスの扱い方針を決定し実装する。保存時にファイルパスを相対パス化するか、または絶対パスのまま保存するかを決定し、読込時に存在しないファイルは警告表示してスキップする。**
-  **プレイリスト保存/読込の異常系を処理する：ファイル読込失敗（JSON形式不正、アクセス権限なし等）、トラックファイルが存在しない場合のスキップ処理とユーザー通知を実装する。**
-  **保存したプレイリストファイルでの結合動作を確認する：保存→読込→再生、存在しないファイルを含むプレイリストの読込、空プレイリストの保存/読込。**
-  **`feat: playlist persistence` としてコミットし、任意で `v0.4-playlist-save` タグを付与する。**
-  **動的な環境変化への耐性を強化する：プレイリスト追加後にファイルが削除/移動された場合、再生中に出力デバイスが切断された場合を検知し、未処理例外ではなく回復可能なエラー状態として扱う。**
-  **`Microsoft.Extensions.Logging`（Debugプロバイダ）による最小限のログを、再生状態変化・デバイス変更・エラー発生箇所（サービス境界）に追加する。**
-  **`PlayMuse.Tests` に単体テストを追加する：`PlaylistService` のNext/Previous境界動作、フォーマット判定ロジック、`MainViewModel` のコマンド活性/非活性制御（`IAudioPlaybackService` のフェイク/モックを使用）。**
-  **Phase 1〜3で実装したコードの一貫性・命名・不要コードについてレビュー/リファクタリングを行う。**
-  **`IAudioPlaybackService` に将来のスペクトラムアナライザー向けフック（サンプルデータ供給用イベント/プロバイダインターフェース）を追加する（可視化処理自体は実装しない）。**
-  **`ILyricsProvider` / `IOnlineLyricsProvider` インターフェースを定義する（スタブ/no-op実装のみとし、ローカル/オンライン歌詞機能の拡張ポイントを確保する）。**
-  **`App.xaml` にマージする `Themes/Default.xaml` の `ResourceDictionary` 雛形を作成し、将来のテーマ切替パターンを確立する。**
-  **`SettingsView`/`SettingsViewModel` の雛形を作成し、Phase 3で実装したデバイス/共有モード設定をそこへ移設する（将来の専用設定画面の土台とする）。**
-  **`README.md` を更新し、現状の実装状況と今後のロードマップ（スペクトラムアナライザー、背景画像表示、歌詞表示、オンライン歌詞連携、および未対応のサードパーティライセンス調整）を追記する。**

