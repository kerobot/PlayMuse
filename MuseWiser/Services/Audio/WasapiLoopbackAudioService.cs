using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using System.Threading;

namespace MuseWiser.Services.Audio;

/// <summary>
/// 既定の再生デバイス（共有モード）の WASAPI ループバックキャプチャにより、
/// 左右チャンネルの音量レベルを取得するサービス。
/// NAudio 3.x の <see cref="WasapiRecorderBuilder"/> / <see cref="WasapiRecorder"/> を利用する。
/// 既定の再生デバイスが切り替わった場合は、キャプチャ対象を自動的に追従させる。
/// </summary>
public sealed class WasapiLoopbackAudioService : IAudioCaptureService
{
    /// <summary>
    /// FFT サイズの基準値と、その基準となるサンプルレート。
    /// サンプルレートが高いほど同じ時間窓を保つために FFT サイズを大きくし、
    /// 低域の周波数分解能（1 ビンあたりの Hz 幅）を確保する。
    /// </summary>
    private const int BaseFftSize = 2048;

    /// <summary>
    /// <see cref="BaseFftSize"/> の基準となるサンプルレート（Hz）。
    /// </summary>
    private const int BaseSampleRate = 44100;

    /// <summary>
    /// FFT サイズの下限・上限（2 のべき乗）。
    /// </summary>
    private const int MinFftLength = 1024;

    /// <summary>
    /// FFT サイズの上限（2 のべき乗）。
    /// </summary>
    private const int MaxFftLength = 16384;

    /// <summary>
    /// 表示するスペクトラムバンド数（PlayMuse 風 16 バンド LED を想定）。
    /// </summary>
    private const int SpectrumBandCount = 16;

    /// <summary>
    /// スペクトラム表示の下限（この dB 以下は無音扱い）。
    /// </summary>
    private const double SpectrumMinDecibels = -70.0;

    /// <summary>
    /// <see cref="WasapiRecorder.RecordingStopped"/>（予期しない停止）を検知した際、
    /// またはキャプチャ開始に失敗した際に、再開を試みるまでの待機時間（ミリ秒）。
    /// PlayMuse が排他モードでデバイスを占有している間は <see cref="NAudio.CoreAudioApi.AudioDeviceInUseException"/>
    /// による失敗が繰り返されるが、共有モードへ戻った際の検知遅延を避けるため固定間隔でリトライする
    /// （失敗時のログは Information レベルに抑えてノイズを防いでいる）。
    /// </summary>
    private const int RecordingRetryDelayMilliseconds = 1000;

    /// <summary>
    /// ログ出力用のロガー。
    /// </summary>
    private readonly ILogger<WasapiLoopbackAudioService> _logger;

    /// <summary>
    /// キャプチャの開始・停止・デバイス切り替え処理を排他制御するための同期オブジェクト。
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// 既定の再生デバイスの取得や、デバイス変更通知クライアントの生成に使用する列挙子。
    /// </summary>
    private readonly MMDeviceEnumerator _enumerator = new();

    /// <summary>
    /// 現在使用中の FFT サイズ（サンプル数、2 のべき乗）。
    /// </summary>
    private int _fftSize = BaseFftSize;

    /// <summary>
    /// 現在の <see cref="_fftSize"/> に対応する 2 のべき指数（FFT 計算に渡す値）。
    /// </summary>
    private int _fftPow = (int)Math.Log2(BaseFftSize);

    /// <summary>
    /// FFT 計算のために L/R をミックスしたモノラルサンプルを蓄積するリングバッファ。
    /// </summary>
    private float[] _fftCircularBuffer = new float[BaseFftSize];

    /// <summary>
    /// FFT 計算用の複素数ワークバッファ（窓関数適用後の入力および計算結果を保持）。
    /// </summary>
    private Complex[] _fftWorkBuffer = new Complex[BaseFftSize];

    /// <summary>
    /// <see cref="_fftCircularBuffer"/> への次回書き込み位置（リングバッファの先頭インデックス）。
    /// </summary>
    private int _fftWritePos;

    /// <summary>
    /// <see cref="_fftCircularBuffer"/> に書き込まれた有効サンプル数（最大で <see cref="_fftSize"/>）。
    /// </summary>
    private int _fftFilledCount;

    /// <summary>
    /// 前回の FFT 計算からリングバッファに書き込まれたサンプル数。
    /// 一定数（FFT サイズの 1/4）溜まるごとに再計算する間引き制御に使用する。
    /// </summary>
    private int _fftSamplesSinceLastCompute;

    /// <summary>
    /// 現在キャプチャ中のデバイスのサンプルレート（Hz）。
    /// </summary>
    private int _sampleRate = 44100;

    /// <summary>
    /// 直近に計算されたスペクトラムバンドごとの値（0.0～1.0 に正規化済み）。
    /// </summary>
    private float[] _lastSpectrum = new float[SpectrumBandCount];

    /// <summary>
    /// 既定の再生デバイス変更を検知するための通知クライアント。<see cref="Start"/> 実行中のみ生成される。
    /// </summary>
    private MMDeviceNotificationClient? _notificationClient;

    /// <summary>
    /// 現在稼働中の WASAPI ループバックレコーダー。キャプチャ未開始時は null。
    /// </summary>
    private WasapiRecorder? _recorder;

    /// <summary>
    /// <see cref="_recorder"/> が入れ替わるたびにインクリメントされる世代番号。
    /// 古いレコーダーの <see cref="WasapiRecorder.RecordingStopped"/> が、
    /// 既に新しいレコーダーへ差し替わった後に遅延発火しても誤って再起動処理が走らないようにするために使用する。
    /// </summary>
    private int _recorderGeneration;

    /// <summary>
    /// 予期しない停止（<see cref="WasapiRecorder.RecordingStopped"/>）後の再起動リトライをキャンセルするためのトークンソース。
    /// <see cref="Stop"/> や意図的なデバイス切り替え時にキャンセルし、再起動処理が二重に走らないようにする。
    /// </summary>
    private CancellationTokenSource? _restartCts;

    /// <summary>
    /// <see cref="Start"/> が呼び出され、キャプチャが有効化されているかどうか。
    /// </summary>
    private bool _started;

    /// <summary>
    /// <see cref="Dispose"/> が実行済みかどうか。
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// PlayMuse Named Pipe が接続中（排他モードでの配信中）など、WASAPI ループバックキャプチャが
    /// 本質的に不要な間 <see cref="Suspend"/> によって true に設定される。
    /// true の間はキャプチャ開始・リトライを一切行わず、無駄な <see cref="NAudio.CoreAudioApi.AudioDeviceInUseException"/> と
    /// それに伴うログの大量発生を防ぐ。
    /// </summary>
    private bool _suspended;

    /// <summary>
    /// <see cref="WasapiLoopbackAudioService"/> の新しいインスタンスを初期化する。
    /// </summary>
    /// <param name="logger">ログ出力に使用するロガー。</param>
    public WasapiLoopbackAudioService(ILogger<WasapiLoopbackAudioService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 音声データ（左右チャンネルのレベルおよびスペクトラム）が取得されるたびに発生するイベント。
    /// </summary>
    public event EventHandler<AudioCaptureDataEventArgs>? DataAvailable;

    /// <summary>
    /// キャプチャ対象のデバイス情報が変化した（開始・デバイス切り替え・取得失敗など）際に発生するイベント。
    /// </summary>
    public event EventHandler<AudioCaptureDeviceInfo>? DeviceChanged;

    /// <summary>
    /// キャプチャの開始（<see cref="Start"/>）で Connected、停止（<see cref="Stop"/>）で Disconnected を発生させる。
    /// WASAPI ループバックはデバイス切断の detection を行わないため、現状は開始・停止のみに連動する。
    /// </summary>
    public event EventHandler<AudioCaptureConnectionState>? ConnectionStateChanged;

    /// <summary>
    /// 既定の再生デバイスのループバックキャプチャを開始し、既定デバイス変更の監視も開始する。
    /// 既に開始済みの場合は何もしない。
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            _started = true;

            _notificationClient = _enumerator.CreateNotificationClient();
            _notificationClient.DefaultDeviceChanged += OnDefaultDeviceChanged;

            if (!_suspended)
            {
                StartCaptureOnDefaultDevice();
            }
        }

        ConnectionStateChanged?.Invoke(this, AudioCaptureConnectionState.Connected);
    }

    /// <summary>
    /// ループバックキャプチャおよび既定デバイス変更の監視を停止する。
    /// 未開始の場合は何もしない。
    /// </summary>
    public void Stop()
    {
        lock (_gate)
        {
            if (!_started)
            {
                return;
            }

            _started = false;

            if (_notificationClient is not null)
            {
                _notificationClient.DefaultDeviceChanged -= OnDefaultDeviceChanged;
                _notificationClient.Dispose();
                _notificationClient = null;
            }

            StopCaptureLocked();
        }

        ConnectionStateChanged?.Invoke(this, AudioCaptureConnectionState.Disconnected);
    }

    /// <summary>
    /// WASAPI ループバックキャプチャを一時停止する。PlayMuse Named Pipe が接続中など、
    /// このサービスのデータが不要な間に呼び出すことで、既定デバイスが排他モードで占有されている間の
    /// 無駄なキャプチャ開始リトライ（および、それに伴う例外・ログ）を防ぐ。
    /// <see cref="Start"/>/<see cref="Stop"/> 自体の状態（既定デバイス変更の監視など）には影響しない。
    /// </summary>
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

    /// <summary>
    /// <see cref="Suspend"/> で一時停止していたキャプチャを再開する。
    /// <see cref="Start"/> 済みであれば、直ちに既定デバイスでのキャプチャ開始を試みる。
    /// </summary>
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

    /// <summary>
    /// 既定のオーディオデバイスが変更された際のハンドラー。
    /// 再生（Render）デバイスの変更の場合のみ、キャプチャ対象を切り替える。
    /// </summary>
    /// <param name="sender">イベント送信元。</param>
    /// <param name="e">変更されたデバイスの種別（Render/Capture）を含むイベント引数。</param>
    private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
    {
        // 再生用（Render）の既定デバイス変更にのみ追従する。
        if (e.Flow != DataFlow.Render)
        {
            return;
        }

        lock (_gate)
        {
            if (!_started || _suspended)
            {
                return;
            }

            _logger.LogInformation("既定の再生デバイスが変更されたため、音量メーターのキャプチャ対象を切り替えます。");
            StopCaptureLocked();
            StartCaptureOnDefaultDevice();
        }
    }

    /// <summary>
    /// 既定の再生デバイスを取得し、その WASAPI ループバックキャプチャを開始する。
    /// デバイスが取得できない場合や開始に失敗した場合は、警告ログを出力し <see cref="DeviceChanged"/> で失敗を通知する。
    /// </summary>
    private void StartCaptureOnDefaultDevice()
    {
        try
        {
            if (!_enumerator.TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out var device))
            {
                _logger.LogWarning("既定の再生デバイスが見つからないため、音量メーターのキャプチャを開始できません。");
                DeviceChanged?.Invoke(this, new AudioCaptureDeviceInfo(null, 0, 0));
                ScheduleRestartLocked();
                return;
            }

            var recorder = new WasapiRecorderBuilder()
                .WithDevice(device)
                .WithLoopbackCapture()
                .Build();

            var generation = ++_recorderGeneration;
            recorder.DataAvailable += OnDataAvailable;
            recorder.RecordingStopped += (sender, args) => OnRecordingStopped(generation, args);

            _sampleRate = recorder.WaveFormat.SampleRate > 0 ? recorder.WaveFormat.SampleRate : 44100;
            UpdateFftSizeForSampleRate(_sampleRate);
            _fftWritePos = 0;
            _fftFilledCount = 0;
            _fftSamplesSinceLastCompute = 0;

            recorder.StartRecording();

            _recorder = recorder;

            // 直前のリトライ待機が残っていれば不要になったので解除する。
            _restartCts?.Cancel();
            _restartCts?.Dispose();
            _restartCts = null;

            DeviceChanged?.Invoke(this, new AudioCaptureDeviceInfo(device.FriendlyName, _sampleRate, recorder.WaveFormat.Channels));
        }
        catch (AudioDeviceInUseException)
        {
            // PlayMuse が排他モードでデバイスを占有している間は想定内の失敗であるため、
            // 警告ログ・スタックトレースを出さずに情報ログのみとし、解放されるまで定期的に再試行する。
            _logger.LogInformation("既定の再生デバイスが排他モードで使用中のため、ループバックキャプチャを開始できません。解放され次第再試行します。");
            DeviceChanged?.Invoke(this, new AudioCaptureDeviceInfo(null, 0, 0));
            ScheduleRestartLocked();
        }
        catch (Exception ex)
        {
            // デバイスの再構成中（AudioDeviceDisconnectedException）など、その他の理由で開始に失敗することがある。
            // この場合は警告を出したうえで、解放・復旧されるまで定期的に再試行する。
            _logger.LogWarning(ex, "既定の再生デバイスのループバックキャプチャ開始に失敗しました。再試行します。");
            DeviceChanged?.Invoke(this, new AudioCaptureDeviceInfo(null, 0, 0));
            ScheduleRestartLocked();
        }
    }

    /// <summary>
    /// 現在の <see cref="_recorderGeneration"/> を対象として、キャプチャ再開のリトライをスケジュールする。
    /// <see cref="_gate"/> のロック内から呼び出すことを前提とする。既存の保留中リトライがあればキャンセルして置き換える。
    /// </summary>
    private void ScheduleRestartLocked()
    {
        if (!_started || _suspended)
        {
            return;
        }

        _restartCts?.Cancel();
        _restartCts?.Dispose();
        var cts = new CancellationTokenSource();
        _restartCts = cts;

        var generation = _recorderGeneration;
        _ = ScheduleRestartAsync(generation, cts.Token);
    }


    /// <summary>
    /// 稼働中のレコーダーが（意図的な停止ではなく）予期せず停止した際に呼び出される。
    /// PlayMuse が排他モードでデバイスを占有した後に解放した場合など、再生デバイスが
    /// 再び利用可能になっているはずなので、短い待機を挟んでキャプチャの再開を試みる。
    /// </summary>
    /// <param name="generation">キャプチャ開始時に採番された世代番号。現在の <see cref="_recorder"/> の世代と一致しない場合は無視する。</param>
    /// <param name="args">NAudio から渡される停止理由（例外情報を含む場合がある）。</param>
    private void OnRecordingStopped(int generation, StoppedEventArgs args)
    {
        lock (_gate)
        {
            // サービス自体が停止済み、またはこのコールバックが古い世代（意図的な切り替え/停止で
            // 既に破棄されたレコーダー）に属する場合は、現在の状態に影響を与えない。
            if (!_started || generation != _recorderGeneration)
            {
                return;
            }

            if (args.Exception is not null)
            {
                _logger.LogWarning(args.Exception, "ループバックキャプチャが予期せず停止しました。再接続を試みます。");
            }
            else
            {
                _logger.LogInformation("ループバックキャプチャが停止しました（デバイス占有解放など）。再接続を試みます。");
            }

            ScheduleRestartLocked();
        }
    }

    /// <summary>
    /// 一定時間待機した後、まだ有効な状況であればキャプチャの再開を試みる。
    /// </summary>
    /// <param name="generation">再起動をトリガーしたレコーダーの世代番号。</param>
    /// <param name="token">サービス停止や別の再起動要求でキャンセルされるトークン。</param>
    private async Task ScheduleRestartAsync(int generation, CancellationToken token)
    {
        try
        {
            await Task.Delay(RecordingRetryDelayMilliseconds, token).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        lock (_gate)
        {
            if (!_started || generation != _recorderGeneration || token.IsCancellationRequested || _suspended)
            {
                return;
            }

            _logger.LogInformation("ループバックキャプチャの再開を試みます。");
            StartCaptureOnDefaultDevice();
        }
    }


    /// <summary>
    /// 現在稼働中のレコーダーを停止・破棄する。<see cref="_gate"/> のロック内から呼び出すことを前提とする。
    /// レコーダーが存在しない場合は何もしない。
    /// </summary>
    private void StopCaptureLocked()
    {
        _restartCts?.Cancel();
        _restartCts?.Dispose();
        _restartCts = null;

        // 世代を進めておくことで、これから破棄するレコーダーの RecordingStopped が
        // 後から発火しても、自動再起動のトリガーとして扱われないようにする。
        _recorderGeneration++;

        if (_recorder is null)
        {
            return;
        }

        _recorder.DataAvailable -= OnDataAvailable;
        try
        {
            _recorder.StopRecording();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ループバックキャプチャの停止に失敗しました。");
        }

        _recorder.Dispose();
        _recorder = null;
    }

    /// <summary>
    /// WASAPI から音声データが到着した際に呼び出され、左右チャンネルのピークレベルを算出するとともに、
    /// モノラルミックスサンプルを FFT 用バッファへ供給し、<see cref="DataAvailable"/> イベントを発生させる。
    /// </summary>
    /// <param name="buffer">到着した生の音声バイト列。</param>
    /// <param name="flags">オーディオクライアントのバッファフラグ（無音判定などに使用可能）。</param>
    /// <param name="devicePosition">デバイス上のサンプル位置。</param>
    /// <param name="qpcPosition">QueryPerformanceCounter 基準のタイムスタンプ。</param>
    private void OnDataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        var recorder = _recorder;
        if (recorder is null || buffer.IsEmpty)
        {
            return;
        }

        var format = recorder.WaveFormat;
        var channels = Math.Max(1, format.Channels);

        // WASAPI の既定フォーマットは WAVEFORMATEXTENSIBLE で返されることが多く、
        // 実際のサンプル形式（PCM / IEEE Float）と有効ビット数は SubFormat 側に格納されている。
        var encoding = format.Encoding;
        var bitsPerSample = format.BitsPerSample;
        if (format is WaveFormatExtensible extensible)
        {
            var standard = extensible.ToStandardWaveFormat();
            encoding = standard.Encoding;
            bitsPerSample = extensible.ValidBitsPerSample > 0 ? extensible.ValidBitsPerSample : standard.BitsPerSample;
        }

        float leftPeak = 0f;
        float rightPeak = 0f;
        var sampleCount = 0;

        if (encoding == WaveFormatEncoding.IeeeFloat)
        {
            switch (bitsPerSample)
            {
                case 32:
                    {
                        var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(buffer);
                        for (var i = 0; i + channels <= samples.Length; i += channels)
                        {
                            var rawL = samples[i];
                            var rawR = channels > 1 ? samples[i + 1] : rawL;
                            var l = Math.Abs(rawL);
                            var r = Math.Abs(rawR);
                            if (l > leftPeak) leftPeak = l;
                            if (r > rightPeak) rightPeak = r;
                            PushFftSample((rawL + rawR) * 0.5f);
                            sampleCount++;
                        }
                        break;
                    }
                case 64:
                    {
                        var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, double>(buffer);
                        for (var i = 0; i + channels <= samples.Length; i += channels)
                        {
                            var rawL = (float)samples[i];
                            var rawR = channels > 1 ? (float)samples[i + 1] : rawL;
                            var l = Math.Abs(rawL);
                            var r = Math.Abs(rawR);
                            if (l > leftPeak) leftPeak = l;
                            if (r > rightPeak) rightPeak = r;
                            PushFftSample((rawL + rawR) * 0.5f);
                            sampleCount++;
                        }
                        break;
                    }
                default:
                    _logger.LogWarning("未対応の IEEE Float ビット数です: {BitsPerSample}", bitsPerSample);
                    return;
            }
        }
        else if (encoding == WaveFormatEncoding.Pcm || encoding == WaveFormatEncoding.Extensible)
        {
            switch (bitsPerSample)
            {
                case 8:
                    {
                        // 8bit PCM は符号なし（中心値 128）。
                        for (var i = 0; i + channels <= buffer.Length; i += channels)
                        {
                            var rawL = (buffer[i] - 128) / 128f;
                            var rawR = channels > 1 ? (buffer[i + 1] - 128) / 128f : rawL;
                            var l = Math.Abs(rawL);
                            var r = Math.Abs(rawR);
                            if (l > leftPeak) leftPeak = l;
                            if (r > rightPeak) rightPeak = r;
                            PushFftSample((rawL + rawR) * 0.5f);
                            sampleCount++;
                        }
                        break;
                    }
                case 16:
                    {
                        var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(buffer);
                        for (var i = 0; i + channels <= samples.Length; i += channels)
                        {
                            var rawL = samples[i] / 32768f;
                            var rawR = channels > 1 ? samples[i + 1] / 32768f : rawL;
                            var l = Math.Abs(rawL);
                            var r = Math.Abs(rawR);
                            if (l > leftPeak) leftPeak = l;
                            if (r > rightPeak) rightPeak = r;
                            PushFftSample((rawL + rawR) * 0.5f);
                            sampleCount++;
                        }
                        break;
                    }
                case 24:
                    {
                        // 24bit PCM はパック形式（1 サンプル = 3 バイト）のため専用に読み取る。
                        var bytesPerFrame = 3 * channels;
                        for (var offset = 0; offset + bytesPerFrame <= buffer.Length; offset += bytesPerFrame)
                        {
                            var rawL = ReadInt24(buffer, offset) / 8388608f;
                            var rawR = channels > 1 ? ReadInt24(buffer, offset + 3) / 8388608f : rawL;
                            var l = Math.Abs(rawL);
                            var r = Math.Abs(rawR);
                            if (l > leftPeak) leftPeak = l;
                            if (r > rightPeak) rightPeak = r;
                            PushFftSample((rawL + rawR) * 0.5f);
                            sampleCount++;
                        }
                        break;
                    }
                case 32:
                    {
                        var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(buffer);
                        for (var i = 0; i + channels <= samples.Length; i += channels)
                        {
                            var rawL = samples[i] / 2147483648f;
                            var rawR = channels > 1 ? samples[i + 1] / 2147483648f : rawL;
                            var l = Math.Abs(rawL);
                            var r = Math.Abs(rawR);
                            if (l > leftPeak) leftPeak = l;
                            if (r > rightPeak) rightPeak = r;
                            PushFftSample((rawL + rawR) * 0.5f);
                            sampleCount++;
                        }
                        break;
                    }
                default:
                    _logger.LogWarning("未対応の PCM ビット数です: {BitsPerSample}", bitsPerSample);
                    return;
            }
        }
        else
        {
            // 未対応フォーマットの場合はレベル更新をスキップする。
            _logger.LogWarning("未対応の音声エンコーディングです: {Encoding}", encoding);
            return;
        }

        if (sampleCount == 0)
        {
            return;
        }

        DataAvailable?.Invoke(this, new AudioCaptureDataEventArgs(
            Math.Clamp(leftPeak, 0f, 1f),
            Math.Clamp(rightPeak, 0f, 1f),
            _lastSpectrum));
    }

    /// <summary>
    /// 24bit PCM（リトルエンディアン、1 サンプル 3 バイト）の値を読み取り、符号拡張した 32bit 整数として返す。
    /// </summary>
    /// <param name="buffer">読み取り対象のバイト列。</param>
    /// <param name="offset">サンプルの先頭バイトを指すオフセット。</param>
    /// <returns>符号拡張済みの 32bit 整数値。</returns>
    private static int ReadInt24(ReadOnlySpan<byte> buffer, int offset)
    {
        var value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
        // 符号拡張（24bit → 32bit）。
        if ((value & 0x00800000) != 0)
        {
            value |= unchecked((int)0xFF000000);
        }
        return value;
    }

    /// <summary>
    /// サンプルレートに応じて FFT サイズを決定する。基準（44.1kHz 付近）では <see cref="BaseFftSize"/> を用い、
    /// サンプルレートが高いほど低域の周波数分解能を維持するために FFT サイズを比例して拡大する。
    /// </summary>
    /// <param name="sampleRate">キャプチャ中のデバイスのサンプルレート（Hz）。</param>
    private void UpdateFftSizeForSampleRate(int sampleRate)
    {
        var ratio = (double)sampleRate / BaseSampleRate;
        var desired = BaseFftSize * ratio;

        var size = MinFftLength;
        while (size < desired && size < MaxFftLength)
        {
            size *= 2;
        }

        if (size == _fftSize)
        {
            return;
        }

        _fftSize = size;
        _fftPow = (int)Math.Log2(size);
        _fftCircularBuffer = new float[size];
        _fftWorkBuffer = new Complex[size];
        _fftWritePos = 0;
        _fftFilledCount = 0;
        _fftSamplesSinceLastCompute = 0;
    }

    /// <summary>
    /// L/R をミックスしたモノラルサンプルを FFT 用のリングバッファへ書き込む。
    /// 十分なサンプル数が溜まったら FFT を計算し、<see cref="_lastSpectrum"/> を更新する。
    /// </summary>
    /// <param name="sample">L/R をミックスしたモノラルサンプル値。</param>
    private void PushFftSample(float sample)
    {
        var fftSize = _fftSize;
        _fftCircularBuffer[_fftWritePos] = sample;
        _fftWritePos = (_fftWritePos + 1) % fftSize;
        if (_fftFilledCount < fftSize)
        {
            _fftFilledCount++;
        }

        _fftSamplesSinceLastCompute++;

        // 描画負荷を抑えるため、バッファが満タンかつ一定間隔（FFTサイズの 1/4）ごとに再計算する。
        if (_fftFilledCount >= fftSize && _fftSamplesSinceLastCompute >= fftSize / 4)
        {
            _fftSamplesSinceLastCompute = 0;
            ComputeSpectrum();
        }
    }

    /// <summary>
    /// リングバッファに蓄積された最新のサンプルに Hamming 窓を適用して FFT を実行し、
    /// <see cref="SpectrumBandDefinitions"/> のバンド境界に基づいて <see cref="SpectrumBandCount"/> 個の
    /// バンドごとの正規化済みレベルを算出して <see cref="_lastSpectrum"/> を更新する。
    /// </summary>
    private void ComputeSpectrum()
    {
        var fftSize = _fftSize;

        // リングバッファを時系列順に並べ替えつつ、Hamming 窓を適用して複素配列へコピーする。
        // 窓関数の合計値（windowSum）は、窓によるゲイン損失を後段で補正するために保持しておく。
        var windowSum = 0.0;
        for (var i = 0; i < fftSize; i++)
        {
            var index = (_fftWritePos + i) % fftSize;
            var window = (float)FastFourierTransform.HammingWindow(i, fftSize);
            windowSum += window;
            _fftWorkBuffer[i].X = _fftCircularBuffer[index] * window;
            _fftWorkBuffer[i].Y = 0f;
        }

        FastFourierTransform.FFT(true, _fftPow, _fftWorkBuffer);

        // NAudio の FFT(forward=true) は内部で 1/FftSize にスケーリングするため、
        // 窓関数のゲイン損失（windowSum 分だけ振幅が小さくなる）と片側スペクトラム分を
        // 2 * FftSize / windowSum で補正し、0dBFS を基準とした振幅に揃える。
        var normalization = windowSum > 0 ? 2.0 * fftSize / windowSum : 1.0;

        var spectrum = new float[SpectrumBandCount];
        var binCount = fftSize / 2;
        var sampleRate = _sampleRate;

        // 可聴域を、各バンドの代表周波数の幾何平均から求めた境界（SpectrumBandDefinitions）で
        // SpectrumBandCount 個のバンドに分割する（対数均等割りではなく固定目盛りを使用）。
        var bands = SpectrumBandDefinitions.Bands;
        var binHz = (double)sampleRate / fftSize;

        for (var band = 0; band < SpectrumBandCount; band++)
        {
            var bandRange = bands[band];

            var binStart = Math.Max(1, (int)Math.Floor(bandRange.LowFrequencyHz / binHz));
            var binEnd = Math.Max(binStart + 1, (int)Math.Ceiling(bandRange.HighFrequencyHz / binHz));
            binEnd = Math.Min(binEnd, binCount);

            // バンド内で最も大きいビンの振幅をそのバンドの代表値として採用する（平均ではなく最大値）。
            // 平均を取ると、特に高域側のように1バンドに含まれるビン数が多い場合にピーク成分が
            // 薄まってしまい、実際より不自然に弱いレベルとして表示されるため。
            float maxMagnitude = 0f;
            for (var bin = binStart; bin < binEnd; bin++)
            {
                var re = _fftWorkBuffer[bin].X;
                var im = _fftWorkBuffer[bin].Y;
                var magnitude = MathF.Sqrt(re * re + im * im);
                if (magnitude > maxMagnitude)
                {
                    maxMagnitude = magnitude;
                }
            }

            spectrum[band] = (float)ToSpectrumScale(maxMagnitude * (float)normalization);
        }

        _lastSpectrum = spectrum;
    }

    /// <summary>
    /// FFT の振幅値を dB 変換したうえで、<see cref="SpectrumMinDecibels"/>〜0dB を 0.0〜1.0 に正規化する。
    /// </summary>
    private static double ToSpectrumScale(float magnitude)
    {
        if (magnitude <= 0f)
        {
            return 0.0;
        }

        var decibels = 20.0 * Math.Log10(magnitude);
        if (decibels <= SpectrumMinDecibels)
        {
            return 0.0;
        }

        return Math.Clamp((decibels - SpectrumMinDecibels) / -SpectrumMinDecibels, 0.0, 1.0);
    }

    /// <summary>
    /// キャプチャを停止し、内部で保持しているアンマネージリソース（デバイス列挙子）を解放する。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _enumerator.Dispose();
        _disposed = true;
    }
}
