---
title: "【C#】WASAPI 排他モード中でもレベルメーターを動かしたい、を Named Pipe で解決してみた"
emoji: "🎚️"
type: "tech"
topics: ["dotnet", "csharp", "wpf", "wasapi", "naudio"]
published: false
---

## はじめに

以前「[WASAPI 排他モードでビットパーフェクト再生する音楽プレーヤーを WPF で作ってみた](https://zenn.dev/kerobot/articles/90190d8d203740)」という記事で、自作の音楽プレーヤー PlayMuse を紹介しました。

https://zenn.dev/kerobot/articles/90190d8d203740

その後、NAudio を 3.3 系にバージョンアップした話も書きました。

https://zenn.dev/kerobot/articles/3dbea3af69a50e

PlayMuse はビットパーフェクト再生のために WASAPI 排他モードで出力デバイスを専有してしまうのですが、そうすると困ったことが起きます。以前作った「レベルメーター・スペクトラムアナライザ表示アプリ」MuseWiser（旧：WeatherWiser） が、出力デバイスをキャプチャできなくなってしまうのです。

今回は、この「排他モード中は見た目のメーターが死んでしまう」問題を、プロセス間通信（Named Pipe）で解決したので、対応の過程で踏んだいくつかの不具合や設計判断も含めて紹介します。

## 作ったもの

https://github.com/kerobot/PlayMuse

## 困っていたこと

MuseWiser は WASAPI ループバックキャプチャで既定の出力デバイスの音を拾い、レベルメーターとスペクトラムアナライザを描画する仕組みにしていました。

ところが、PlayMuse が WASAPI 排他モードでデバイスを握ってしまうと、OS のミキサーを経由しないため、ループバックキャプチャ側からは音がまったく拾えなくなります。排他モードはビットパーフェクト再生のために必要な仕組みなので、これは仕様上どうしようもありません。

そこで考えたのが、「PlayMuse が排他モードで再生している間は、PlayMuse 自身がレベル・スペクトラムの計算結果をプロセス間通信で MuseWiser に直接渡してしまおう」というアプローチです。共有モードに戻ったときは、今まで通り MuseWiser 側の WASAPI ループバックキャプチャに任せます。

## 全体の仕組み

```
PlayMuse (排他モードで再生中)
  └─ NamedPipeLevelMeterBroadcastService
	・排他モードに入ったときだけ Named Pipe サーバーを起動
	・33ms 周期（≒30fps、スペクトラムアナライザ表示に合わせた間隔）でレベル・スペクトラムを送信
	・接続時にはデバイス名・サンプルレート・チャンネル数も送る
		  │
		  │ Named Pipe "PlayMuse.LevelMeter"
		  ▼
MuseWiser
  └─ AdaptiveAudioCaptureService
	・パイプ経由のキャプチャサービスと WASAPI ループバックキャプチャサービスを両方常時起動しておき、
	  パイプの接続状態に応じて「採用するデータの出どころ」だけを切り替える
```

ポイントは、MuseWiser 側で「パイプが使えるときはパイプ、使えないときは WASAPI ループバック」という**自動フォールバック**を、`IAudioCaptureService` という共通インターフェースの裏側で実現しているところです。ViewModel 側はこの切り替えをまったく意識する必要がありません。

```csharp
public interface IAudioCaptureService : IDisposable
{
	event EventHandler<AudioCaptureDataEventArgs>? DataAvailable;
	event EventHandler<AudioCaptureDeviceInfo>? DeviceChanged;
	event EventHandler<AudioCaptureConnectionState>? ConnectionStateChanged;

	void Start();
	void Stop();
}
```

`AdaptiveAudioCaptureService` は、この `IAudioCaptureService` を実装する2つの具体クラス（パイプ経由とループバック）を内部に持ち、自身も同じインターフェースとして外部に振る舞います。

## Named Pipe でのメッセージのやり取り

パイプでやり取りするメッセージは、`PlayMuse.LevelMeterIpc` という共有ライブラリに切り出しました。フォーマットはシンプルに、「種別（1バイト）＋ペイロード長（4バイト）＋ペイロード」という固定ヘッダー形式にしています。

```csharp
internal enum LevelMeterMessageType : byte
{
	DeviceInfo = 0,
	Frame = 1,
}
```

1メッセージを必ず1回の `WriteAsync` で書き込むようにして、複数スレッドから同時に書き込まれてもメッセージ単位でのアトミック性が崩れないようにしています。受信側は `ReadExactlyAsync` でヘッダー分・ペイロード分を必ず読み切るまで待ち、途中でストリームが終わってしまったら `EndOfStreamException` を投げて「切断された」とみなす設計です。

```csharp
private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
{
	var totalRead = 0;
	while (totalRead < buffer.Length)
	{
		var read = await stream.ReadAsync(buffer.AsMemory(totalRead), cancellationToken).ConfigureAwait(false);
		if (read == 0)
		{
			throw new EndOfStreamException("パイプ接続が切断されました。");
		}

		totalRead += read;
	}
}
```

ちなみに、デバッグ実行中にこの `EndOfStreamException` が Visual Studio の「例外がスローされました」通知で何度も表示されることがありますが、これは非同期の await 継続ポイントごとに first-chance 例外の通知が複数回飛んでくるためで、実際の例外送出は1回だけです。最初は「大量に例外が出ている…？」と焦りましたが、ちゃんと catch されていて実害はありませんでした。

## つまずいたところ①：共有 → 排他 → 共有 の切り替えで復帰しない

最初に実装したときは、共有モードから排他モードに切り替わり、また共有モードに戻ったときに、MuseWiser 側のレベルメーターが固まったまま動かなくなる不具合がありました。

原因は、`WasapiRecorder` の `RecordingStopped` イベントをまったく監視しておらず、また、キャプチャ開始に失敗したときのリトライ処理も用意していなかったことです。一度キャプチャが止まってしまうと、何もトリガーがないまま復活できなかったわけです。

対応として、レコーダーに「世代番号」を持たせ、意図的に `Stop` したり出力デバイスが切り替わったりしたときは世代を進めて、古い世代からの `RecordingStopped` 通知は無視するようにしました。そのうえで、録音停止の検知時・開始失敗時のどちらでも、同じリトライ処理に合流させています。

```csharp
private void OnRecordingStopped(int generation, StoppedEventArgs args)
{
	lock (_gate)
	{
		if (!_started || generation != _recorderGeneration)
		{
			return;
		}

		_logger.LogInformation("ループバックキャプチャが停止しました（デバイス占有解放など）。再接続を試みます。");
		ScheduleRestartLocked();
	}
}
```

リトライ間隔は固定 1 秒にしました。最初は指数バックオフにして「失敗が続くほど待ち時間を延ばす」実装にしたのですが、そうすると共有モードに戻ったときの復帰がワンテンポ遅れてしまい、体感的な使い勝手が悪化したため、あっさり撤回して固定間隔に戻しています。ログの出力量と復帰の速さはトレードオフの関係にあり、今回は復帰の速さを優先しました。

## つまずいたところ②：排他モード中にエラーログが延々と流れ続ける

上記の対応で切り替え自体はスムーズになったのですが、実機で動かしてみると、排他モード中ずっと以下のようなログが 1 秒おきに流れ続けてしまいました。

```
例外がスローされました: 'NAudio.CoreAudioApi.AudioDeviceInUseException'
WasapiLoopbackAudioService: 既定の再生デバイスが排他モードで使用中のため、ループバックキャプチャを開始できません。解放され次第再試行します。
```

よく考えると当然で、`AdaptiveAudioCaptureService` はパイプ経由のキャプチャサービスと WASAPI ループバックキャプチャサービスを「両方とも常時起動」する設計にしていました。排他モード中はパイプ側のデータしか使わないのに、ループバック側は律儀に「デバイスが使えるようになるまで毎秒リトライ」を続けてしまっていたのです。パイプが生きている間は、そもそもループバック側の出番がないのだから、リトライする必要自体がありません。

そこで、ループバックキャプチャサービス側に `Suspend()` / `Resume()` を追加し、パイプが接続している間は明示的にループバック側のリトライを止めるようにしました。

```csharp
public void Suspend()
{
	lock (_gate)
	{
		if (_suspended)
		{
			return;
		}

		_suspended = true;
		_restartCts?.Cancel();
		_restartCts?.Dispose();
		_restartCts = null;

		StopCaptureLocked();
	}
}

public void Resume()
{
	lock (_gate)
	{
		if (!_suspended)
		{
			return;
		}

		_suspended = false;

		if (_started)
		{
			StartCaptureOnDefaultDevice();
		}
	}
}
```

これだけだと、保留中のリトライを1回止めるだけで終わってしまうので、キャプチャ開始につながる他の経路（デバイス変更の追従、リトライのスケジューリングと実行）にも、すべて `_suspended` のガードを入れています。

```csharp
private void ScheduleRestartLocked()
{
	if (!_started || _suspended)
	{
		return;
	}
	// ...リトライをスケジュール
}
```

あとは、呼び出し元の `AdaptiveAudioCaptureService` でパイプの接続状態が変わったタイミングでこれを呼び出すだけです。

```csharp
private void OnPipeConnectionStateChanged(object? sender, AudioCaptureConnectionState state)
{
	var isActive = state == AudioCaptureConnectionState.Connected;
	// ...（状態が変わらなければ何もしない）

	_isPipeActive = isActive;

	if (isActive)
	{
		// パイプが有効な間はループバックのリトライを止める
		_loopbackService.Suspend();
	}
	else
	{
		// パイプが切れたら即座に復帰を試みる
		_loopbackService.Resume();
	}
}
```

これで、排他モード中は不要なエラーログがぴたりと止まり、共有モードに戻った瞬間には `Resume()` によって即座にループバックキャプチャが再開されるようになりました。「止めるべきときにきちんと止める」というだけのことなのですが、両方のサービスを常時動かしておく設計にしていたぶん、後から気づきにくいポイントだったなと感じています。

新しくキャプチャ開始の経路を追加するときは、`_suspended` のガードを入れ忘れるとまたログが復活してしまうので、ここは今後の自分向けの注意メモでもあります。

## 気づき・まとめ

今回の対応を通じて、あらためて感じたことをまとめておきます。

- **「常時起動しておいて、採用するデータだけ切り替える」設計は実装がシンプルになる反面、使っていない側のサービスが律儀に働き続けてしまう罠がある。** 今回のように、裏側で動き続けているサービスには明示的な一時停止の仕組みを用意しておくと安心です。
- **リトライの間隔は「復帰の速さ」と「ログの静かさ」のトレードオフ。** 指数バックオフは一見賢そうに見えますが、ユーザー体験に直結する箇所では固定間隔のほうが素直に良いこともあります。
- **Visual Studio の「例外がスローされました」通知は、catch されていても出ることがある。** 非同期処理の await 継続のたびに first-chance 例外が複数回通知されることがあるので、ログの件数だけで慌てず、実際に catch されているか・ハンドリングされているかを見るのが大事です。
- **世代番号（generation）でイベントの新旧を判定するパターンは、非同期のコールバックが絡む再起動処理との相性が良い。** 意図した停止と予期しない停止を区別できるので、今回のような「自動復旧はしたいけど、意図的な停止では復旧させたくない」というケースにぴったりでした。

PlayMuse と MuseWiser、見た目にはただのレベルメーターの連動ですが、裏側では排他モードならではの細かい調整がいろいろと必要でした。今後、PlayMuse 側で新しい情報（例えば再生中の曲情報など）をパイプで渡したくなったときのために、今回整理した設計をベースに拡張していきたいと思います。