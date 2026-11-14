using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MuseWiser.Models;
using PlayMuse.LevelMeterIpc;

namespace MuseWiser.Services.Audio.PlayMuse;

/// <summary>
/// PlayMuse（別プロセスの音楽プレーヤー）が WASAPI 排他モードで再生中に公開する
/// 名前付きパイプへ接続し、音量レベル・スペクトラムレベル・出力デバイス情報を受信して
/// <see cref="IAudioCaptureService"/> として公開するクライアント実装。
/// PlayMuse が未起動、または共有モードに切り替わってパイプサーバーが停止している間は、
/// 接続試行とリトライを繰り返しつつ <see cref="ConnectionStateChanged"/> で Disconnected を通知する。
/// </summary>
public sealed class PlayMuseNamedPipeAudioCaptureService : IAudioCaptureService
{
    private readonly PlayMusePipeOptions _options;
    private readonly ILogger<PlayMuseNamedPipeAudioCaptureService> _logger;

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _connectionLoopTask;
    private bool _started;
    private bool _disposed;
    private volatile bool _isConnected;

    public event EventHandler<AudioCaptureDataEventArgs>? DataAvailable;

    public event EventHandler<AudioCaptureDeviceInfo>? DeviceChanged;

    public event EventHandler<AudioCaptureConnectionState>? ConnectionStateChanged;

    public PlayMuseNamedPipeAudioCaptureService(
        IOptions<PlayMusePipeOptions> options,
        ILogger<PlayMuseNamedPipeAudioCaptureService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// キャプチャを開始する。バックグラウンドで PlayMuse 側パイプへの接続・再接続ループを開始する。
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
            _cts = new CancellationTokenSource();
            _connectionLoopTask = RunConnectionLoopAsync(_cts.Token);
        }
    }

    /// <summary>
    /// キャプチャを停止し、現在の接続を切断する。
    /// </summary>
    public void Stop()
    {
        CancellationTokenSource? cts;

        lock (_gate)
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            cts = _cts;
            _cts = null;
            _connectionLoopTask = null;
        }

        cts?.Cancel();
        cts?.Dispose();

        SetConnected(false);
    }

    private async Task RunConnectionLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeClientStream? pipeClient = null;
            try
            {
                pipeClient = new NamedPipeClientStream(".", _options.PipeName, PipeDirection.In, PipeOptions.Asynchronous);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("PlayMuse 連携: パイプ '{PipeName}' への接続を試みます。", _options.PipeName);
                }

                await pipeClient.ConnectAsync(_options.ReconnectIntervalMilliseconds, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("PlayMuse 連携: パイプ '{PipeName}' に接続しました。", _options.PipeName);
                SetConnected(true);

                await ReceiveLoopAsync(pipeClient, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Stop/Dispose による正常終了。
                break;
            }
            catch (TimeoutException)
            {
                // PlayMuse が未起動、または排他モードでないため接続できなかった。再試行する。
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "PlayMuse 連携: パイプ接続中に例外が発生しました。再接続を試みます。");
            }
            finally
            {
                pipeClient?.Dispose();
                SetConnected(false);
            }

            try
            {
                await Task.Delay(_options.ReconnectIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ReceiveLoopAsync(NamedPipeClientStream pipeClient, CancellationToken cancellationToken)
    {
        while (pipeClient.IsConnected && !cancellationToken.IsCancellationRequested)
        {
            var message = await LevelMeterMessageReader.ReadMessageAsync(pipeClient, cancellationToken).ConfigureAwait(false);

            switch (message)
            {
                case LevelMeterDeviceInfoMessage deviceInfo:
                    RaiseDeviceChanged(deviceInfo);
                    break;

                case LevelMeterFrameMessage frame:
                    RaiseDataAvailable(new AudioCaptureDataEventArgs(frame.Left, frame.Right, frame.Spectrum));
                    break;
            }
        }
    }

    private void SetConnected(bool connected)
    {
        if (_isConnected == connected)
        {
            return;
        }

        _isConnected = connected;
        ConnectionStateChanged?.Invoke(
            this,
            connected ? AudioCaptureConnectionState.Connected : AudioCaptureConnectionState.Disconnected);
    }

    /// <summary>
    /// 受信した <see cref="LevelMeterDeviceInfoMessage"/> を、既存の <see cref="DeviceChanged"/> イベントで
    /// 利用できる形式（デバイス名に排他モード表示を付加）へ変換して発行する。
    /// </summary>
    private void RaiseDeviceChanged(LevelMeterDeviceInfoMessage message)
    {
        var deviceName = message.DeviceName;
        if (!string.IsNullOrEmpty(deviceName) && message.IsExclusiveMode)
        {
            deviceName = $"{deviceName}（排他モード）";
        }

        DeviceChanged?.Invoke(this, new AudioCaptureDeviceInfo(deviceName, message.SampleRate, message.Channels));
    }

    private void RaiseDataAvailable(AudioCaptureDataEventArgs data)
    {
        DataAvailable?.Invoke(this, data);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}
