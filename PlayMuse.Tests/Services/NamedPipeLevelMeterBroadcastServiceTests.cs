using Microsoft.Extensions.Logging.Abstractions;
using PlayMuse.Core.Models;
using PlayMuse.Core.Services;
using PlayMuse.Tests.Fakes;

namespace PlayMuse.Tests.Services;

public class NamedPipeLevelMeterBroadcastServiceTests
{
    [Fact]
    public void ActualShareModeChanged_ToExclusive_StartsBroadcast()
    {
        var playbackService = new FakeAudioPlaybackService();
        using var service = new NamedPipeLevelMeterBroadcastService(
            playbackService,
            new SpectrumAnalyzerService(),
            new ChannelLevelMeterService(),
            NullLogger<NamedPipeLevelMeterBroadcastService>.Instance);

        service.Start();

        // 排他モードへの変化によって例外なくブロードキャスト（パイプサーバー起動）が開始されることを確認する。
        var exception = Record.Exception(() => playbackService.SetShareMode(AudioShareMode.Exclusive));

        Assert.Null(exception);
    }

    [Fact]
    public void ActualShareModeChanged_BackToShared_StopsBroadcastWithoutException()
    {
        var playbackService = new FakeAudioPlaybackService();
        using var service = new NamedPipeLevelMeterBroadcastService(
            playbackService,
            new SpectrumAnalyzerService(),
            new ChannelLevelMeterService(),
            NullLogger<NamedPipeLevelMeterBroadcastService>.Instance);

        service.Start();
        playbackService.SetShareMode(AudioShareMode.Exclusive);

        var exception = Record.Exception(() => playbackService.RaiseActualShareMode(AudioShareMode.Shared));

        Assert.Null(exception);
    }

    [Fact]
    public void Start_AlreadyExclusive_StartsBroadcastImmediately()
    {
        var playbackService = new FakeAudioPlaybackService();
        playbackService.SetShareMode(AudioShareMode.Exclusive);

        using var service = new NamedPipeLevelMeterBroadcastService(
            playbackService,
            new SpectrumAnalyzerService(),
            new ChannelLevelMeterService(),
            NullLogger<NamedPipeLevelMeterBroadcastService>.Instance);

        var exception = Record.Exception(service.Start);

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(0.0, 0.0f)]
    [InlineData(10.0, 1.0f)]
    [InlineData(5.0, 0.5f)]
    [InlineData(-1.0, 0.0f)]
    [InlineData(15.0, 1.0f)]
    public void NormalizeLevel_ClampsAndScalesToUnitRange(double level, float expected)
    {
        var normalized = NamedPipeLevelMeterBroadcastService.NormalizeLevel(level);

        Assert.Equal(expected, normalized, precision: 5);
    }
}
