using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using PlayMuse.Core.Models;
using PlayMuse.LevelMeterIpc;

namespace PlayMuse.Core.Services;

/// <summary>
/// <see cref="ILevelMeterBroadcastService"/> の既定実装。
/// PlayMuse が WASAPI 排他モードで再生中の間だけ Named Pipe サーバーを起動し、
/// 音量レベル・スペクトラムレベル・出力デバイス情報を MuseWiser へブロードキャストする。
/// 共有モードに戻る、または再生が停止した場合はサーバーを停止する。
/// </summary>
public sealed class NamedPipeLevelMeterBroadcastService : ILevelMeterBroadcastService
{
    /// <summary>
    /// レベル／スペクトラムをブロードキャストする周期（ミリ秒）。
    /// PlayMuse のスペクトラムアナライザ表示更新（約33ms/30fps）に合わせる。
    /// </summary>
    private const int BroadcastIntervalMilliseconds = 33;

    private readonly IAudioPlaybackService playbackService;
    private readonly ISpectrumAnalyzerService spectrumAnalyzerService;
    private readonly IChannelLevelMeterService channelLevelMeterService;
    private readonly ILogger<NamedPipeLevelMeterBroadcastService> logger;

    private readonly object gate = new();
    private CancellationTokenSource? serverCts;
    private Task? serverTask;
    private bool started;
    private bool disposed;

    public NamedPipeLevelMeterBroadcastService(
        IAudioPlaybackService playbackService,
        ISpectrumAnalyzerService spectrumAnalyzerService,
        IChannelLevelMeterService channelLevelMeterService,
        ILogger<NamedPipeLevelMeterBroadcastService> logger)
    {
        this.playbackService = playbackService;
        this.spectrumAnalyzerService = spectrumAnalyzerService;
        this.channelLevelMeterService = channelLevelMeterService;
        this.logger = logger;
    }

    public void Start()
    {
        lock (gate)
        {
            if (started)
            {
                return;
            }

            started = true;
            playbackService.ActualShareModeChanged += OnActualShareModeChanged;

            if (playbackService.ActualShareMode == AudioShareMode.Exclusive)
            {
                StartServerLocked();
            }
        }
    }

    private void OnActualShareModeChanged(object? sender, AudioShareMode mode)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            if (mode == AudioShareMode.Exclusive)
            {
                StartServerLocked();
            }
            else
            {
                StopServerLocked();
            }
        }
    }

    private void StartServerLocked()
    {
        if (serverTask is not null)
        {
            return;
        }

        logger.LogInformation("NamedPipeLevelMeterBroadcastService: 排他モード検知のため Named Pipe サーバーを起動します。");

        serverCts = new CancellationTokenSource();
        serverTask = RunServerAsync(serverCts.Token);
    }

    private void StopServerLocked()
    {
        if (serverTask is null)
        {
            return;
        }

        logger.LogInformation("NamedPipeLevelMeterBroadcastService: 共有モードへの復帰のため Named Pipe サーバーを停止します。");

        serverCts?.Cancel();
        serverCts?.Dispose();
        serverCts = null;
        serverTask = null;
    }

    private async Task RunServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipeServer = null;
            try
            {
                pipeServer = new NamedPipeServerStream(
                    LevelMeterIpcDefaults.PipeName,
                    PipeDirection.Out,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipeServer.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                logger.LogInformation("NamedPipeLevelMeterBroadcastService: MuseWiser がパイプに接続しました。");

                await SendDeviceInfoAsync(pipeServer, cancellationToken).ConfigureAwait(false);
                await BroadcastLoopAsync(pipeServer, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // サーバー停止要求。ループを抜ける。
            }
            catch (IOException ex)
            {
                logger.LogDebug(ex, "NamedPipeLevelMeterBroadcastService: パイプ接続が切断されました。再接続を待機します。");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "NamedPipeLevelMeterBroadcastService: パイプサーバー処理中に例外が発生しました。再接続を待機します。");
            }
            finally
            {
                pipeServer?.Dispose();
            }
        }
    }

    private Task SendDeviceInfoAsync(NamedPipeServerStream pipeServer, CancellationToken cancellationToken)
    {
        var message = new LevelMeterDeviceInfoMessage(
            playbackService.OutputDevice?.Name,
            playbackService.OutputFormat?.SampleRate ?? 0,
            playbackService.OutputFormat?.Channels ?? 0,
            IsExclusiveMode: true);

        return LevelMeterMessageWriter.WriteDeviceInfoAsync(pipeServer, message, cancellationToken);
    }

    private async Task BroadcastLoopAsync(NamedPipeServerStream pipeServer, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(BroadcastIntervalMilliseconds));

        while (pipeServer.IsConnected && await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var (left, right) = channelLevelMeterService.GetLevels();
            var bandLevels = spectrumAnalyzerService.GetBandLevels();

            var spectrum = new float[bandLevels.Count];
            for (var i = 0; i < bandLevels.Count; i++)
            {
                spectrum[i] = NormalizeLevel(bandLevels[i].Level);
            }

            var frame = new LevelMeterFrameMessage(
                NormalizeLevel(left.Level),
                NormalizeLevel(right.Level),
                spectrum);

            await LevelMeterMessageWriter.WriteFrameAsync(pipeServer, frame, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// PlayMuse 内部の表示用レベル（0.0〜10.0）を、IPC で送信する正規化値（0.0〜1.0）に変換する。
    /// </summary>
    internal static float NormalizeLevel(double level) => (float)Math.Clamp(level / 10.0, 0.0, 1.0);

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            if (started)
            {
                playbackService.ActualShareModeChanged -= OnActualShareModeChanged;
            }

            StopServerLocked();
        }
    }
}
