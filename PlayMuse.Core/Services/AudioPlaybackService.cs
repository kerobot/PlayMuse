using NAudio.CoreAudioApi;
using NAudio.Wave;
using PlayMuse.Core.Models;
using PlaybackState = PlayMuse.Core.Models.PlaybackState;

namespace PlayMuse.Core.Services;

/// <summary>
/// NAudioの<see cref="AudioFileReader"/>（デコード）と<see cref="WasapiOut"/>（WASAPI出力）を
/// 組み合わせて実装する再生サービス。
/// </summary>
public sealed class AudioPlaybackService : IAudioPlaybackService
{
    private const int LatencyMilliseconds = 200;

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
    }

    private AudioFileReader? reader;
    private WasapiOut? output;
    private MMDevice? currentMMDevice;
    private MediaFoundationResampler? resampler;
    private PlaybackState state = PlaybackState.Stopped;
    private float desiredVolume = 1.0f;

    public PlaybackState State
    {
        get => state;
        private set
        {
            if (state == value)
            {
                return;
            }

            state = value;
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
            if (reader is not null)
            {
                reader.Volume = desiredVolume;
            }
        }
    }

    public AudioDeviceInfo? OutputDevice { get; private set; }

    public AudioShareMode ShareMode { get; private set; } = AudioShareMode.Shared;

    public AudioShareMode ActualShareMode { get; private set; } = AudioShareMode.Shared;

    public WaveFormat? SourceFormat => reader?.WaveFormat;

    public WaveFormat? OutputFormat { get; private set; }

    public bool IsResampling { get; private set; }

    public event EventHandler<PlaybackState>? StateChanged;

    public event EventHandler? PlaybackCompleted;

    public event EventHandler<AudioErrorEventArgs>? ErrorOccurred;

    public void Load(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        TeardownOutput();
        reader?.Dispose();
        reader = null;

        try
        {
            var newReader = new AudioFileReader(track.FilePath)
            {
                Volume = desiredVolume,
            };

            reader = newReader;
            track.Duration = newReader.TotalTime;
            State = PlaybackState.Stopped;
        }
        catch (Exception ex)
        {
            reader = null;
            State = PlaybackState.Stopped;
            RaiseError($"'{track.FileName}' を読み込めませんでした。対応していないファイル形式か、ファイルが破損している可能性があります。", ex);
        }
    }

    public void Play()
    {
        if (reader is null)
        {
            return;
        }

        try
        {
            if (output is null)
            {
                InitializeOutput();
            }

            output!.Play();
            State = PlaybackState.Playing;
        }
        catch (Exception ex)
        {
            State = PlaybackState.Stopped;
            RaiseError("再生を開始できませんでした。", ex);
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
        output.Stop();
        State = PlaybackState.Paused;
    }

    public void Stop()
    {
        // 停止時は出力リソースを完全にクリーンアップし、位置をリセット
        TeardownOutput();

        if (reader is not null)
        {
            reader.Position = 0;
        }

        State = PlaybackState.Stopped;
    }

    public void SetOutputDevice(AudioDeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (OutputDevice?.Id == device.Id)
        {
            return;
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
            RaiseError("出力デバイスの切り替えに失敗しました。", ex);
        }
    }

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
        // 排他モードの場合、デバイスのネイティブフォーマットにリサンプリングが必要か判定
        IWaveProvider waveProvider = reader!;
        IsResampling = false;
        OutputFormat = null;

        if (shareModeNative == AudioClientShareMode.Exclusive)
        {
            var deviceNativeFormat = currentMMDevice!.AudioClient.MixFormat;
            var fileFormat = reader.WaveFormat;

            System.Diagnostics.Debug.WriteLine($"[InitializeOutputCore] 排他モード: ファイル={fileFormat.SampleRate}Hz, デバイス={deviceNativeFormat.SampleRate}Hz");

            // サンプルレート、チャンネル数、またはエンコーディングが異なる場合、リサンプリングを実行
            if (fileFormat.SampleRate != deviceNativeFormat.SampleRate ||
                fileFormat.Channels != deviceNativeFormat.Channels ||
                fileFormat.Encoding != deviceNativeFormat.Encoding)
            {
                System.Diagnostics.Debug.WriteLine($"[InitializeOutputCore] リサンプリング実行: {fileFormat.SampleRate}Hz -> {deviceNativeFormat.SampleRate}Hz");

                // 既存のリサンプラーを破棄
                resampler?.Dispose();

                // MediaFoundationResamplerでデバイスのネイティブフォーマットに変換
                resampler = new MediaFoundationResampler(reader, deviceNativeFormat);
                waveProvider = resampler;
                IsResampling = true;
                OutputFormat = deviceNativeFormat;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[InitializeOutputCore] リサンプリング不要");
                OutputFormat = fileFormat;
            }
        }
        else
        {
            // 共有モードの場合、Windowsが自動的にミックスするため出力フォーマットは不明
            OutputFormat = currentMMDevice?.AudioClient.MixFormat;
        }

        var newOutput = new WasapiOut(currentMMDevice, shareModeNative, true, LatencyMilliseconds);
        newOutput.PlaybackStopped += OnPlaybackStopped;
        newOutput.Init(waveProvider);
        output = newOutput;
        ActualShareMode = shareModeNative == AudioClientShareMode.Exclusive
            ? AudioShareMode.Exclusive
            : AudioShareMode.Shared;
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
        if (output is not null)
        {
            output.PlaybackStopped -= OnPlaybackStopped;

            try
            {
                output.Stop();
            }
            catch
            {
                // 破棄目的の停止のため、ここでの例外は無視する。
            }

            output.Dispose();
            output = null;
        }

        resampler?.Dispose();
        resampler = null;

        currentMMDevice?.Dispose();
        currentMMDevice = null;
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // このハンドラーは現在アクティブなoutputにのみ購読しているため、
        // 呼び出された時点でNAudio側の自然終了（トラック終端 or デバイスエラー）とみなせる。
        State = PlaybackState.Stopped;

        if (e.Exception is not null)
        {
            RaiseError("再生デバイスとの通信中にエラーが発生しました。", e.Exception);
            return;
        }

        PlaybackCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseError(string message, Exception? exception = null)
    {
        ErrorOccurred?.Invoke(this, new AudioErrorEventArgs(message, exception));
    }

    private static MMDevice ResolveDevice(AudioDeviceInfo? deviceInfo)
    {
        using var enumerator = new MMDeviceEnumerator();
        return deviceInfo is not null
            ? enumerator.GetDevice(deviceInfo.Id)
            : enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }
}
