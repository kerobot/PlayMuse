using PlayMuse.Core.Models;
using PlayMuse.Core.Services;
using PlayMuse.Tests.Fakes;

namespace PlayMuse.Tests.Services;

public class MetadataServiceTests
{
    private static readonly Guid PcmSubFormatGuid = new("00000001-0000-0010-8000-00aa00389b71");

    [Fact]
    public async Task ApplyMetadataAsync_24In32ExtensibleWav_SetsEffectiveBitsPerSample()
    {
        var path = CreateTempWavPath();
        try
        {
            // 24bit有効データが32bitコンテナへ格納された24-in-32形式
            CreateExtensibleWav(path, sampleRate: 96000, bitsPerSample: 32, channels: 2, subFormat: PcmSubFormatGuid, validBitsPerSample: 24);

            var track = new Track(path);
            var service = new MetadataService(new FakeDispatcherService());

            await service.ApplyMetadataAsync(track);

            // コンテナ幅(32bit)ではなく有効ビット数(24bit)が反映されること
            Assert.Equal(24, track.BitsPerSample);
            // 可逆形式(WAV)であっても常にファイル形式名がラベルとして設定されること
            Assert.Equal("WAV", track.ContainerFormatLabel);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ApplyMetadataAsync_NonWavExtensionWithNoBitsPerSample_SetsContainerFormatLabel()
    {
        // TagLibが対応形式と認識できない/BitsPerSampleを持たないケースを、存在しないダミー拡張子で模擬する。
        // 実ファイル読み取りに失敗するため bitsPerSample は 0 のままとなり、拡張子由来のラベルが設定される想定。
        var path = Path.Combine(Path.GetTempPath(), $"playmuse_test_{Guid.NewGuid():N}.m4a");
        File.WriteAllBytes(path, new byte[16]); // TagLibが解析できない不正なM4Aダミーファイル

        try
        {
            var track = new Track(path);
            var service = new MetadataService(new FakeDispatcherService());

            await service.ApplyMetadataAsync(track);

            Assert.Equal(0, track.BitsPerSample);
            Assert.Equal("M4A", track.ContainerFormatLabel);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ApplyMetadataAsync_LossyExtensionEvenWithNonZeroBitsPerSample_ForcesFormatLabelInsteadOfBits()
    {
        // NAudio/TagLibがデコーダー内部値としてBitsPerSample>0を返しても、
        // 非可逆圧縮形式(MP3/AAC/M4A)は実際にはビット深度の概念を持たないため、
        // フォーマット名表示に統一されることを検証する。
        // ここではWAVコンテナのバイト列をMP3拡張子で保存し、TagLibが偶然BitsPerSampleを
        // 読み取れてしまうケースを模擬する代わりに、拡張子ベースの強制上書きを直接検証する。
        var path = CreateTempWavPath();
        var mp3Path = Path.ChangeExtension(path, ".mp3");
        try
        {
            CreateExtensibleWav(path, sampleRate: 44100, bitsPerSample: 16, channels: 2, subFormat: PcmSubFormatGuid);
            File.Move(path, mp3Path);

            var track = new Track(mp3Path);
            var service = new MetadataService(new FakeDispatcherService());

            await service.ApplyMetadataAsync(track);

            // 拡張子がMP3である以上、たとえ内部的にビット深度相当の値が取得できても0に強制し、
            // フォーマット名(MP3)を表示する。
            Assert.Equal(0, track.BitsPerSample);
            Assert.Equal("MP3", track.ContainerFormatLabel);
        }
        finally
        {
            File.Delete(mp3Path);
        }
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
}
