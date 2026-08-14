using NAudio.Wave;

namespace PlayMuse.Core.Services;

/// <summary>
/// リサンプル後・WasapiOut初期化前の位置に挟み込み、再生データを透過的にそのまま出力しつつ
/// <see cref="ISpectrumAnalyzerService"/> へキャプチャさせるパススルー <see cref="IWaveProvider"/>。
/// </summary>
internal sealed class SpectrumTapProvider(IWaveProvider source, ISpectrumAnalyzerService spectrumAnalyzer) : IWaveProvider
{
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(byte[] buffer, int offset, int count)
    {
        var bytesRead = source.Read(buffer, offset, count);

        if (bytesRead > 0)
        {
            spectrumAnalyzer.PushSamples(buffer.AsSpan(offset, bytesRead), WaveFormat);
        }

        return bytesRead;
    }
}
