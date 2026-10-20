using NAudio.Wave;

namespace PlayMuse.Core.Services;

/// <summary>
/// リサンプル後・WasapiPlayer初期化前の位置に挟み込み、再生データを透過的にそのまま出力しつつ
/// <see cref="ISpectrumAnalyzerService"/> へキャプチャさせるパススルー <see cref="IWaveProvider"/>。
/// </summary>
internal sealed class SpectrumTapProvider(IWaveProvider source, ISpectrumAnalyzerService spectrumAnalyzer, IChannelLevelMeterService channelLevelMeter) : IWaveProvider
{
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(Span<byte> buffer)
    {
        var bytesRead = source.Read(buffer);

        if (bytesRead > 0)
        {
            spectrumAnalyzer.PushSamples(buffer[..bytesRead], WaveFormat);
            channelLevelMeter.PushSamples(buffer[..bytesRead], WaveFormat);
        }

        return bytesRead;
    }
}
