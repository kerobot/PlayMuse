using NAudio.Wave;

namespace PlayMuse.Core.Services;

/// <summary>
/// 再生波形をキャプチャし、L/Rチャンネル別の縦型音量レベルメーター用レベル（0〜10、ピークホールド込み）を
/// 算出するサービス。スペクトラムアナライザーより高速に追従させるため、FFTを用いずピーク検出のみで動作する。
/// </summary>
public sealed class ChannelLevelMeterService : IChannelLevelMeterService
{
    private const double MinDecibel = -60.0;
    private const double MaxDecibel = 0.0;
    private const int LevelSteps = 10;

    /// <summary>レベルメーター用のアタック係数。スペクトラムより高速に追従させるため大きめの値にする。</summary>
    private const double LevelAttackFactor = 0.9;

    /// <summary>レベルメーター用のリリース係数。小さいほどゆっくり減衰し、点滅しにくくなる。</summary>
    private const double LevelReleaseFactor = 0.2;

    /// <summary>ピークホールドが1回の更新あたりに減衰する段数。</summary>
    private const double PeakDecayPerUpdate = 0.1;

    /// <summary>平滑化・ピークホールドの値が指数的減衰のため厳密な0に到達しないので、この値未満で0へスナップする。</summary>
    private const double LevelSnapToZeroThreshold = 0.05;

    /// <summary>平滑化・ピークホールドの値が指数的漸近のため厳密な最大値(LevelSteps)に到達しないので、
    /// この値未満の差であれば最大値へスナップする。これにより音量最大時に最上段が確実に点灯する。</summary>
    private const double LevelSnapToMaxThreshold = 0.05;

    /// <summary>再生停止とみなすまでの、最終サンプル受信からの猶予時間。</summary>
    private static readonly TimeSpan SilenceTimeout = TimeSpan.FromMilliseconds(150);

    private const long NoSampleReceivedTicks = -1;

    private const double FullScale8Bit = 128.0;
    private const double FullScale16Bit = 32768.0;
    private const double FullScale24Bit = 8388608.0;
    private const double FullScale32Bit = 2147483648.0;
    private const int Offset8Bit = 128;
    private const int SignBit24Bit = 0x800000;
    private const int SignExtensionMask24Bit = unchecked((int)0xFF000000);
    private const double DecibelScale = 20.0;

    private readonly Lock sync = new();

    private double leftPeakAmplitude;
    private double rightPeakAmplitude;
    private double leftSmoothedLevel;
    private double rightSmoothedLevel;
    private double leftPeakHoldLevel;
    private double rightPeakHoldLevel;
    private long lastSampleTicks = NoSampleReceivedTicks;

    /// <summary>
    /// 再生波形のバイト列を受け取り、L/Rチャンネル（先頭2ch）のフレーム内最大振幅を求めて保持する。
    /// モノラル(1ch)の場合はL値をRにも複製する。
    /// </summary>
    public void PushSamples(ReadOnlySpan<byte> buffer, WaveFormat format)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        var channels = Math.Max(1, format.Channels);
        var bytesPerSample = format.BlockAlign > 0 && channels > 0
            ? format.BlockAlign / channels
            : format.BitsPerSample / 8;
        if (bytesPerSample <= 0)
        {
            return;
        }

        var frameSize = bytesPerSample * channels;
        if (frameSize <= 0)
        {
            return;
        }

        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat;
        var containerBits = bytesPerSample * 8;
        var frameCount = buffer.Length / frameSize;
        if (frameCount <= 0)
        {
            return;
        }

        var leftMax = 0.0;
        var rightMax = 0.0;

        for (var frame = 0; frame < frameCount; frame++)
        {
            var frameOffset = frame * frameSize;

            var leftOffset = frameOffset;
            var leftSample = Math.Abs(ReadSample(buffer.Slice(leftOffset, bytesPerSample), isFloat, containerBits));
            if (leftSample > leftMax)
            {
                leftMax = leftSample;
            }

            // モノラルはLをRへ複製、2chを超えるフォーマットはch1(R)のみを参照し、3ch目以降は無視する。
            var rightChannelIndex = channels > 1 ? 1 : 0;
            var rightOffset = frameOffset + rightChannelIndex * bytesPerSample;
            var rightSample = Math.Abs(ReadSample(buffer.Slice(rightOffset, bytesPerSample), isFloat, containerBits));
            if (rightSample > rightMax)
            {
                rightMax = rightSample;
            }
        }

        lock (sync)
        {
            if (leftMax > leftPeakAmplitude)
            {
                leftPeakAmplitude = leftMax;
            }

            if (rightMax > rightPeakAmplitude)
            {
                rightPeakAmplitude = rightMax;
            }

            lastSampleTicks = Environment.TickCount64;
        }
    }

    /// <summary>
    /// 直近のピーク振幅から表示レベル（0〜10、ピークホールド込み）を算出する。UI側のタイマーから定期的に呼び出される想定。
    /// </summary>
    public (ChannelLevel Left, ChannelLevel Right) GetLevels()
    {
        double leftAmplitude;
        double rightAmplitude;
        bool isSilentTimeout;

        lock (sync)
        {
            isSilentTimeout = lastSampleTicks == NoSampleReceivedTicks
                || Environment.TickCount64 - lastSampleTicks > SilenceTimeout.TotalMilliseconds;

            leftAmplitude = isSilentTimeout ? 0.0 : leftPeakAmplitude;
            rightAmplitude = isSilentTimeout ? 0.0 : rightPeakAmplitude;

            // 取得済みのピーク振幅は毎回クリアし、次回GetLevels呼び出しまでの区間のピークを新たに蓄積する。
            leftPeakAmplitude = 0.0;
            rightPeakAmplitude = 0.0;
        }

        var leftTarget = AmplitudeToLevel(leftAmplitude);
        var rightTarget = AmplitudeToLevel(rightAmplitude);

        var left = BuildLevel(leftTarget, ref leftSmoothedLevel, ref leftPeakHoldLevel);
        var right = BuildLevel(rightTarget, ref rightSmoothedLevel, ref rightPeakHoldLevel);

        return (left, right);
    }

    /// <summary>
    /// 保持している振幅・平滑化レベル・ピークホールド状態をすべて初期化する。再生停止時やフォーマット変更時に呼ぶ。
    /// </summary>
    public void Reset()
    {
        lock (sync)
        {
            leftPeakAmplitude = 0.0;
            rightPeakAmplitude = 0.0;
            lastSampleTicks = NoSampleReceivedTicks;
        }

        leftSmoothedLevel = 0.0;
        rightSmoothedLevel = 0.0;
        leftPeakHoldLevel = 0.0;
        rightPeakHoldLevel = 0.0;
    }

    /// <summary>振幅の絶対値(0.0〜1.0)をデシベル経由で0〜<see cref="LevelSteps"/>の目標レベルへ変換する。</summary>
    private static double AmplitudeToLevel(double amplitude)
    {
        var decibel = amplitude <= 0 ? MinDecibel : DecibelScale * Math.Log10(amplitude);
        var clamped = Math.Clamp(decibel, MinDecibel, MaxDecibel);
        var normalized = (clamped - MinDecibel) / (MaxDecibel - MinDecibel);
        return normalized * LevelSteps;
    }

    private static ChannelLevel BuildLevel(double target, ref double smoothedLevel, ref double peakHoldLevel)
    {
        var factor = target > smoothedLevel ? LevelAttackFactor : LevelReleaseFactor;
        smoothedLevel += (target - smoothedLevel) * factor;
        if (smoothedLevel < LevelSnapToZeroThreshold)
        {
            smoothedLevel = 0.0;
        }
        else if (LevelSteps - smoothedLevel < LevelSnapToMaxThreshold)
        {
            smoothedLevel = LevelSteps;
        }

        if (smoothedLevel >= peakHoldLevel)
        {
            peakHoldLevel = smoothedLevel;
        }
        else
        {
            peakHoldLevel = Math.Max(smoothedLevel, peakHoldLevel - PeakDecayPerUpdate);
            if (peakHoldLevel < LevelSnapToZeroThreshold)
            {
                peakHoldLevel = 0.0;
            }
        }

        return new ChannelLevel(smoothedLevel, peakHoldLevel);
    }

    private static double ReadSample(ReadOnlySpan<byte> sampleBytes, bool isFloat, int bitsPerSample)
    {
        if (isFloat && bitsPerSample == 32)
        {
            return BitConverter.ToSingle(sampleBytes);
        }

        return bitsPerSample switch
        {
            16 => BitConverter.ToInt16(sampleBytes) / FullScale16Bit,
            24 => Read24BitSample(sampleBytes) / FullScale24Bit,
            32 => BitConverter.ToInt32(sampleBytes) / FullScale32Bit,
            8 => (sampleBytes[0] - Offset8Bit) / FullScale8Bit,
            _ => 0.0,
        };
    }

    private static int Read24BitSample(ReadOnlySpan<byte> sampleBytes)
    {
        var value = sampleBytes[0] | (sampleBytes[1] << 8) | (sampleBytes[2] << 16);
        if ((value & SignBit24Bit) != 0)
        {
            value |= SignExtensionMask24Bit;
        }

        return value;
    }
}
