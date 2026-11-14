using Microsoft.Extensions.Logging;
using MuseWiser.Services.Audio.PlayMuse;

namespace MuseWiser.Services.Audio;

/// <summary>
/// <see cref="Models.Enums.AudioSourceMode.Auto"/> 用の <see cref="IAudioCaptureService"/> 実装。
/// PlayMuse 名前付きパイプへの接続を優先し、接続されている間はそのデータ（<see cref="DataAvailable"/>、
/// <see cref="DeviceChanged"/>）を採用する。パイプが未接続（PlayMuse 未起動・共有モード再生中等）の間は
/// WASAPI ループバックキャプチャのデータへ自動的にフォールバックする。
/// 両方のキャプチャサービスは常時 <see cref="Start"/> されており、採用元の切り替えのみを行う。
/// </summary>
public sealed class AdaptiveAudioCaptureService : IAudioCaptureService
{
    private readonly WasapiLoopbackAudioService _loopbackService;
    private readonly PlayMuseNamedPipeAudioCaptureService _pipeService;
    private readonly ILogger<AdaptiveAudioCaptureService> _logger;

    private readonly object _gate = new();
    private bool _started;
    private bool _disposed;
    private volatile bool _isPipeActive;

    public event EventHandler<AudioCaptureDataEventArgs>? DataAvailable;

    public event EventHandler<AudioCaptureDeviceInfo>? DeviceChanged;

    public event EventHandler<AudioCaptureConnectionState>? ConnectionStateChanged;

    public AdaptiveAudioCaptureService(
        WasapiLoopbackAudioService loopbackService,
        PlayMuseNamedPipeAudioCaptureService pipeService,
        ILogger<AdaptiveAudioCaptureService> logger)
    {
        _loopbackService = loopbackService;
        _pipeService = pipeService;
        _logger = logger;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            _started = true;

            _pipeService.ConnectionStateChanged += OnPipeConnectionStateChanged;
            _pipeService.DataAvailable += OnPipeDataAvailable;
            _pipeService.DeviceChanged += OnPipeDeviceChanged;

            _loopbackService.DataAvailable += OnLoopbackDataAvailable;
            _loopbackService.DeviceChanged += OnLoopbackDeviceChanged;

            // 両方を常時起動し、PlayMuse パイプの接続状態のみで採用元を切り替える。
            _loopbackService.Start();
            _pipeService.Start();
        }

        ConnectionStateChanged?.Invoke(this, AudioCaptureConnectionState.Connected);
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_started)
            {
                return;
            }

            _started = false;

            _pipeService.ConnectionStateChanged -= OnPipeConnectionStateChanged;
            _pipeService.DataAvailable -= OnPipeDataAvailable;
            _pipeService.DeviceChanged -= OnPipeDeviceChanged;

            _loopbackService.DataAvailable -= OnLoopbackDataAvailable;
            _loopbackService.DeviceChanged -= OnLoopbackDeviceChanged;

            _pipeService.Stop();
            _loopbackService.Stop();
        }

        ConnectionStateChanged?.Invoke(this, AudioCaptureConnectionState.Disconnected);
    }

    private void OnPipeConnectionStateChanged(object? sender, AudioCaptureConnectionState state)
    {
        var isActive = state == AudioCaptureConnectionState.Connected;
        if (_isPipeActive == isActive)
        {
            return;
        }

        _isPipeActive = isActive;

        if (isActive)
        {
            // PlayMuse パイプが有効な間は WASAPI ループバックのデータは採用しないため、
            // 無駄なキャプチャ開始リトライ（排他モード中は AudioDeviceInUseException が発生し続ける）を止める。
            _loopbackService.Suspend();
        }
        else
        {
            _loopbackService.Resume();
        }

        _logger.LogInformation(
            isActive
                ? "AdaptiveAudioCaptureService: PlayMuse パイプへ接続したため、データ採用元をパイプへ切り替えます。"
                : "AdaptiveAudioCaptureService: PlayMuse パイプが切断されたため、データ採用元を WASAPI ループバックへ切り替えます。");
    }

    private void OnPipeDataAvailable(object? sender, AudioCaptureDataEventArgs e)
    {
        if (_isPipeActive)
        {
            DataAvailable?.Invoke(this, e);
        }
    }

    private void OnPipeDeviceChanged(object? sender, AudioCaptureDeviceInfo e)
    {
        if (_isPipeActive)
        {
            DeviceChanged?.Invoke(this, e);
        }
    }

    private void OnLoopbackDataAvailable(object? sender, AudioCaptureDataEventArgs e)
    {
        if (!_isPipeActive)
        {
            DataAvailable?.Invoke(this, e);
        }
    }

    private void OnLoopbackDeviceChanged(object? sender, AudioCaptureDeviceInfo e)
    {
        if (!_isPipeActive)
        {
            DeviceChanged?.Invoke(this, e);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();

        _loopbackService.Dispose();
        _pipeService.Dispose();
    }
}
