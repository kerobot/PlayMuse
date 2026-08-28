using System.Reflection;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using PlayMuse.Core.Models;
using PlaybackState = PlayMuse.Core.Models.PlaybackState;

namespace PlayMuse.Core.Services;

/// <summary>
/// NAudioの<see cref="AudioFileReader"/>（デコード）と<see cref="WasapiOut"/>（WASAPI出力）を
/// 組み合わせて実装する再生サービス。
/// </summary>
public sealed class AudioPlaybackService(ILogger<AudioPlaybackService>? logger = null, ISpectrumAnalyzerService? spectrumAnalyzer = null) : IAudioPlaybackService
{
    private const int LatencyMilliseconds = 200;

    /// <summary>
    /// 排他モード解放後、Windowsオーディオエンジン側（特に外部SPDIF機器等）が
    /// 実際にロックを解放するまでの猶予時間（ミリ秒）。
    /// </summary>
    private const int ExclusiveModeReleaseDelayMilliseconds = 50;

    /// <summary>
    /// WASAPI排他モード関連のHResult値。
    /// NAudioの内部型(NAudio.CoreAudioApi.Interfaces.AudioClientErrorCode)と同値をここに転記し、
    /// 内部名前空間への直接依存を避ける。
    /// </summary>
    private static class ExclusiveModeHResult
    {
        public const int UnsupportedFormat = -2004287480;
        public const int DeviceInUse = -2004287478;
        public const int ExclusiveModeNotAllowed = -2004287474;

        /// <summary>
        /// AUDCLNT_E_DEVICE_INVALIDATED。出力デバイスの切断・無効化を示す。
        /// </summary>
        public const int DeviceInvalidated = -2004287484;
    }

    private readonly ILogger<AudioPlaybackService> logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AudioPlaybackService>.Instance;
    private WaveStream? reader;
    private PcmVolumeProvider? volumeProvider;
    private WasapiOut? output;
    private MMDevice? currentMMDevice;
    private MediaFoundationResampler? resampler;
    private PlaybackState state = PlaybackState.Stopped;
    private float desiredVolume = 1.0f;
    private bool isUserStopped;
    private bool decoderOutputIsTrusted;
    private Track? pendingTrack;
    private string? loadedTrackFilePath;

    public PlaybackState State
    {
        get => state;
        private set
        {
            if (state == value)
            {
                return;
            }

            var oldState = state;
            state = value;
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("再生状態が変化しました: {OldState} -> {NewState}", oldState, value);
            }
            StateChanged?.Invoke(this, value);
        }
    }

    public TimeSpan Position
    {
        get => reader?.CurrentTime ?? TimeSpan.Zero;
        set
        {
            if (reader is null)
            {
                return;
            }

            reader.CurrentTime = value < TimeSpan.Zero
                ? TimeSpan.Zero
                : value > reader.TotalTime ? reader.TotalTime : value;
        }
    }

    public TimeSpan Duration => reader?.TotalTime ?? TimeSpan.Zero;

    public float Volume
    {
        get => desiredVolume;
        set
        {
            desiredVolume = Math.Clamp(value, 0f, 1f);
            volumeProvider?.Volume = desiredVolume;
        }
    }

    public AudioDeviceInfo? OutputDevice { get; private set; }

    public AudioShareMode ShareMode { get; private set; } = AudioShareMode.Shared;

    public AudioShareMode ActualShareMode { get; private set; } = AudioShareMode.Shared;

    public WaveFormat? SourceFormat => reader?.WaveFormat;

    public int? SourceDisplayBitsPerSample
    {
        get
        {
            if (reader is null)
            {
                return null;
            }

            if (loadedTrackFilePath is not null &&
                string.Equals(Path.GetExtension(loadedTrackFilePath), ".wav", StringComparison.OrdinalIgnoreCase))
            {
                var effectiveBits = WavFormatNormalizer.TryGetEffectiveBitsPerSample(loadedTrackFilePath);
                if (effectiveBits.HasValue)
                {
                    return effectiveBits.Value;
                }
            }

            return reader.WaveFormat?.BitsPerSample;
        }
    }

    public WaveFormat? OutputFormat { get; private set; }

    public string? OutputFormatLabel { get; private set; }

    public bool IsResampling { get; private set; }

    /// <summary>
    /// 現在の再生が真にビットパーフェクトであるかどうか。
    /// 排他モードでリサンプリングが行われておらず（デコーダー出力がそのまま、
    /// またはロスレス再パッキングのみでデバイスへ渡せている）、かつ音量が1.0（<see cref="PcmVolumeProvider"/>が無加工でパススルーする状態）
    /// である場合にのみ true となる。
    /// <see cref="InitializeOutputCore"/> 内で <see cref="WasapiOut.Init(IWaveProvider)"/> が
    /// 成功した後に確定する値であるため、出力初期化前の判定候補よりも厳密。
    /// </summary>
    public bool IsBitPerfect =>
        ActualShareMode == AudioShareMode.Exclusive &&
        !IsResampling &&
        desiredVolume >= 1.0f &&
        decoderOutputIsTrusted;

    public event EventHandler<PlaybackState>? StateChanged;

    public event EventHandler? PlaybackCompleted;

    public event EventHandler<AudioErrorEventArgs>? ErrorOccurred;

    public void Load(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        // 実際のデコード（AudioFileReader生成）はコストが高いため、ここでは行わず
        // Play() 呼び出し時まで遅延させる。これによりプレイリスト読込や曲切替時に
        // UIスレッドがブロックされることを防ぐ。
        TeardownOutput();
        reader?.Dispose();
        reader = null;
        loadedTrackFilePath = null;

        pendingTrack = track;
        State = PlaybackState.Stopped;
    }

    /// <summary>
    /// 保留中のトラック（<see cref="pendingTrack"/>）を実際にデコードし、<see cref="reader"/> へ反映する。
    /// 正規化・AudioFileReader生成といった重い処理はここで初めて実行される。
    /// </summary>
    private bool TryLoadReader(Track track)
    {
        if (!File.Exists(track.FilePath))
        {
            reader = null;
            State = PlaybackState.Stopped;
            RaiseError($"'{track.FileName}' が見つかりません。ファイルが削除または移動された可能性があります。", null, AudioErrorKind.TrackUnavailable);
            return false;
        }

        try
        {
            // AudioFileReaderは内部で必ずIEEE Float 32bitへ変換してしまいビットパーフェクトを
            // 損なうため使用せず、元のビット深度・サンプルレートを保持するWaveStreamを直接生成する。
            // WAV Extensible 形式の正規化は NativeAudioFileReaderFactory 内で自動的に行われる。
            var newReader = NativeAudioFileReaderFactory.Create(track.FilePath);
            reader = newReader;
            loadedTrackFilePath = track.FilePath;
            track.Duration = newReader.TotalTime;
            return true;
        }
        catch (Exception ex)
        {
            reader = null;
            loadedTrackFilePath = null;
            State = PlaybackState.Stopped;
            RaiseError($"'{track.FileName}' を読み込めませんでした。対応していないファイル形式か、ファイルが破損している可能性があります。", ex, AudioErrorKind.TrackUnavailable);
            return false;
        }
    }

    public void Play()
    {
        if (reader is null)
        {
            if (pendingTrack is null || !TryLoadReader(pendingTrack))
            {
                return;
            }
        }

        try
        {
            if (output is null)
            {
                InitializeOutput();
            }

            isUserStopped = false;
            output!.Play();
            State = PlaybackState.Playing;
        }
        catch (Exception ex)
        {
            State = PlaybackState.Stopped;
            RaiseError("再生を開始できませんでした。", ex, ClassifyOutputException(ex));
        }
    }

    public void Pause()
    {
        if (output is null || State != PlaybackState.Playing)
        {
            return;
        }

        // 一時停止時は位置を保持したまま音声出力を完全に停止する
        // NAudioのPause()は一部環境で正しく動作しないため、Stop()を使用
        // ただし、PlaybackStoppedイベントで次の曲に進まないようにフラグを設定
        isUserStopped = true;
        output.Stop();
        State = PlaybackState.Paused;
    }

    public void Stop()
    {
        // ユーザーが明示的に停止ボタンを押したことを記録
        isUserStopped = true;

        // 停止時は出力リソースを完全にクリーンアップし、位置をリセット
        TeardownOutput();

        reader?.Position = 0;

        State = PlaybackState.Stopped;
    }

    public void SetOutputDevice(AudioDeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (OutputDevice?.Id == device.Id)
        {
            return;
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("出力デバイスが変更されました: {DeviceName} ({DeviceId})", device.Name, device.Id);
        }
        OutputDevice = device;
        ReinitializeOutputPreservingState();
    }

    public void SetShareMode(AudioShareMode shareMode)
    {
        if (ShareMode == shareMode)
        {
            return;
        }

        ShareMode = shareMode;
        ReinitializeOutputPreservingState();
    }

    public void Dispose()
    {
        TeardownOutput();
        reader?.Dispose();
        reader = null;
    }

    private void ReinitializeOutputPreservingState()
    {
        if (reader is null)
        {
            return;
        }

        var wasPlaying = State == PlaybackState.Playing;
        var resumePosition = reader.CurrentTime;

        TeardownOutput();

        try
        {
            InitializeOutput();
            reader.CurrentTime = resumePosition;

            if (wasPlaying)
            {
                output!.Play();
                State = PlaybackState.Playing;
            }
        }
        catch (Exception ex)
        {
            State = PlaybackState.Stopped;
            RaiseError("出力デバイスの切り替えに失敗しました。", ex, ClassifyOutputException(ex));
        }
    }

    /// <summary>
    /// 出力関連の例外がデバイス切断（AUDCLNT_E_DEVICE_INVALIDATED）に起因するかを判定する。
    /// </summary>
    private static AudioErrorKind ClassifyOutputException(Exception ex) =>
        ex is System.Runtime.InteropServices.COMException comEx && comEx.HResult == ExclusiveModeHResult.DeviceInvalidated
            ? AudioErrorKind.DeviceDisconnected
            : AudioErrorKind.Playback;

    private void InitializeOutput()
    {
        currentMMDevice = ResolveDevice(OutputDevice);

        var requestedShareMode = ShareMode == AudioShareMode.Exclusive
            ? AudioClientShareMode.Exclusive
            : AudioClientShareMode.Shared;

        try
        {
            InitializeOutputCore(requestedShareMode);
        }
        catch (Exception ex) when (requestedShareMode == AudioClientShareMode.Exclusive)
        {
            // 排他モードでの初期化に失敗した場合、共有モードへフォールバックして再試行する。
            RaiseError(BuildExclusiveModeFailureMessage(ex), ex);
            InitializeOutputCore(AudioClientShareMode.Shared);
        }
    }

    private void InitializeOutputCore(AudioClientShareMode shareModeNative)
    {
        // PcmVolumeProviderは音量が最大(1.0)の間は無加工でパススルーするため、
        // ここでラップしてもビットパーフェクト再生を妨げない。
        volumeProvider = new PcmVolumeProvider(reader!) { Volume = desiredVolume };
        IWaveProvider waveProvider = volumeProvider;
        IsResampling = false;
        OutputFormat = null;
        OutputFormatLabel = null;
        decoderOutputIsTrusted = false;

        bool canOutputWithoutSampleConversion = false;

        if (shareModeNative == AudioClientShareMode.Exclusive)
        {
            var fileFormat = reader!.WaveFormat;

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "[InitializeOutputCore] 排他モード: ファイル={SampleRate}Hz {BitsPerSample}bit {Channels}ch {Encoding}{SubFormat}",
                    fileFormat.SampleRate, fileFormat.BitsPerSample, fileFormat.Channels, fileFormat.Encoding,
                    fileFormat is WaveFormatExtensible fe ? $" SubFormat={fe.SubFormat}" : "");
            }

            // デバイスが対応しているフォーマット候補を列挙してデバッグ出力
            LogSupportedDeviceFormats(logger, currentMMDevice!, fileFormat.Channels);

            // ファイルのフォーマットを排他モードでデバイスが直接サポートするか確認。
            // WASAPI排他モードは、16bit/1〜2chの標準WAVEFORMATEX以外（24bit等）では
            // WAVEFORMATEXTENSIBLEでの指定を要求するデバイスが多いため、
            // ファイルそのままの形式とExtensible相当の形式の両方を試す。
            // この時点では音量・スペクトラムタップ・実際のWasapiOut.Init()成功可否はまだ未確認であり、
            // directOutputFormat は「デバイスへ無変換またはロスレス再パッキングで渡せる候補形式がある」ことのみを表す。
            // 最終的なビットパーフェクト状態は IsBitPerfect プロパティで判定する。
            var directOutputFormat = ResolveDirectOutputFormat(currentMMDevice!, fileFormat);
            canOutputWithoutSampleConversion = directOutputFormat is not null;

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "[InitializeOutputCore] 無変換出力可否: {CanOutputWithoutSampleConversion} ({SampleRate}Hz {BitsPerSample}bit {Channels}ch {Encoding})",
                    canOutputWithoutSampleConversion, fileFormat.SampleRate, fileFormat.BitsPerSample, fileFormat.Channels, fileFormat.Encoding);
            }

            if (canOutputWithoutSampleConversion)
            {
                // 3段階のフォーマット互換性判定（ExactFormatMatch/LosslessRepacking/RequiresConversion）に基づき、
                // コンテナサイズのみが異なる24-in-32等のロスレス再パッキングが必要かどうかを判定する。
                var compatibility = ClassifyFormatCompatibility(fileFormat, directOutputFormat!);
                bool requiresPacking = compatibility == FormatCompatibility.LosslessRepacking;

                if (requiresPacking)
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogDebug(
                            "[InitializeOutputCore] 無変換出力（24-in-32パッキング）: {SampleRate}Hz {BitsPerSample}bit -> {ContainerBits}bitコンテナ {Channels}ch {Encoding}",
                            fileFormat.SampleRate, fileFormat.BitsPerSample, directOutputFormat!.BitsPerSample, fileFormat.Channels, fileFormat.Encoding);
                    }
                    waveProvider = new Pack24In32WaveProvider(waveProvider, directOutputFormat!);
                }
                else if (logger.IsEnabled(LogLevel.Debug))
                {
                    // ファイルフォーマット（またはそのExtensible相当）をそのまま出力（リサンプリング不要）
                    logger.LogDebug(
                        "[InitializeOutputCore] 無変換出力: {SampleRate}Hz {BitsPerSample}bit {Channels}ch {Encoding}",
                        fileFormat.SampleRate, fileFormat.BitsPerSample, fileFormat.Channels, fileFormat.Encoding);
                }
                OutputFormat = directOutputFormat;
                OutputFormatLabel = DescribeFormat(directOutputFormat!);
            }
            else
            {
                // 無変換出力不可: デバイスが対応する最も近しいフォーマットを選択してリサンプリング
                logger.LogDebug("[InitializeOutputCore] 無変換出力不可: デバイス対応フォーマットを探索");

                resampler?.Dispose();
                var bestDeviceFormat = FindBestSupportedFormat(currentMMDevice!, fileFormat);

                if (bestDeviceFormat is not null)
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogDebug(
                            "[InitializeOutputCore] リサンプリング: {SourceSampleRate}Hz {SourceBitsPerSample}bit {SourceEncoding} -> {TargetSampleRate}Hz {TargetBitsPerSample}bit {TargetEncoding}",
                            fileFormat.SampleRate, fileFormat.BitsPerSample, fileFormat.Encoding,
                            bestDeviceFormat.SampleRate, bestDeviceFormat.BitsPerSample, bestDeviceFormat.Encoding);
                    }

                    resampler = new MediaFoundationResampler(volumeProvider, bestDeviceFormat);
                    waveProvider = resampler;
                    IsResampling = true;
                    OutputFormat = bestDeviceFormat;
                    OutputFormatLabel = DescribeFormat(bestDeviceFormat);
                }
                else
                {
                    // 対応フォーマットが見つからない場合はMixFormat（デバイスのネイティブ共有フォーマット）へリサンプリング
                    var mixFormat = currentMMDevice!.AudioClient.MixFormat;
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogDebug(
                            "[InitializeOutputCore] 対応フォーマットなし: MixFormatへリサンプリング {SourceSampleRate}Hz -> {TargetSampleRate}Hz",
                            fileFormat.SampleRate, mixFormat.SampleRate);
                    }

                    resampler = new MediaFoundationResampler(volumeProvider, mixFormat);
                    waveProvider = resampler;
                    IsResampling = true;
                    OutputFormat = mixFormat;
                    OutputFormatLabel = DescribeFormat(mixFormat);
                }
            }
        }
        else
        {
            // 共有モードの場合、Windowsが自動的にミックスするため出力フォーマットは不明
            OutputFormat = currentMMDevice?.AudioClient.MixFormat;
            OutputFormatLabel = OutputFormat is not null ? DescribeFormat(OutputFormat) : null;
        }

        if (spectrumAnalyzer is not null)
        {
            // リサンプル後・WasapiOut初期化前の位置で透過的にキャプチャする。
            waveProvider = new SpectrumTapProvider(waveProvider, spectrumAnalyzer);
        }

        var newOutput = new WasapiOut(currentMMDevice, shareModeNative, true, LatencyMilliseconds);
        newOutput.PlaybackStopped += OnPlaybackStopped;
        newOutput.Init(waveProvider);
        output = newOutput;
        ActualShareMode = shareModeNative == AudioClientShareMode.Exclusive
            ? AudioShareMode.Exclusive
            : AudioShareMode.Shared;

        // WasapiOut.Init() が実際に成功した後で、デコーダー出力（またはロスレス再パッキング）が
        // そのままデバイスへ渡せたことを確定する。音量（desiredVolume）の考慮は呼び出し元の
        // IsBitPerfect プロパティ側で行う。
        decoderOutputIsTrusted = ActualShareMode == AudioShareMode.Exclusive && canOutputWithoutSampleConversion;
    }

    /// <summary>
    /// 2つのWaveFormatが互換性があるか（ビットパーフェクト再生可能か）を判定する。
    /// ExtensibleフォーマットのSubFormatも考慮して比較する。
    /// </summary>
    private enum FormatCompatibility
    {
        /// <summary>SampleRate/Channels/BitsPerSample/実エンコーディング/ValidBitsPerSampleがすべて一致する。</summary>
        ExactFormatMatch,

        /// <summary>PCM値としては保存されるが、コンテナ形式のみが異なる（例: 24-in-24 -> 24-in-32）。</summary>
        LosslessRepacking,

        /// <summary>サンプルレート変換・ビット深度変換・Float/PCM変換等、リサンプリングを要する。</summary>
        RequiresConversion,
    }

    /// <summary>
    /// 指定フォーマットがIEEE Floatエンコーディングかどうかを判定する。
    /// 非Extensibleは<see cref="WaveFormat.Encoding"/>、Extensibleは<see cref="WaveFormatExtensible.SubFormat"/>で判定する。
    /// </summary>
    private static bool IsFloatEncoding(WaveFormat format) =>
        format.Encoding == WaveFormatEncoding.IeeeFloat ||
        (format is WaveFormatExtensible extensible && extensible.SubFormat == IeeeFloatSubFormatGuid);

    /// <summary>
    /// 指定フォーマットの実効ビット数（ValidBitsPerSample）を取得する。
    /// Extensibleでない場合はコンテナビット数（BitsPerSample）をそのまま返す。
    /// </summary>
    private static int GetValidBits(WaveFormat format) =>
        format is WaveFormatExtensible extensible
            ? (short)WaveFormatExtensibleValidBitsField.GetValue(extensible)!
            : format.BitsPerSample;

    /// <summary>
    /// 2つのWaveFormatの互換性を<see cref="FormatCompatibility"/>の3段階で判定する。
    /// SampleRate/Channels/実エンコーディング（Extensibleなら<see cref="WaveFormatExtensible.SubFormat"/>）が
    /// 一致しない場合は無条件で<see cref="FormatCompatibility.RequiresConversion"/>とする。
    /// コンテナビット数（BitsPerSample）と実効ビット数（ValidBitsPerSample）が完全に一致すれば
    /// <see cref="FormatCompatibility.ExactFormatMatch"/>、実効ビット数（24bit）のみ一致し
    /// コンテナサイズだけが異なる場合（24-in-24 -> 24-in-32）は
    /// <see cref="FormatCompatibility.LosslessRepacking"/>（<see cref="Pack24In32WaveProvider"/>で対応可能）とする。
    /// </summary>
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
        // 「24-in-32」へのロスレス再パッキングのみ対応（Pack24In32WaveProviderの対象範囲と一致させる）。
        if (!sourceIsFloat && sourceValidBits == 24 && targetValidBits == 24 && target.BitsPerSample == 32)
        {
            return FormatCompatibility.LosslessRepacking;
        }

        // 一部のデバイス(例: FiiO BTR17)は validBits=24 の 24-in-32 に対応せず、
        // validBits=32 の 32bit PCM のみに対応している。この場合も 24bit → 32bit への
        // ビット拡張はロスレス(下位8bitをゼロ埋め)で可能なため、LosslessRepacking として扱う。
        if (!sourceIsFloat && sourceValidBits == 24 && targetValidBits == 32 && target.BitsPerSample == 32)
        {
            return FormatCompatibility.LosslessRepacking;
        }

        return FormatCompatibility.RequiresConversion;
    }

    /// <summary>
    /// 排他モードでの初期化失敗時に、原因別のユーザー向けメッセージを組み立てる。
    /// </summary>
    private static string BuildExclusiveModeFailureMessage(Exception ex)
    {
        if (ex is System.Runtime.InteropServices.COMException comEx)
        {
            switch (comEx.HResult)
            {
                case ExclusiveModeHResult.DeviceInUse:
                    return "他のアプリケーションが出力デバイスを排他使用中のため、排他モードで再生できませんでした。共有モードで再生します。";
                case ExclusiveModeHResult.ExclusiveModeNotAllowed:
                    return "出力デバイスが排他モードでの再生を許可していないため、共有モードで再生します。";
                case ExclusiveModeHResult.UnsupportedFormat:
                    return "出力デバイスがこのファイルの形式での排他モード再生に対応していないため、共有モードで再生します。";
            }
        }

        return "排他モードでの再生開始に失敗したため、共有モードで再生します。";
    }

    private void TeardownOutput()
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("TeardownOutput: 出力解放を開始します (ActualShareMode={ActualShareMode})", ActualShareMode);
        }

        // 排他モード判定用に現在のActualShareModeをローカル変数に退避
        var wasExclusiveMode = ActualShareMode == AudioShareMode.Exclusive;

        if (output is not null)
        {
            output.PlaybackStopped -= OnPlaybackStopped;

            try
            {
                output.Stop();
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("TeardownOutput: WasapiOutを停止しました。");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "TeardownOutput: WasapiOut停止時に例外が発生しましたが、破棄処理を続行します。");
            }

            output.Dispose();
            output = null;

            // 排他モードだった場合のみ、オーディオエンジン側の解放猶予として短時間待機
            if (wasExclusiveMode)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("TeardownOutput: 排他モード解放の猶予待機を開始します ({DelayMs}ms)", ExclusiveModeReleaseDelayMilliseconds);
                }
                Thread.Sleep(ExclusiveModeReleaseDelayMilliseconds);
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("TeardownOutput: 排他モード解放の猶予待機を終了しました。");
                }
            }
        }
        else
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("TeardownOutput: 出力が既に解放済みのためスキップします。");
            }
        }

        resampler?.Dispose();
        resampler = null;

        volumeProvider = null;

        currentMMDevice?.Dispose();
        currentMMDevice = null;

        spectrumAnalyzer?.Reset();

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("TeardownOutput: 出力解放が完了しました。");
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // このハンドラーは現在アクティブなoutputにのみ購読しているため、
        // 呼び出された時点でNAudio側の自然終了（トラック終端 or デバイスエラー）とみなせる。

        // 状態を記録してから変更（Playing状態だったかを判定するため）
        var wasPlaying = State == PlaybackState.Playing;

        State = PlaybackState.Stopped;

        if (e.Exception is not null)
        {
            RaiseError("再生デバイスとの通信中にエラーが発生しました。", e.Exception, ClassifyOutputException(e.Exception));
            return;
        }

        // 次の曲に進むのは以下の条件を両方満たす場合のみ：
        // 1. 再生中だった（Playing状態）
        // 2. ユーザーが停止/一時停止ボタンを押していない
        if (wasPlaying && !isUserStopped)
        {
            PlaybackCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RaiseError(string message, Exception? exception = null, AudioErrorKind kind = AudioErrorKind.Playback)
    {
        logger.LogError(exception, "再生エラーが発生しました ({Kind}): {Message}", kind, message);
        ErrorOccurred?.Invoke(this, new AudioErrorEventArgs(message, exception, kind));
    }

    private static MMDevice ResolveDevice(AudioDeviceInfo? deviceInfo)
    {
        using var enumerator = new MMDeviceEnumerator();
        return deviceInfo is not null
            ? enumerator.GetDevice(deviceInfo.Id)
            : enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    /// <summary>
    /// 指定デバイスが排他モードで対応しているサンプルレート・チャンネル数・エンコーディングの組み合わせを
    /// デバッグ出力に列挙する。PCM / IEEE Float / WaveFormatExtensible(PCM・Float) を網羅的に試す。
    /// </summary>
    private static void LogSupportedDeviceFormats(ILogger logger, MMDevice device, int fileChannels)
    {
        int[] sampleRates = [384000, 352800, 192000, 176400, 96000, 88200, 48000, 44100];
        int[] bitsPerSampleList = [32, 24, 16];
        // ファイルのチャンネル数を優先しつつ、一般的なチャンネル数を網羅する
        int[] channelCandidates = [.. new[] { fileChannels, 1, 2, 6, 8 }.Distinct()];

        logger.LogDebug("[InitializeOutputCore] デバイス対応フォーマット一覧:");

        foreach (var channels in channelCandidates)
        {
            foreach (var sampleRate in sampleRates)
            {
                foreach (var bitsPerSample in bitsPerSampleList)
                {
                    // PCM (WaveFormat)
                    TryLogFormat(logger, device, new WaveFormat(sampleRate, bitsPerSample, channels),
                        "PCM", channels, sampleRate, bitsPerSample);

                    // WaveFormatExtensible:
                    //   16/24bit → SubFormat=PCM、32bit → SubFormat=IEEE Float（NAudio の仕様による）
                    TryLogFormat(logger, device, new WaveFormatExtensible(sampleRate, bitsPerSample, channels),
                        bitsPerSample == 32 ? "Extensible/Float" : "Extensible/PCM",
                        channels, sampleRate, bitsPerSample);

                    if (bitsPerSample == 32)
                    {
                        // IEEE Float (WaveFormat)
                        TryLogFormat(logger, device, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels),
                            "IeeeFloat", channels, sampleRate, bitsPerSample);

                        // 32bit整数PCM（IEEE Floatと区別するため CreateExtensibleFormat で明示的にPCM指定）
                        TryLogFormat(logger, device, CreateExtensibleFormat(sampleRate, 32, channels, isFloatSource: false),
                            "Extensible/PCM(32bit int)", channels, sampleRate, bitsPerSample);
                    }

                    if (bitsPerSample == 24)
                    {
                        // 24-in-32（32bitコンテナに24bit有効データを左詰め格納するPCM形式）。
                        // 一部のUSB DAC（例: Fiio DM15 R2R）は排他モードでこの形式のみを受理する。
                        TryLogFormat(logger, device, CreatePacked24In32Format(sampleRate, channels),
                            "Extensible/PCM(24-in-32)", channels, sampleRate, bitsPerSample);
                    }
                }
            }
        }
    }

    /// <summary>
    /// デバイスが指定フォーマットを排他モードでサポートしていればデバッグ出力する。
    /// </summary>
    private static void TryLogFormat(ILogger logger, MMDevice device, WaveFormat format, string label,
        int channels, int sampleRate, int bitsPerSample)
    {
        try
        {
            if (device.AudioClient.IsFormatSupported(AudioClientShareMode.Exclusive, format))
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    if (format is WaveFormatExtensible ext)
                    {
                        var validBits = (short?)WaveFormatExtensibleValidBitsField.GetValue(ext);
                        logger.LogDebug("  対応: {SampleRate}Hz {BitsPerSample}bit {Channels}ch ({Label}, validBits={ValidBits})",
                            sampleRate, bitsPerSample, channels, label, validBits);
                    }
                    else
                    {
                        logger.LogDebug("  対応: {SampleRate}Hz {BitsPerSample}bit {Channels}ch ({Label})", sampleRate, bitsPerSample, channels, label);
                    }
                }
            }
        }
        catch
        {
            // 確認失敗は無視する
        }
    }

    /// <summary>
    /// デバイスが排他モードで対応しているフォーマットの中から、ファイルのフォーマットに最も近しいものを返す。
    /// 優先順位: (1) ファイルと同じサンプルレート → (2) 差が小さい順（同差は高い方優先）。
    /// エンコーディングはファイルに合わせ（Float → Float、PCM → PCM）、
    /// ビット深度はファイルのビット深度を最優先とする。
    /// WaveFormatExtensible（SubFormat付き）も候補に含め、より多くのデバイスに対応する。
    /// 対応フォーマットが1つも見つからない場合は null を返す。
    /// </summary>
    private WaveFormat? FindBestSupportedFormat(MMDevice device, WaveFormat fileFormat)
    {
        // サンプルレート候補: ファイルのサンプルレートからの差が小さい順（同差なら高い方優先）
        int[] standardRates = [384000, 352800, 192000, 176400, 96000, 88200, 48000, 44100];
        int[] sampleRates = [.. new[] { fileFormat.SampleRate }.Concat(standardRates)
            .Distinct()
            .OrderBy(r => Math.Abs(r - fileFormat.SampleRate))
            .ThenByDescending(r => r)];

        // ファイルがFloatかどうかを SubFormat まで含めて判定
        bool isFloat = IsFloatEncoding(fileFormat);

        // ビット深度候補: ファイルのビット深度を優先し、続いて一般的な深度を試す
        int[] bitsCandidates = isFloat
            ? [32]
            : [.. new[] { fileFormat.BitsPerSample, 32, 24, 16 }.Distinct()];

        foreach (var sampleRate in sampleRates)
        {
            foreach (var bitsPerSample in bitsCandidates)
            {
                // WaveFormatExtensible を優先し、通常フォーマットも続けて試す
                // CreateExtensibleFormat は isFloatSource を明示指定するため、
                // 32bit整数PCM（bitsPerSample==32かつisFloat==false）でも
                // 誤って Float SubFormat になることはない。
                // 24bitの場合は、一部のUSB DAC（例: Fiio DM15 R2R）が排他モードでのみ受理する
                // 「24-in-32」形式も、16bitへ落ちる前に候補として試す。
                IEnumerable<WaveFormat> candidates = isFloat
                    ? [
                        CreateExtensibleFormat(sampleRate, 32, fileFormat.Channels, isFloatSource: true),
                        WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, fileFormat.Channels),
                      ]
                    : bitsPerSample == 24
                    ? [
                        CreateExtensibleFormat(sampleRate, bitsPerSample, fileFormat.Channels, isFloatSource: false),
                        new WaveFormat(sampleRate, bitsPerSample, fileFormat.Channels),
                        CreatePacked24In32Format(sampleRate, fileFormat.Channels),
                      ]
                    : [
                        CreateExtensibleFormat(sampleRate, bitsPerSample, fileFormat.Channels, isFloatSource: false),
                        new WaveFormat(sampleRate, bitsPerSample, fileFormat.Channels),
                      ];

                foreach (var candidate in candidates)
                {
                    if (IsFormatSupportedSafe(device, candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 指定フォーマットが排他モードでビットパーフェクト再生可能かを判定し、
    /// 実際にデバイスへ渡すべき<see cref="WaveFormat"/>を返す（対応不可の場合は null）。
    /// </summary>
    /// <remarks>
    /// WASAPI排他モードでは、16bit/1〜2chの標準的な組み合わせ以外（24bit等）は
    /// WAVEFORMATEXTENSIBLE構造体での指定が必須というAPI仕様がある。
    /// <see cref="MediaFoundationReader"/> 等が返す<see cref="WaveFormat"/>は非Extensibleの
    /// 場合があるため、それがそのまま拒否された場合はExtensible相当の形式でも試す。
    /// サンプルデータ自体は変わらないため、どちらが採用されてもビットパーフェクト性は保たれる。
    /// </remarks>
    private WaveFormat? ResolveDirectOutputFormat(MMDevice device, WaveFormat fileFormat)
    {
        if (IsFormatSupportedSafe(device, fileFormat))
        {
            return fileFormat;
        }

        if (fileFormat is not WaveFormatExtensible)
        {
            var extensible = CreateExtensibleFormat(fileFormat.SampleRate, fileFormat.BitsPerSample, fileFormat.Channels,
                isFloatSource: IsFloatEncoding(fileFormat));
            if (IsFormatSupportedSafe(device, extensible))
            {
                return extensible;
            }
        }

        // 一部のUSB DAC（例: Fiio DM15 R2R）は排他モードで「24-in-24」ではなく
        // 「24-in-32」（32bitコンテナに24bit有効データを左詰め格納）でのみ対応する。
        // 24bit PCMファイルの場合はこの形式もビットパーフェクト候補として試す。
        bool isPcm24 = fileFormat.BitsPerSample == 24 &&
            (fileFormat.Encoding == WaveFormatEncoding.Pcm ||
             (fileFormat is WaveFormatExtensible pcm24Ext && pcm24Ext.SubFormat == PcmSubFormatGuid));

        if (isPcm24)
        {
            // まず validBits=24 の 24-in-32 フォーマットを試す
            var packed24In32 = CreatePacked24In32Format(fileFormat.SampleRate, fileFormat.Channels);
            if (logger.IsEnabled(LogLevel.Debug))
            {
                var validBits = (short?)WaveFormatExtensibleValidBitsField.GetValue(packed24In32);
                var subFormat = (Guid?)WaveFormatExtensibleSubFormatField.GetValue(packed24In32);
                logger.LogDebug("  24-in-32フォーマットを試行: {Format} (validBits={ValidBits}, subFormat={SubFormat})",
                    packed24In32, validBits, subFormat);
            }
            if (IsFormatSupportedSafe(device, packed24In32))
            {
                return packed24In32;
            }

            // 24-in-32 (validBits=24) が対応していない場合、32bit PCM (validBits=32) も試す。
            // 一部のデバイス(例: FiiO BTR17)は validBits=32 の 32bit PCM のみに対応している。
            // この場合、24bit データを 32bit に変換することでビットパーフェクト再生が可能。
            var pcm32 = CreateExtensibleFormat(fileFormat.SampleRate, 32, fileFormat.Channels, isFloatSource: false);
            if (logger.IsEnabled(LogLevel.Debug))
            {
                var validBits = (short?)WaveFormatExtensibleValidBitsField.GetValue(pcm32);
                var subFormat = (Guid?)WaveFormatExtensibleSubFormatField.GetValue(pcm32);
                logger.LogDebug("  32bit PCMフォーマットを試行: {Format} (validBits={ValidBits}, subFormat={SubFormat})",
                    pcm32, validBits, subFormat);
            }
            if (IsFormatSupportedSafe(device, pcm32))
            {
                return pcm32;
            }
        }

        return null;
    }

    /// <summary>
    /// PCM フォーマットの SubFormat GUID: {00000001-0000-0010-8000-00aa00389b71}
    /// </summary>
    private static readonly Guid PcmSubFormatGuid = new("00000001-0000-0010-8000-00aa00389b71");

    /// <summary>
    /// IEEE Float フォーマットの SubFormat GUID: {00000003-0000-0010-8000-00aa00389b71}
    /// </summary>
    private static readonly Guid IeeeFloatSubFormatGuid = new("00000003-0000-0010-8000-00aa00389b71");

    /// <summary>
    /// <see cref="WaveFormatExtensible"></see> の非公privateフィールド `wValidBitsPerSample`。
    /// NAudioの公開コンストラクタ(rate, bits, channels)はコンテナビット数と有効ビット数を
    /// 必ず同一値にするため、、24-in-32」のように両者が異なるパッキング形式を作成するには
    /// このフィールドをリフレクションで直接上書きする必要がある。
    /// </summary>
    private static readonly FieldInfo WaveFormatExtensibleValidBitsField =
        typeof(WaveFormatExtensible).GetField("wValidBitsPerSample", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new MissingFieldException(nameof(WaveFormatExtensible), "wValidBitsPerSample");

    /// <summary>
    /// <see cref="WaveFormatExtensible"></see> の非公privateフィールド `subFormat`。
    /// bits=32で構築するとNAudioは自動的にIEEE FloatのSubFormatを設定するため、
    /// 24-in-32(PCM)の場合は PCM の SubFormat へ上書きする必要がある。
    /// </summary>
    private static readonly FieldInfo WaveFormatExtensibleSubFormatField =
        typeof(WaveFormatExtensible).GetField("subFormat", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new MissingFieldException(nameof(WaveFormatExtensible), "subFormat");

    /// <summary>
    /// 24bit有効データを32bitコンテナに左詰め格納する「24-in-32」PCMフォーマットを生成する。
    /// 多くのUSB DAC（例: Fiio DM15 R2R）は排他モードで、24-in-24」ではなく
    /// この、24-in-32」形式のみを受理するため、ビットパーフェクト候補の一つとして試す。
    /// </summary>
    private static WaveFormatExtensible CreatePacked24In32Format(int sampleRate, int channels) =>
        // コンテナ32bitで生成することで blockAlign / averageBytesPerSecond を
        // 32bitコンテナ基準で正しく計算させつつ、PCM の SubFormat・有効ビット数24を明示する。
        CreateExtensibleFormat(sampleRate, 32, channels, isFloatSource: false, validBits: 24);

    /// <summary>
    /// <see cref="WaveFormatExtensible"/> を生成するための共通ヘルパー。
    /// NAudio の公開コンストラクタ(rate, bits, channels)は bits==32 の場合に自動的に
    /// IEEE Float の SubFormat を設定してしまうため、32bit整数PCM（真の32bit int PCMや
    /// 24-in-32パッキング）を扱う際に誤って Float 扱いとなる問題を避けるべく、
    /// 呼び出し側が明示的に isFloatSource / validBits を指定してリフレクションで
    /// subFormat・wValidBitsPerSample を常に上書きする。
    /// </summary>
    /// <param name="sampleRate">サンプルレート。</param>
    /// <param name="bitsPerSample">コンテナのビット数（BlockAlign計算に使われる）。</param>
    /// <param name="channels">チャンネル数。</param>
    /// <param name="isFloatSource">IEEE Float として扱うか（false ならPCM）。</param>
    /// <param name="validBits">実効ビット数。未指定の場合は<paramref name="bitsPerSample"/>と同じ値を使う。</param>
    private static WaveFormatExtensible CreateExtensibleFormat(int sampleRate, int bitsPerSample, int channels,
        bool isFloatSource, int? validBits = null)
    {
        var format = new WaveFormatExtensible(sampleRate, bitsPerSample, channels);

        WaveFormatExtensibleSubFormatField.SetValue(format, isFloatSource ? IeeeFloatSubFormatGuid : PcmSubFormatGuid);
        WaveFormatExtensibleValidBitsField.SetValue(format, (short)(validBits ?? bitsPerSample));

        return format;
    }

    /// <summary>
    /// 出力フォーマットをUI表示向けの詳細ラベル（例: "PCM", "Extensible/PCM", "Extensible/PCM(24-in-32)"）に変換する。
    /// <see cref="WaveFormatExtensible"/> の場合、コンテナビット数と実際の有効ビット数（wValidBitsPerSample）が
    /// 異なる場合（例: 24bit有効データを32bitコンテナへ格納する「24-in-32」パッキング）は、その旨を付記する。
    /// </summary>
    private static string DescribeFormat(WaveFormat format)
    {
        if (format is WaveFormatExtensible extensible)
        {
            var isFloat = extensible.SubFormat == IeeeFloatSubFormatGuid;
            var label = isFloat ? "Extensible/Float" : "Extensible/PCM";

            var validBits = (short)WaveFormatExtensibleValidBitsField.GetValue(extensible)!;
            if (validBits != format.BitsPerSample)
            {
                label += $"({validBits}-in-{format.BitsPerSample})";
            }

            return label;
        }

        return format.Encoding == WaveFormatEncoding.IeeeFloat ? "IeeeFloat" : "PCM";
    }

    /// <summary>
    /// デバイスが指定フォーマットを排他モードでサポートしているかを安全に確認する。
    /// IsFormatSupported が例外を投げた場合は false を返す。
    /// </summary>
    private bool IsFormatSupportedSafe(MMDevice device, WaveFormat format)
    {
        try
        {
            var result = device.AudioClient.IsFormatSupported(AudioClientShareMode.Exclusive, format);
            if (logger.IsEnabled(LogLevel.Trace))
            {
                if (format is WaveFormatExtensible ext)
                {
                    var validBits = (short?)WaveFormatExtensibleValidBitsField.GetValue(ext);
                    var subFormat = (Guid?)WaveFormatExtensibleSubFormatField.GetValue(ext);
                    logger.LogTrace("IsFormatSupported: {Result} for {Format} (validBits={ValidBits}, subFormat={SubFormat})",
                        result, format, validBits, subFormat);
                }
                else
                {
                    logger.LogTrace("IsFormatSupported: {Result} for {Format}", result, format);
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace(ex, "IsFormatSupported failed for {Format}", format);
            }
            return false;
        }
    }
}
