# WASAPI 排他モードでビットパーフェクト再生を追求する音楽プレーヤーを WPF で作ってみた

- Windows
- C#
- WPF
- NAudio
- WASAPI

## はじめに

普段からロスレス音源（FLAC）を中心に音楽を聴いています。楽曲配信サービスとしては Spotify を利用しており、2026年3月頃から排他モードが実装されてロスレスのビットパーフェクト再生が行えるようになりました。

Windows 向けとしては foobar2000 などのビットパーフェクト再生可能なアプリケーションも数多くありますが、仕組みを理解しつつ自分好みの音楽プレーヤーアプリを作ってみたくなったので、「ファイルに記録された音をそのまま出力する」ことにこだわった、WASAPI 排他モードを利用するビットパーフェクト再生専用の WPF デスクトップアプリ **PlayMuse** を作ってみました。

せっかくのロスレス音源であっても、Windows の共有モードでリサンプリングやビット深度変換が行われるのはもったいないので、デジタルの間だけでも原音忠実に再生してみたいと思った次第です。

## 作ったもの

https://github.com/kerobot/PlayMuse

PlayMuse は、MP3 / FLAC / WAV / AAC / M4A を再生できるシンプルな WPF デスクトップアプリです。単なる再生機能にとどまらず、NAudio の WASAPI 排他モードと自作のフォーマット解決ロジックを組み合わせることで、OS のミキサーによるリサンプリングやビット深度変換を避ける再生を実現しています。

主な機能は以下の通りです。

- MP3 / FLAC / WAV / AAC / M4A の再生、再生・一時停止・停止・シーク・音量調整
- プレイリスト管理（トラックの追加・削除・並び替え、D&D による曲順変更・ファイル追加）
- プレイリストの保存／読み込みと、前回開いていたプレイリストの自動復元
- 出力デバイスの一覧表示・切り替え（USB DAC を含む WASAPI レンダーエンドポイントの列挙）
- 共有モード／排他モードの切り替え、デバイスの抜き差し・既定デバイス変更のリアルタイム検知
- ID3 / Vorbis Comment からのメタデータ・アルバムアート取得
- 再生中フォーマット（サンプルレート・ビット深度・チャンネル数・リサンプリングの有無）の可視化

## 開発環境

- Windows 11
- Visual Studio 2026 Professional
- .NET 10 / WPF
- CommunityToolkit.Mvvm（MVVM 実装）
- NAudio（`NAudio.Wave` / `NAudio.CoreAudioApi`）
- Media Foundation（FLAC / AAC / M4A のデコード）
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

「デジタル伝送だから劣化しない」というのは半分正しく半分は誤りで、実際には以下のような要因で「送り出す値そのもの」が変わってしまうことがあります。

- OS 側のミキシング・リサンプリング（共有モードでの共通フォーマットへの変換）
- アプリ側での不要な float 変換や音量調整によるサンプル値の書き換え
- 中間段（USB DAC 等）でのリサンプリング

PlayMuse では、WASAPI 排他モードでデバイスが受け付け可能なフォーマットとファイルのフォーマットを完全一致させ、OS のミキサーやアプリ内の不要な変換を排除することで、PC から最初のデジタル出力機器へ渡すサンプル値をファイルの値と完全に一致させることを目指しています。といっても、耳で明確に違いがわかるようなものではないですけどね…。

### 再生環境の例で見るデジタル区間とアナログ区間

例えば、以下のような構成でオーディオを再生する場合を考えます。

```
[Windows PC 上の PlayMuse]
        │  USB (デジタル: PCM ビットストリーム)
        ▼
[Sound Blaster G8]  ← USB DAC / ヘッドホンアンプ
        │  光デジタル (S/PDIF, デジタル: PCM ビットストリーム)
        ▼
[DENON RCD-M41]  ← DAC 内蔵レシーバー／CD レシーバー
        │  スピーカーケーブル (アナログ: 電気信号 = 電圧の連続的な変化)
        ▼
[DENON SC-M41 / YAMAHA NS-SW050]  ← スピーカー
        │  空気振動 (アナログ: 音波)
        ▼
       耳
```

この構成では、**PlayMuse → Sound Blaster G8 → DENON RCD-M41 までがデジタル区間**です。USB や光デジタル (S/PDIF) で伝送されている間は、音は「0 と 1 の数値の列（PCM サンプル値）」として扱われています。

**DENON RCD-M41 が内蔵 DAC でデジタル信号をアナログ電気信号に変換した以降、つまりスピーカーケーブルからスピーカーユニット・ウーファーまでがアナログ区間**です。ここから先は電圧の連続的な変化・音波として伝わるため、ケーブルの特性やアンプの回路、スピーカーの物理特性によって音質が変化しうる領域になります。

## 対応する音楽ファイル形式と、その扱いの違い

対応形式ごとに `NativeAudioFileReaderFactory` がデコード方式を切り替えています。

| 形式 | デコード方法 | 特徴 |
|---|---|---|
| WAV | `WaveFileReader` | コンテナの PCM / IEEE Float データをそのまま読み取る。ロスレスかつ変換なし。 |
| MP3 | `Mp3FileReader` | デコード結果は常に 16bit PCM 相当（フォーマット自体が非可逆圧縮のため）。 |
| FLAC / AAC / M4A | `MediaFoundationReader`（`RequestFloatOutput = false`） | Media Foundation のデコーダーを利用し、可能な限り元のビット深度の整数 PCM を要求する。 |

NAudio が提供する汎用の `AudioFileReader` は、内部で必ず IEEE Float 32bit の `ISampleProvider` パイプラインへ変換しているようです。これは扱いやすい反面、24bit FLAC のような高解像度音源でも一度 32bit float へ変換されるため、後段でビットパーフェクト判定を行うための「元のビット深度」という情報が失われてしまいます。

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

		// FLAC/AAC/M4A等はMedia Foundationでデコードする。
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

また、PCM と IEEE Float、`WaveFormatExtensible` の SubFormat の違いも考慮して互換性を判定する必要があります。

```csharp
private static bool AreFormatsCompatible(WaveFormat sourceFormat, WaveFormat deviceFormat)
{
	// 両方とも同じEncodingの場合は互換性あり
	if (sourceFormat.Encoding == deviceFormat.Encoding)
	{
		return true;
	}

	var sourceExt = sourceFormat as WaveFormatExtensible;
	var deviceExt = deviceFormat as WaveFormatExtensible;

	// ソースがIeeeFloat、デバイスがExtensibleの場合
	if (sourceFormat.Encoding == WaveFormatEncoding.IeeeFloat && deviceExt != null)
	{
		return deviceExt.SubFormat == IeeeFloatSubFormatGuid;
	}

	// デバイスがIeeeFloat、ソースがExtensibleの場合
	if (deviceFormat.Encoding == WaveFormatEncoding.IeeeFloat && sourceExt != null)
	{
		return sourceExt.SubFormat == IeeeFloatSubFormatGuid;
	}

	// 両方がExtensibleの場合、SubFormatを比較
	if (sourceExt != null && deviceExt != null)
	{
		return sourceExt.SubFormat == deviceExt.SubFormat;
	}

	return false;
}
```

## ボリューム処理でも波形を破壊しない

音量を下げる操作は本来、サンプル値を書き換える処理です。しかし音量が最大のときにまで無駄にスケーリング処理を挟んでしまうと、浮動小数点演算による微小な誤差が生じかねません。そこで `PcmVolumeProvider` では、音量が閾値（`0.999f`）以上のときは一切サンプルへ手を加えずにスルーし、音量を下げた場合のみ 8/16/24/32bit PCM および IEEE Float それぞれに対応したスケーリング処理を行うようにしています。

```csharp
internal sealed class PcmVolumeProvider(IWaveProvider source) : IWaveProvider
{
	// この値以上の音量は「実質最大音量」とみなし、サンプル加工を完全にスキップする。
	private const float BitPerfectThreshold = 0.999f;

	public float Volume { get; set; } = 1.0f;

	public int Read(byte[] buffer, int offset, int count)
	{
		var bytesRead = source.Read(buffer, offset, count);

		if (bytesRead <= 0 || Volume >= BitPerfectThreshold)
		{
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

- **MP3 はそもそもビットパーフェクトの対象になりにくい**  
  MP3 は非可逆圧縮フォーマットのため、劣化はエンコード時点で既に発生しています。「デコード後の値をそのまま送る」という意味でのビットパーフェクトは成立しますが、WAV/FLAC のような「原音を完全に保持した再生」という文脈でのビットパーフェクトとは意味合いが異なります。

- **共有モードではビットパーフェクトを保証しない**  
  共有モードの場合、実際のミキシング・リサンプリングは Windows のオーディオエンジンに委ねられます。ビットパーフェクト再生を行うには、設定で排他モードを選択する必要があります。

- **排他モードでもデバイスが対応フォーマットを持たない場合はリサンプリングされる**  
  ファイルと完全一致するフォーマットが見つからない場合、`MediaFoundationResampler` を用いた変換が行われ、`IsResampling` が `true` になります。この場合も再生自体は継続されますが、厳密な意味でのビットパーフェクトではなくなります。UI 上のフォーマット表示（サンプルレート・ビット深度・リサンプリングの有無）で、現在の再生がビットパーフェクトかどうかを確認できます。

- **排他モードの初期化に失敗した場合の自動フォールバック**  
  他アプリによるデバイスの排他使用中や、デバイスが該当フォーマットでの排他モードに対応していない場合は、共有モードへ自動フォールバックして再生を継続します。この場合もビットパーフェクトではなくなりますが、エラーメッセージにより状況をユーザーへ明示します。

## テスト

`PlayMuse.Tests` では ViewModel やサービスの単体テストを xUnit で実装しています。ループ再生の伝播、プレイリストの読み込み結果、デバイス切り替え時の挙動など、コアロジックの振る舞いを検証しています。

## おわりに

普段何気なく聴いている音楽も、実は OS のオーディオパイプラインの中で意外と値が変換されていることが今回の実装を通してよく分かりました。WASAPI 排他モードや `WaveFormatExtensible` の SubFormat 比較など、普段の開発ではあまり触れない領域だったため実装には苦労しましたが、その分「ファイルに記録された値をそのまま送り届ける」という当初の目標を形にできたのは良い経験になりました。

スペクトラムアナライザー表示や、歌詞表示、背景画像表示など、見た目的にも楽しい音楽プレーヤーアプリにしていきたいです。