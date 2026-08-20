# WASAPI 排他モードでビットパーフェクト再生する音楽プレーヤーを .NET 10 C# で作ってみた

Windows C# WPF NAudio WASAPI 

## はじめに

普段からロスレス音源を中心に音楽をよく聴いています。楽曲配信サービスとしては Spotify をメインに利用しており、2026年3月頃より排他モードが実装されて最大44.1kHz/24bitのロスレス音源をビットパーフェクトで再生できるようになりました。

また、サンプリング周波数が96kHzや192kHzなどのいわゆるハイレゾ音源というものを再生したい場合、ハイレゾ音源を購入して DAP(FiiO M21) や DAC(FiiO BTR17) などで聴いていますが、Windows 上で動作するビットパーフェクト再生可能な音楽プレーヤーアプリを自分好みに作ってみたくなったので、「ファイルに記録された音をそのまま出力する」こととシンプルな見た目に拘った、WASAPI 排他モードを利用するビットパーフェクト再生可能な WPF デスクトップアプリ **PlayMuse** を作ってみました。

せっかくのロスレス音源であっても、Windows の共有モードでリサンプリングやビット深度変換が行われるのはもったいないので、デジタルの間だけでも原音忠実に自作アプリで再生してみたいと思った次第です。

## 作ったもの

https://github.com/kerobot/PlayMuse

PlayMuse は、MP3 / FLAC / WAV / AAC / M4A を再生できるシンプルな WPF デスクトップアプリです。単なる再生機能にとどまらず、NAudio の WASAPI 排他モードと自作のフォーマット解決ロジックを組み合わせることで、OS のミキサーによるリサンプリングやビット深度変換を避ける再生を実現しています。

主な機能は以下の通りです。

- MP3 / FLAC / WAV / AAC / M4A の再生、再生・一時停止・停止・シーク・音量調整
- メディアキー（音量UP/DOWN・再生/一時停止・前曲・次曲）によるハードウェアキーボード操作対応
- プレイリスト管理（トラックの追加・削除・並び替え、D&D による曲順変更・ファイル追加）
- プレイリストの保存／読み込みと、前回開いていたプレイリストの自動復元
- 出力デバイスの一覧表示・切り替え（USB DAC を含む WASAPI レンダーエンドポイントの列挙）
- 共有モード／排他モードの切り替え、デバイスの抜き差し・既定デバイス変更のリアルタイム検知
- ID3 / Vorbis Comment からのメタデータ・アルバムアート取得
- 再生中フォーマット（サンプルレート・ビット深度・チャンネル数・リサンプリングの有無）の可視化
- スペクトラムアナライザ表示（16バンドのLED風表示、ピークホールド付き）

いまのところは複雑な設定画面を設けず、ひとつの画面ですべての操作を行えるシンプルさを目指してみました。

## 開発環境

- Windows 11
- Visual Studio 2026 Professional
- .NET 10 / WPF / C#
- CommunityToolkit.Mvvm（MVVM 実装）
- NAudio（`NAudio.Wave` / `NAudio.CoreAudioApi`）
- BunLabs.NAudio.Flac（FLAC のネイティブデコード）
- Media Foundation（AAC / M4A のデコード、及び非ビットパーフェクト時のリサンプリング）
- NAudio.Dsp（スペクトラムアナライザのFFT演算）
- TagLibSharp（メタデータ・アルバムアート読み取り）
- xUnit（ユニットテスト）

## アーキテクチャ

- `PlayMuse.Core` — UI に依存しないコアロジック（サービス層・ViewModel・モデル）
- `PlayMuse` — WPF によるビュー層（`MainWindow` / コンバーター / プラットフォーム固有サービス）
- `PlayMuse.Tests` — `PlayMuse.Core` に対する単体テスト

`IAudioPlaybackService` / `IAudioDeviceService` / `IPlaylistService` / `IMetadataService` / `ISettingsService` などのインターフェースを介してサービスを注入する構成にしており、WPF に依存する `IDispatcherService` / `IFileDialogService` はアプリ層で実装を差し替えられるようにしています。

## ビットパーフェクト再生とは

ビットパーフェクト再生とは、音楽ファイルに記録されたデジタルサンプル値を、リサンプリング・ビット深度変換・音量による波形加工などを一切行わずに、そのままオーディオデバイスへ送り届ける再生方式です。

Windows の既定の音声パイプラインは、共有モードで動作する際にデバイスの共有フォーマットへ自動的にミキシング・リサンプリングを行うため、通常の再生ではファイルそのものの値がスピーカーや DAC まで届きません。ビットパーフェクト再生を行うには、OS のミキサーを経由しない専用の出力経路（WASAPI 排他モード）が必要になります。

「デジタル伝送だから劣化しない」というのは半分正しく半分誤りな感じで、実際には以下のような要因で「送り出す値そのもの」が変わってしまうことがあります。

- OS 側のミキシング・リサンプリング（共有モードでの共通フォーマットへの変換）
- アプリ側での不要な float 変換や音量調整によるサンプル値の書き換え
- 中間段（USB DAC 等）でのリサンプリング

PlayMuse では、WASAPI 排他モードでデバイスが受け付け可能なフォーマットとファイルのフォーマットを完全一致させ、OS のミキサーやアプリ内の不要な変換を排除することで、PC から最初のデジタル出力機器へ渡すサンプル値をファイルの値と完全に一致させることを目指しています。といっても、耳で明確に違いがわかるようなものではないですけどね…。

### 再生環境の例で見るデジタル区間とアナログ区間

例えば、以下のような構成でオーディオを再生する場合を考えます。

```
[Windows PC 上の PlayMuse]
        │  USBケーブル (デジタル: PCM ビットストリーム)
        ▼
[Creative Sound Blaster G8]  ← USB DAC を USB to S/PDIF コンバーターとして利用
        │  光デジタルケーブル (デジタル: PCM ビットストリーム)
        ▼
[DENON RCD-M41]  ← ディスクリートアンプ搭載CDレシーバーを DAC 兼アンプとして利用
        │  スピーカーケーブル (アナログ: 電気信号 = 電圧の連続的な変化)
        ▼
[DENON SC-M41 / YAMAHA NS-SW050]  ← スピーカーおよびサブウーファー
        │  空気振動 (アナログ: 音波)
        ▼
       耳
```

この構成では、**PlayMuse → Sound Blaster G8 → DENON RCD-M41 までがデジタル区間**です。USB や光デジタル (S/PDIF) で伝送されている間は、音は「0 と 1 の数値の列（PCM サンプル値）」として扱われています。

**DENON RCD-M41 が内蔵 DAC でデジタル信号をアナログ電気信号に変換した以降、つまりディスクリートパワーアンプからスピーカーケーブルを通ってスピーカーユニット・ウーファーまでがアナログ区間**です。この区間では電圧の連続的な変化・音波として伝わるため、アンプの回路やケーブルの特性、スピーカーの物理特性によって音質が変化しうる領域になります。耳で聴こえる音質にもっとも影響する部分です。

ちなみに、Sound Blaster G8 はUSB入力2系統で2台の PC、S/PDIF 入力1系統でオーディオデバイス、LINE 入力1系統でカセットプレーヤーなどを接続できるので、PC からの音楽再生だけでなく、複数音源を同時にミックスして出力できるところが気に入ってます。（排他モードではない場合）

もうひとつのパターンとして、Fiio DM15 R2R のような USB DAC を使う場合は、**PlayMuse → FiiO DM15 R2R までがデジタル区間**で、**FiiO DM15 R2R のヘッドホンアンプ → audio-technica ATH-R70x のヘッドホンまでがアナログ区間**になります。

## 対応する音楽ファイル形式と、その扱いの違い

対応形式ごとに `NativeAudioFileReaderFactory` がデコード方式を切り替えています。

| 形式 | デコード方法 | 特徴 |
|---|---|---|
| WAV | `WaveFileReader` | コンテナの PCM / IEEE Float データをそのまま読み取る。ロスレスかつ変換なし。 |
| MP3 | `Mp3FileReader` | デコード結果は常に 16bit PCM 相当（フォーマット自体が非可逆圧縮のため）。 |
| FLAC | `NAudio.Flac.FlacReader`（`BunLabs.NAudio.Flac`） | FLAC の STREAMINFO を直接参照し、Media Foundation を介さずに元のビット深度（16/24bit）の整数 PCM を復号する。 |
| AAC / M4A | `MediaFoundationReader`（`RequestFloatOutput = false`） | Media Foundation のデコーダーを利用し、可能な限り元のビット深度の整数 PCM を要求する（可逆圧縮のため元のビットパーフェクトは保証されない）。 |

NAudio が提供する汎用の `AudioFileReader` は、内部で必ず IEEE Float 32bit の `ISampleProvider` パイプラインへ変換しているようです。これは扱いやすい反面、24bit FLAC のような高解像度音源でも一度 32bit float へ変換されるため、後段でビットパーフェクト判定を行うための「元のビット深度」という情報が失われてしまいます。

さらに FLAC に限っていえば、Media Foundation の FLAC デコーダーは必ずしも元のビット深度（24bit など）を保ったままデコードしてくれる保証がありません。そこで PlayMuse では FLAC のみ `BunLabs.NAudio.Flac` パッケージの `NAudio.Flac.FlacReader` を利用し、STREAMINFO ブロックから直接サンプルレート・ビット深度を取得した上でネイティブの整数 PCM を復号します。

そのため `AudioFileReader` は使わず、形式ごとに最適な `WaveStream` を直接選択するファクトリを実装しました。

```csharp
internal static class NativeAudioFileReaderFactory
{
	public static WaveStream Create(string filePath)
	{
		var extension = Path.GetExtension(filePath);

		if (string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase))
		{
			// WAVはコンテナのPCM/Floatデータをそのまま保持するため、WaveFileReaderで直接読み込む。
			return new WaveFileReader(filePath);
		}

		if (string.Equals(extension, ".mp3", StringComparison.OrdinalIgnoreCase))
		{
			// Mp3FileReaderはデコード結果を素のPCM（通常16bit）のまま返す。
			return new Mp3FileReader(filePath);
		}

		if (string.Equals(extension, ".flac", StringComparison.OrdinalIgnoreCase))
		{
			// Media FoundationはFLACの元のビット深度を保証しないため、
			// STREAMINFOを直接参照するFlacReaderでネイティブデコードする。
			return new FlacReader(filePath);
		}

		// AAC/M4A等はMedia Foundationでデコードする。
		// RequestFloatOutput = false により、可能な限り元のビット深度の整数PCMを要求する。
		var settings = new MediaFoundationReader.MediaFoundationReaderSettings
		{
			RequestFloatOutput = false,
		};

		return new MediaFoundationReader(filePath, settings);
	}
}
```

## WASAPI 排他モードでのフォーマット解決

排他モードで再生する際は、ファイルのフォーマット（サンプルレート・ビット深度・チャンネル数・エンコーディング）とデバイスが受け付け可能なフォーマットを比較し、完全一致すればそのまま出力、一致しなければリサンプリングにフォールバックします。

```csharp
private void InitializeOutputCore(AudioClientShareMode shareModeNative)
{
	// PcmVolumeProviderは音量が最大(1.0)の間は無加工でパススルーするため、
	// ここでラップしてもビットパーフェクト再生を妨げない。
	volumeProvider = new PcmVolumeProvider(reader!) { Volume = desiredVolume };
	IWaveProvider waveProvider = volumeProvider;

	if (shareModeNative == AudioClientShareMode.Exclusive)
	{
		var fileFormat = reader!.WaveFormat;

		// ファイルのフォーマットを排他モードでデバイスが直接サポートするか確認（ビットパーフェクト判定）。
		var bitPerfectFormat = ResolveBitPerfectFormat(currentMMDevice!, fileFormat);

		if (bitPerfectFormat is not null)
		{
			// ファイルフォーマット（またはそのExtensible相当）をそのまま出力（リサンプリング不要）
			OutputFormat = bitPerfectFormat;
		}
		else
		{
			// ビットパーフェクト不可: デバイスが対応する最も近いフォーマットを探索してリサンプリング
			var bestDeviceFormat = FindBestSupportedFormat(currentMMDevice!, fileFormat);

			if (bestDeviceFormat is not null)
			{
				resampler = new MediaFoundationResampler(volumeProvider, bestDeviceFormat);
				waveProvider = resampler;
				IsResampling = true;
				OutputFormat = bestDeviceFormat;
			}
			else
			{
				// 対応フォーマットが見つからない場合はMixFormat（デバイスのネイティブ共有フォーマット）へリサンプリング
				var mixFormat = currentMMDevice!.AudioClient.MixFormat;
				resampler = new MediaFoundationResampler(volumeProvider, mixFormat);
				waveProvider = resampler;
				IsResampling = true;
				OutputFormat = mixFormat;
			}
		}
	}
	else
	{
		// 共有モードの場合、WindowsがミキシングするためOutputFormatはMixFormat
		OutputFormat = currentMMDevice?.AudioClient.MixFormat;
	}

	var newOutput = new WasapiOut(currentMMDevice, shareModeNative, true, LatencyMilliseconds);
	newOutput.Init(waveProvider);
	output = newOutput;
}
```

排他モードの完全一致判定は `ResolveBitPerfectFormat` が担当します。`MediaFoundationReader` 等が返すフォーマットは非 Extensible の場合があるため、素の形式で拒否された場合は Extensible 相当の形式でも試すようにしています（サンプルデータ自体は変わらないため、どちらが採用されてもビットパーフェクト性は保たれます）。

```csharp
private static WaveFormat? ResolveBitPerfectFormat(MMDevice device, WaveFormat fileFormat)
{
	if (IsFormatSupportedSafe(device, fileFormat))
	{
		return fileFormat;
	}

	if (fileFormat is not WaveFormatExtensible)
	{
		var extensible = new WaveFormatExtensible(fileFormat.SampleRate, fileFormat.BitsPerSample, fileFormat.Channels);
		if (IsFormatSupportedSafe(device, extensible))
		{
			return extensible;
		}
	}

	return null;
}

private static bool IsFormatSupportedSafe(MMDevice device, WaveFormat format)
{
	try
	{
		return device.AudioClient.IsFormatSupported(AudioClientShareMode.Exclusive, format);
	}
	catch
	{
		return false;
	}
}
```

また、コンテナビット数だけが異なる「24-in-32」のようなロスレス再パッキングも区別する必要があるため、互換性判定は `ExactFormatMatch` / `LosslessRepacking` / `RequiresConversion` の3段階で行っています。

```csharp
private enum FormatCompatibility
{
	ExactFormatMatch,
	LosslessRepacking,
	RequiresConversion,
}

private static FormatCompatibility ClassifyFormatCompatibility(WaveFormat source, WaveFormat target)
{
	if (source.SampleRate != target.SampleRate || source.Channels != target.Channels)
	{
		return FormatCompatibility.RequiresConversion;
	}

	var sourceIsFloat = IsFloatEncoding(source);
	var targetIsFloat = IsFloatEncoding(target);

	if (sourceIsFloat != targetIsFloat)
	{
		return FormatCompatibility.RequiresConversion;
	}

	var sourceValidBits = GetValidBits(source);
	var targetValidBits = GetValidBits(target);

	if (source.BitsPerSample == target.BitsPerSample && sourceValidBits == targetValidBits)
	{
		return FormatCompatibility.ExactFormatMatch;
	}

	// 24bit整数PCMの実効データ（ValidBits=24）を32bitコンテナへ左詰め格納する
	// 「24-in-32」へのロスレス再パッキングのみ対応する。
	if (!sourceIsFloat && sourceValidBits == 24 && targetValidBits == 24 && target.BitsPerSample == 32)
	{
		return FormatCompatibility.LosslessRepacking;
	}

	return FormatCompatibility.RequiresConversion;
}
```

## デバイスによっては 24bit が「32bit」として扱われる

排他モードの対応状況をログに出して確認していたところ、実際に Fiio DM15 R2R という USB DAC を USB DAC モードで接続した際、24bit/48kHz の音源が **16bit** でしか再生されない現象に遭遇しました。同じ構成で Sound Blaster G8 に繋いだ場合は問題なく 24bit で再生できていたので、原因はデバイス側のフォーマット対応にありそうだと当たりを付けて調査しました。

`WAVEFORMATEXTENSIBLE` 構造体は、1 サンプルあたりのメモリ上のサイズ（コンテナビット数 `wBitsPerSample`）と、実際に意味を持つビット数（有効ビット数 `wValidBitsPerSample`）を別々に持てます。多くの USB DAC は 24bit のサンプルを「24-in-24」（コンテナと有効ビットが 24bit、3 バイト/サンプル）としてそのまま受け付けますが、内部の DSP／転送チップが 32bit 単位でのデータ処理を前提に設計されている機種では、**「24-in-32」（コンテナ 32bit・有効ビット 24bit、4 バイト/サンプルで下位 1 バイトは 0 埋め）という PCM パッキング形式のみを排他モードで受理する**ことがあります。

従来の `ResolveBitPerfectFormat` は「24-in-24」しか候補にしていなかったため、この形式のみに対応する DAC では全ての候補が `IsFormatSupported` に失敗し、最終的に 16bit まで格下げされてフォールバックしていた、というのが今回の原因でした。サンプル値そのものは変わらず、単に格納先のメモリレイアウトが変わるだけなので、この形式を候補に追加してもビットパーフェクト性は損なわれません。保守性は低下しますが、今回は楽曲の再生方法について理解を深めるため、リフレクションで非公開フィールドを書き換えて「24-in-32」フォーマットを生成する方法を採用しました。

### 24-in-32 フォーマットの生成

NAudio の `WaveFormatExtensible` の公開コンストラクタは、コンテナビット数と有効ビット数を常に同一値にしてしまいます（`bits` を 32 で構築すると SubFormat も IEEE Float になってしまいます）。そこで、非公開フィールドをリフレクションで直接書き換えることで「コンテナ 32bit・有効ビット 24bit・SubFormat=PCM」の組み合わせを作っています。

```csharp
private static readonly FieldInfo WaveFormatExtensibleValidBitsField =
	typeof(WaveFormatExtensible).GetField("wValidBitsPerSample", BindingFlags.NonPublic | BindingFlags.Instance)!;

private static readonly FieldInfo WaveFormatExtensibleSubFormatField =
	typeof(WaveFormatExtensible).GetField("subFormat", BindingFlags.NonPublic | BindingFlags.Instance)!;

private static WaveFormatExtensible CreatePacked24In32Format(int sampleRate, int channels)
{
	// コンテナ32bitで生成することで blockAlign / averageBytesPerSecond を
	// 32bitコンテナ基準で正しく計算させる。
	var format = new WaveFormatExtensible(sampleRate, 32, channels);

	// 既定では bits=32 により IEEE Float の SubFormat が設定されるため、
	// PCM の SubFormat へ上書きし、有効ビット数を24に設定する。
	WaveFormatExtensibleSubFormatField.SetValue(format, PcmSubFormatGuid);
	WaveFormatExtensibleValidBitsField.SetValue(format, (short)24);

	return format;
}
```

`ResolveBitPerfectFormat` は、ファイルが 24bit PCM の場合にこの「24-in-32」もビットパーフェクト候補として `IsFormatSupported` で確認するように拡張しました。`FindBestSupportedFormat` によるフォールバック探索でも、16bit へ格下げされる前にこの形式を試すようにしています。

### 無劣化なコンテナ変換

24-in-32 が採用された場合、デコード結果（24bit, 3 バイト/サンプル）をそのまま `WasapiOut` へ渡すことはできません。サンプル値は変えずにメモリレイアウトだけを 32bit コンテナへ変換する、軽量な `IWaveProvider` を挟みます。

```csharp
internal sealed class Pack24In32WaveProvider(IWaveProvider source, WaveFormat packedFormat) : IWaveProvider
{
	private byte[] sourceBuffer = [];

	public WaveFormat WaveFormat { get; } = packedFormat;

	public int Read(byte[] buffer, int offset, int count)
	{
		// 出力(4バイト/サンプル)の要求量に対応する、ソース側(3バイト/サンプル)の読み取り量を算出する。
		var sampleCount = count / 4;
		var sourceBytesNeeded = sampleCount * 3;

		if (sourceBuffer.Length < sourceBytesNeeded)
		{
			sourceBuffer = new byte[sourceBytesNeeded];
		}

		var sourceBytesRead = source.Read(sourceBuffer, 0, sourceBytesNeeded);
		var samplesRead = sourceBytesRead / 3;

		for (var i = 0; i < samplesRead; i++)
		{
			var srcIndex = i * 3;
			var dstIndex = offset + (i * 4);

			// 24bit有効データを32bitコンテナへ左詰め格納（下位1バイトは0埋め）。
			buffer[dstIndex] = 0;
			buffer[dstIndex + 1] = sourceBuffer[srcIndex];
			buffer[dstIndex + 2] = sourceBuffer[srcIndex + 1];
			buffer[dstIndex + 3] = sourceBuffer[srcIndex + 2];
		}

		return samplesRead * 4;
	}
}
```

`InitializeOutputCore` では、採用したビットパーフェクトフォーマットのコンテナビット数がファイル本来のビット数と異なる場合（＝24-in-32 採用時）に、この `Pack24In32WaveProvider` を出力チェーンへ挿入します。`MediaFoundationResampler` によるリサンプリングとは異なり、サンプル値の丸めや補間は一切行わないため `IsResampling` は `false` のままです。

### UI 上での見え方の工夫

24-in-32 を採用すると `OutputFormat.BitsPerSample` は 32 になるため、UI に単純に「32bit」とだけ表示すると「24bit のはずなのに 32bit に変換されている」という誤解を招きます。そこで、採用フォーマットが PCM か Float か、コンテナビット数と有効ビット数が異なるかを文字列化する `OutputFormatLabel` を追加し、`Extensible/PCM(24-in-32)` のような内訳を「出力」情報に併記するようにしました。

```
🔊 出力: 48 kHz / 32 bit / 2 ch / Extensible (Extensible/PCM(24-in-32))
💎 ✓ ビットパーフェクト再生
```

これにより、実機（Fiio DM15 R2R、USB DAC モード・排他モード）でも 24bit 音源をビットパーフェクトで再生しつつ、UI 上でその実態（32bit コンテナへ無劣化で格納されているだけであること）を正しく伝えられるようになりました。

## ボリューム処理でも波形を破壊しない

音量を下げる操作は本来、サンプル値を書き換える処理です。しかし音量が最大のときにまで無駄にスケーリング処理を挟んでしまうと、浮動小数点演算による微小な誤差が生じかねません。そこで `PcmVolumeProvider` では、音量が完全に最大値（`Volume >= 1.0f`）のときのみ一切サンプルへ手を加えずにスルーし、音量を下げた場合のみ 8/16/24/32bit PCM および IEEE Float それぞれに対応したスケーリング処理を行うようにしています。`Volume` は `[0, 1]` にクランプされているため、これは実質的に厳密な unity-gain 判定になります。

```csharp
internal sealed class PcmVolumeProvider(IWaveProvider source) : IWaveProvider
{
	public float Volume { get; set; } = 1.0f;

	public int Read(byte[] buffer, int offset, int count)
	{
		var bytesRead = source.Read(buffer, offset, count);

		if (bytesRead <= 0 || Volume >= 1.0f)
		{
			// Volumeは[0,1]にクランプされているため、これは厳密なunity-gain判定になる。
			// 最大音量時はデコード結果をそのまま出力し、ビットパーフェクトを保つ。
			return bytesRead;
		}

		ApplyVolume(buffer, offset, bytesRead);
		return bytesRead;
	}

	private void ApplyVolume(byte[] buffer, int offset, int count)
	{
		var format = WaveFormat;
		var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
			(format is WaveFormatExtensible extensible && extensible.SubFormat == IeeeFloatSubFormatGuid);

		if (isFloat)
		{
			ScaleFloat(buffer, offset, count);
			return;
		}

		switch (format.BitsPerSample)
		{
			case 8: ScaleUInt8(buffer, offset, count); break;
			case 16: ScaleInt16(buffer, offset, count); break;
			case 24: ScaleInt24(buffer, offset, count); break;
			case 32: ScaleInt32(buffer, offset, count); break;
		}
	}
	// ...(各ビット深度ごとのスケーリング処理は省略)
}
```

## 排他モード非対応時の安全なフォールバック

デバイスや形式によっては排他モードでの初期化自体に失敗することがあります（`AUDCLNT_E_UNSUPPORTED_FORMAT` / `AUDCLNT_E_DEVICE_IN_USE` / `AUDCLNT_E_EXCLUSIVE_MODE_NOT_ALLOWED` など）。これらの HResult を判定し、原因別のメッセージをユーザーに提示した上で自動的に共有モードへフォールバックし、再生自体は継続できるようにしています。

また、出力先デバイスが物理的に切断された場合に発生する `AUDCLNT_E_DEVICE_INVALIDATED` は個別に判定し、`AudioErrorKind.DeviceDisconnected` として一般的な再生エラーと区別して通知するようにしました。ViewModel 側ではこの種別を見て、既定デバイスへの自動切り替えなど適切なリカバリー処理を行えるようにしています。

## デバイス切り替え・排他モード切り替えへの対策

- **デバイス列挙とホットプラグ検知**  
  `MMDeviceEnumerator` でレンダーエンドポイントを列挙するとともに、`IMMNotificationClient` を実装してデバイスの追加・削除・状態変化・既定デバイス変更を監視し、`DevicesChanged` イベントで UI へ通知します。これにより USB DAC の抜き差しや Bluetooth 機器の接続にも追従できます。

- **再生を継続したままのシームレスな切り替え**  
  出力デバイスや共有モードの切り替えは、いずれも `ReinitializeOutputPreservingState` を呼び出します。現在の再生状態と再生位置を保持したまま出力だけを再構築する処理で、切り替え前に再生中であればシーク位置を復元して再生を再開し、そうでなければ停止状態を維持します。

## WAV の互換性問題への対応

24bit/32bit float を含む `WAVE_FORMAT_EXTENSIBLE` 形式の WAV は、環境によっては Audio Compression Manager (ACM) 経由の変換に失敗し `NoDriver calling acmFormatSuggest` 例外で再生できないことがあります。これに対応するため `WavFormatNormalizer` が事前にヘッダーを解析し、SubFormat が PCM / IEEE Float であれば非 Extensible 形式の一時ファイルへ書き換えることで、データ自体を変更せずに互換性の問題を回避しています。

## ビットパーフェクト再生に関する制約・注意点

ビットパーフェクト再生は万能ではなく、以下の条件下では成立しない、あるいは意図的にトレードオフが発生します。実装の透明性のため、該当箇所とあわせて明記します。

- **音量を下げるとビットパーフェクトではなくなる**  
  音量を下げた場合は各ビット深度に応じたスケーリング処理が入るため、その時点でファイル本来の値とは異なる値がデバイスへ送られます。真にビットパーフェクトな再生を行いたい場合は、アプリの音量を最大にし、音量調整は外部アンプ側で行うことを推奨します。

- **MP3 はそもそもビットパーフェクトの対象ではない**  
  MP3 は非可逆圧縮フォーマットのため、劣化はエンコード時点で既に発生しています。「デコード後の値をそのまま送る」という意味でのビットパーフェクトは成立しますが、WAV/FLAC のような「原音を完全に保持した再生」という文脈でのビットパーフェクトとは意味合いが異なります。

- **共有モードではビットパーフェクトを保証しない**  
  共有モードの場合、実際のミキシング・リサンプリングは Windows のオーディオエンジンに委ねられます。ビットパーフェクト再生を行うには、設定で排他モードを選択する必要があります。

- **排他モードでもデバイスが対応フォーマットを持たない場合はリサンプリングされる**  
  ファイルと完全一致するフォーマットが見つからない場合、`MediaFoundationResampler` を用いた変換が行われ、`IsResampling` が `true` になります。この場合も再生自体は継続されますが、厳密な意味でのビットパーフェクトではなくなります。UI 上のフォーマット表示（サンプルレート・ビット深度・リサンプリングの有無）で、現在の再生がビットパーフェクトかどうかを確認できます。

- **排他モードの初期化に失敗した場合の自動フォールバック**  
  他アプリによるデバイスの排他使用中や、デバイスが該当フォーマットでの排他モードに対応していない場合は、共有モードへ自動フォールバックして再生を継続します。この場合もビットパーフェクトではなくなりますが、エラーメッセージにより状況をユーザーへ明示します。

## スペクトラムアナライザの実装

ビットパーフェクト再生に加えて、再生中の音声をリアルタイムに解析して16バンドのLED風スペクトラムバーを表示する機能も実装しています。単に「見た目が賑やかになる」だけでなく、実際にWASAPI出力へ渡している波形をそのまま解析対象にすることで、音量調整やリサンプリング後の「実際にデバイスへ送られる音」を可視化できるようにしています。そのためアプリ側の音量調整によりバーの高さが変化します。

### 出力パイプラインへの透過的な分岐

`SpectrumTapProvider` を `WasapiOut.Init()` 直前に挿入し、再生データをそのまま出力しつつ、コピーを解析サービスへ渡します。再生経路そのものには一切手を加えないため、ビットパーフェクト性を損ないません。

```csharp
internal sealed class SpectrumTapProvider(IWaveProvider source, ISpectrumAnalyzerService spectrumAnalyzer) : IWaveProvider
{
	public WaveFormat WaveFormat => source.WaveFormat;

	public int Read(byte[] buffer, int offset, int count)
	{
		var bytesRead = source.Read(buffer, offset, count);

		if (bytesRead > 0)
		{
			spectrumAnalyzer.PushSamples(buffer.AsSpan(offset, bytesRead), WaveFormat);
		}

		return bytesRead;
	}
}
```

### バッファ → FFT → マグニチュード → デシベル → 表示レベル

解析サービス（`SpectrumAnalyzerService`）は、以下の流れで16バンド分の表示レベルを算出します。

1. **バッファリング**: 受け取った波形を全チャンネル平均でモノラル化し、リングバッファへ蓄積する。
2. **FFT**: サンプルレートに応じた可変長（2048〜16384）でHamming窓を適用し、`NAudio.Dsp.FastFourierTransform` で高速フーリエ変換を実行する。
3. **マグニチュード算出**: FFT結果の複素数から各周波数ビンの振幅を求め、窓関数のゲイン低下とNAudio側のスケーリングを補正する正規化係数を掛け合わせる。
4. **デシベル変換とバンド集約**: 16の周波数帯域ごとに最大振幅を採用し、`20 * log10(振幅)` でデシベル値に変換する。
5. **表示レベルへの変換**: デシベル値を0〜10段階に正規化した後、アタック/リリース方式の平滑化（上昇は速く、下降はゆっくり）とピークホールドを適用し、なめらかな表示値を生成する。

```csharp
private SpectrumBandLevel[] BuildLevels(double[] bandDecibels)
{
	var result = new SpectrumBandLevel[BandCount];

	for (var b = 0; b < BandCount; b++)
	{
		var clamped = Math.Clamp(bandDecibels[b], MinDecibel, MaxDecibel);
		var normalized = (clamped - MinDecibel) / (MaxDecibel - MinDecibel);
		var target = normalized * LevelSteps;

		// 上昇は速く、下降はゆっくり追従させることで、フレームごとの瞬間値の変動を
		// 平滑化し、バーの動きをなめらかに見せる（アタック/リリース方式）。
		var factor = target > smoothedLevels[b] ? LevelAttackFactor : LevelReleaseFactor;
		smoothedLevels[b] += (target - smoothedLevels[b]) * factor;

		var level = smoothedLevels[b];

		// ピークホールド：現在値がピークを超えたら即座に更新し、超えない場合は
		// 一定量ずつゆっくり減衰させる。
		peakLevels[b] = level > peakLevels[b] ? level : Math.Max(level, peakLevels[b] - PeakDecayPerUpdate);

		result[b] = new SpectrumBandLevel(
			Math.Clamp(level, 0, LevelSteps),
			Math.Clamp(peakLevels[b], 0, LevelSteps));
	}

	return result;
}
```

### 一時停止・停止時になめらかにゼロへ戻す

一時停止や停止で音声データの供給が止まると、キャプチャバッファの古い波形を再利用してしまい、表示が最後の値のまま残ってしまう問題がありました。そこで、最終サンプル受信時刻からの経過時間を見て、一定時間（150ms）以上新しいデータが届いていない場合は「無音」を入力として同じ平滑化処理に流し込むようにしています。これにより、表示が瞬時にゼロになるのではなく、既存のリリース（減衰）係数に従ってなめらかにゼロへ近づきます。単純に「前回呼び出し以降にデータが来たか」というフラグ判定にすると、オーディオコールバックとUI側ポーリングの間隔のずれで瞬間的に誤検出し、通常再生中でもバーが上下に振動してしまうため、経過時間ベースの判定を採用しています。

```csharp
isSilentTimeout = lastSampleTicks == NoSampleReceivedTicks
	|| Environment.TickCount64 - lastSampleTicks > SilenceTimeout.TotalMilliseconds;
```

## テスト

`PlayMuse.Tests` では ViewModel やサービスの単体テストを xUnit で実装しています。ループ再生の伝播、プレイリストの読み込み結果、デバイス切り替え時の挙動など、コアロジックの振る舞いを検証しています。

## おわりに

普段何気なく聴いている音楽も、実は OS のオーディオパイプラインの中で意外と値が変換されていることが今回の実装を通してよく分かりました。

WASAPI 排他モードや `WaveFormatExtensible` の SubFormat 比較、24-in-32 PCM パッキング形式など、普段の開発ではあまり触れない領域だったため苦労しましたが、その分「ファイルに記録された値をそのまま送り届ける」という当初の目標を形にできたのは良い経験になりました。

アプリの外観やスペクトラムアナライザー表示についてもこだわったので、見た目的にもメロい音楽プレーヤーアプリができたと思います。今後はリピート再生やシャッフル再生、ギャップレス再生など、音楽をさらに楽しむ機能を追加してみたいです。