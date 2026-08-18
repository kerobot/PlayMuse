using NAudio.Wave;
using PlayMuse.Core.Services;

namespace PlayMuse.Tests.Services;

public class WavFormatNormalizerTests
{
    private static readonly Guid PcmSubFormatGuid = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid IeeeFloatSubFormatGuid = new("00000003-0000-0010-8000-00aa00389b71");

    [Fact]
    public void TryOpenReinterpreted_ExtensiblePcmWav_ReturnsReinterpretedWaveStream()
    {
        var path = CreateTempWavPath();
        try
        {
            CreateExtensibleWav(path, sampleRate: 44100, bitsPerSample: 24, channels: 2, subFormat: PcmSubFormatGuid);

            using var stream = WavFormatNormalizer.TryOpenReinterpreted(path);

            Assert.NotNull(stream);
            Assert.Equal(44100, stream!.WaveFormat.SampleRate);
            Assert.Equal(24, stream.WaveFormat.BitsPerSample);
            Assert.Equal(2, stream.WaveFormat.Channels);
            Assert.Equal(WaveFormatEncoding.Pcm, stream.WaveFormat.Encoding);
            // データの読み取りが可能であることを確認
            var buffer = new byte[stream.WaveFormat.BlockAlign];
            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            Assert.Equal(buffer.Length, bytesRead);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryOpenReinterpreted_ExtensibleFloatWav_ReturnsReinterpretedWaveStream()
    {
        var path = CreateTempWavPath();
        try
        {
            CreateExtensibleWav(path, sampleRate: 48000, bitsPerSample: 32, channels: 2, subFormat: IeeeFloatSubFormatGuid);

            using var stream = WavFormatNormalizer.TryOpenReinterpreted(path);

            Assert.NotNull(stream);
            Assert.Equal(48000, stream!.WaveFormat.SampleRate);
            Assert.Equal(32, stream.WaveFormat.BitsPerSample);
            Assert.Equal(2, stream.WaveFormat.Channels);
            Assert.Equal(WaveFormatEncoding.IeeeFloat, stream.WaveFormat.Encoding);
            // データの読み取りが可能であることを確認
            var buffer = new byte[stream.WaveFormat.BlockAlign];
            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            Assert.Equal(buffer.Length, bytesRead);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryOpenReinterpreted_StandardPcmWav_ReturnsNull()
    {
        var path = CreateTempWavPath();
        try
        {
            CreateStandardPcmWav(path, sampleRate: 44100, bitsPerSample: 16, channels: 2);

            using var stream = WavFormatNormalizer.TryOpenReinterpreted(path);

            // 標準 PCM WAV は再解釈が不要なので null を返す
            Assert.Null(stream);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryOpenReinterpreted_NonWavExtension_ReturnsNull()
    {
        using var stream = WavFormatNormalizer.TryOpenReinterpreted("song.mp3");

        // .wav 以外の拡張子では null を返す
        Assert.Null(stream);
    }

    [Fact]
    public void TryGetEffectiveBitsPerSample_24In32ExtensibleWav_ReturnsValidBitsPerSample()
    {
        var path = CreateTempWavPath();
        try
        {
            // 24bit有効データが32bitコンテナへ格納された24-in-32形式
            CreateExtensibleWav(path, sampleRate: 96000, bitsPerSample: 32, channels: 2, subFormat: PcmSubFormatGuid, validBitsPerSample: 24);

            var effectiveBits = WavFormatNormalizer.TryGetEffectiveBitsPerSample(path);

            Assert.Equal(24, effectiveBits);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryGetEffectiveBitsPerSample_StandardExtensibleWav_ReturnsContainerBitsPerSample()
    {
        var path = CreateTempWavPath();
        try
        {
            CreateExtensibleWav(path, sampleRate: 44100, bitsPerSample: 24, channels: 2, subFormat: PcmSubFormatGuid);

            var effectiveBits = WavFormatNormalizer.TryGetEffectiveBitsPerSample(path);

            Assert.Equal(24, effectiveBits);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryGetEffectiveBitsPerSample_StandardPcmWav_ReturnsNull()
    {
        var path = CreateTempWavPath();
        try
        {
            CreateStandardPcmWav(path, sampleRate: 44100, bitsPerSample: 16, channels: 2);

            var effectiveBits = WavFormatNormalizer.TryGetEffectiveBitsPerSample(path);

            // 標準PCM(非Extensible)WAVはExtensibleヘッダーを持たないためnullを返す
            Assert.Null(effectiveBits);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryGetEffectiveBitsPerSample_NonWavExtension_ReturnsNull()
    {
        var effectiveBits = WavFormatNormalizer.TryGetEffectiveBitsPerSample("song.mp3");

        Assert.Null(effectiveBits);
    }

    private static string CreateTempWavPath()
        => Path.Combine(Path.GetTempPath(), $"playmuse_test_{Guid.NewGuid():N}.wav");

    private static void CreateExtensibleWav(string path, int sampleRate, int bitsPerSample, int channels, Guid subFormat, int? validBitsPerSample = null)
    {
        var blockAlign = channels * (bitsPerSample / 8);
        var byteRate = sampleRate * blockAlign;
        var dataBytes = blockAlign * sampleRate;

        using var fs = new FileStream(path, FileMode.Create);
        using var bw = new BinaryWriter(fs);

        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        const int fmtChunkSize = 40;
        var riffSize = 4 + (8 + fmtChunkSize) + (8 + dataBytes);
        bw.Write(riffSize);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(fmtChunkSize);
        bw.Write(unchecked((short)0xFFFE)); // WAVE_FORMAT_EXTENSIBLE
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)blockAlign);
        bw.Write((short)bitsPerSample);
        bw.Write((short)22); // cbSize
        bw.Write((short)(validBitsPerSample ?? bitsPerSample)); // valid bits per sample
        bw.Write(0); // channel mask
        bw.Write(subFormat.ToByteArray());

        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(dataBytes);
        bw.Write(new byte[dataBytes]);
    }

    private static void CreateStandardPcmWav(string path, int sampleRate, int bitsPerSample, int channels)
    {
        var blockAlign = channels * (bitsPerSample / 8);
        var byteRate = sampleRate * blockAlign;
        var dataBytes = blockAlign * sampleRate;

        using var fs = new FileStream(path, FileMode.Create);
        using var bw = new BinaryWriter(fs);

        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        const int fmtChunkSize = 16;
        var riffSize = 4 + (8 + fmtChunkSize) + (8 + dataBytes);
        bw.Write(riffSize);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(fmtChunkSize);
        bw.Write((short)1); // WAVE_FORMAT_PCM
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)blockAlign);
        bw.Write((short)bitsPerSample);

        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(dataBytes);
        bw.Write(new byte[dataBytes]);
    }
}
