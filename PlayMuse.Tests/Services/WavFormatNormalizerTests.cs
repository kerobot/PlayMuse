using NAudio.Wave;
using PlayMuse.Core.Services;

namespace PlayMuse.Tests.Services;

public class WavFormatNormalizerTests
{
    private static readonly Guid PcmSubFormatGuid = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid IeeeFloatSubFormatGuid = new("00000003-0000-0010-8000-00aa00389b71");

    [Fact]
    public void NormalizeIfNeeded_ExtensiblePcmWav_CreatesPlayableTempFile()
    {
        var path = CreateTempWavPath();
        try
        {
            CreateExtensibleWav(path, sampleRate: 44100, bitsPerSample: 24, channels: 2, subFormat: PcmSubFormatGuid);

            var normalizedPath = WavFormatNormalizer.NormalizeIfNeeded(path);

            Assert.NotNull(normalizedPath);
            try
            {
                using var reader = new AudioFileReader(normalizedPath!);
                Assert.Equal(44100, reader.WaveFormat.SampleRate);
            }
            finally
            {
                File.Delete(normalizedPath!);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NormalizeIfNeeded_ExtensibleFloatWav_CreatesPlayableTempFile()
    {
        var path = CreateTempWavPath();
        try
        {
            CreateExtensibleWav(path, sampleRate: 48000, bitsPerSample: 32, channels: 2, subFormat: IeeeFloatSubFormatGuid);

            var normalizedPath = WavFormatNormalizer.NormalizeIfNeeded(path);

            Assert.NotNull(normalizedPath);
            try
            {
                using var reader = new AudioFileReader(normalizedPath!);
                Assert.Equal(48000, reader.WaveFormat.SampleRate);
            }
            finally
            {
                File.Delete(normalizedPath!);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NormalizeIfNeeded_StandardPcmWav_ReturnsNull()
    {
        var path = CreateTempWavPath();
        try
        {
            CreateStandardPcmWav(path, sampleRate: 44100, bitsPerSample: 16, channels: 2);

            var normalizedPath = WavFormatNormalizer.NormalizeIfNeeded(path);

            Assert.Null(normalizedPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NormalizeIfNeeded_NonWavExtension_ReturnsNull()
    {
        var normalizedPath = WavFormatNormalizer.NormalizeIfNeeded("song.mp3");

        Assert.Null(normalizedPath);
    }

    private static string CreateTempWavPath()
        => Path.Combine(Path.GetTempPath(), $"playmuse_test_{Guid.NewGuid():N}.wav");

    private static void CreateExtensibleWav(string path, int sampleRate, int bitsPerSample, int channels, Guid subFormat)
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
        bw.Write((short)bitsPerSample); // valid bits per sample
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
