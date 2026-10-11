using NAudio.Wave;
using PlayMuse.Core.Services;

namespace PlayMuse.Tests.Services;

public class ChannelLevelMeterServiceTests
{
    private static byte[] BuildInt16Samples(int channels, params short[] frameValues)
    {
        // frameValues は1フレーム分（channels個）の16bit PCM値の並びを、フレーム数分繰り返したもの。
        var buffer = new byte[frameValues.Length * sizeof(short)];
        for (var i = 0; i < frameValues.Length; i++)
        {
            BitConverter.GetBytes(frameValues[i]).CopyTo(buffer, i * sizeof(short));
        }

        _ = channels;
        return buffer;
    }

    [Fact]
    public void GetLevels_MonoInput_DuplicatesLeftLevelToRight()
    {
        var service = new ChannelLevelMeterService();
        var format = new WaveFormat(44100, 16, 1);
        var buffer = BuildInt16Samples(1, short.MaxValue);

        service.PushSamples(buffer, format);
        var (left, right) = service.GetLevels();

        Assert.True(left.Level > 0);
        Assert.Equal(left.Level, right.Level);
        Assert.Equal(left.PeakLevel, right.PeakLevel);
    }

    [Fact]
    public void GetLevels_MoreThanTwoChannels_UsesOnlyFirstTwoChannels()
    {
        var service = new ChannelLevelMeterService();
        var format = new WaveFormat(44100, 16, 6);
        // ch0(L)=無音, ch1(R)=フルスケール, ch2..5=無音。先頭2chのみ参照されるため、
        // Lは無音(0)、Rは最大レベルになるはず。
        var buffer = BuildInt16Samples(6, 0, short.MaxValue, 0, 0, 0, 0);

        service.PushSamples(buffer, format);
        var (left, right) = service.GetLevels();

        Assert.Equal(0, left.Level);
        Assert.True(right.Level > 0);
    }

    [Fact]
    public void GetLevels_AfterSilenceTimeout_ResetsToZero()
    {
        var service = new ChannelLevelMeterService();
        var format = new WaveFormat(44100, 16, 2);
        var buffer = BuildInt16Samples(2, short.MaxValue, short.MaxValue);

        service.PushSamples(buffer, format);
        var (beforeTimeoutLeft, _) = service.GetLevels();
        Assert.True(beforeTimeoutLeft.Level > 0);

        // 無音タイムアウト(150ms)を超えるまで待機し、再生停止とみなされることを確認する。
        Thread.Sleep(250);

        // リリース係数による減衰は1回の呼び出しでは完了しないため、複数回呼び出して収束を待つ。
        ChannelLevel afterTimeoutLeft = default;
        ChannelLevel afterTimeoutRight = default;
        for (var i = 0; i < 50; i++)
        {
            (afterTimeoutLeft, afterTimeoutRight) = service.GetLevels();
        }

        Assert.Equal(0, afterTimeoutLeft.Level);
        Assert.Equal(0, afterTimeoutRight.Level);
    }

    [Fact]
    public void Reset_ClearsPeakHoldState()
    {
        var service = new ChannelLevelMeterService();
        var format = new WaveFormat(44100, 16, 2);
        var buffer = BuildInt16Samples(2, short.MaxValue, short.MaxValue);

        service.PushSamples(buffer, format);
        service.GetLevels();

        service.Reset();
        var (left, right) = service.GetLevels();

        Assert.Equal(0, left.Level);
        Assert.Equal(0, left.PeakLevel);
        Assert.Equal(0, right.Level);
        Assert.Equal(0, right.PeakLevel);
    }
}
