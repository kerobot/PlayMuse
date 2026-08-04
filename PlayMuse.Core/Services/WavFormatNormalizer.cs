using System.Text;
using NAudio.Wave;

namespace PlayMuse.Core.Services;

/// <summary>
/// WAVE_FORMAT_EXTENSIBLE 形式の WAV ファイルを、NAudio の <see cref="AudioFileReader"/> が
/// 問題なく読み込める形式へ正規化するヘルパー。
/// </summary>
/// <remarks>
/// <see cref="AudioFileReader"/> は .wav 読み込み時、内部の <see cref="WaveFileReader"/> の
/// <see cref="WaveFormat.Encoding"/> が <see cref="WaveFormatEncoding.Pcm"/> /
/// <see cref="WaveFormatEncoding.IeeeFloat"/> 以外（<see cref="WaveFormatEncoding.Extensible"/> を含む）の場合、
/// Audio Compression Manager (ACM) 経由での変換を試みる。24bit/32bit float 等の高解像度 WAV でよく使われる
/// WAVE_FORMAT_EXTENSIBLE タグはこれに該当し、環境によっては ACM ドライバが存在せず
/// <c>NAudio.MmException: NoDriver calling acmFormatSuggest</c> が発生して再生できない。
/// SubFormat が PCM または IEEE Float であれば音声データのバイト列自体は通常の PCM/Float と同一のため、
/// fmt チャンクを非 Extensible 形式に書き換えた一時ファイルを生成することで ACM 経路を回避できる。
/// </remarks>
public static class WavFormatNormalizer
{
    /// <summary>
    /// PCM フォーマットの SubFormat GUID: {00000001-0000-0010-8000-00aa00389b71}
    /// </summary>
    private static readonly Guid PcmSubFormatGuid = new("00000001-0000-0010-8000-00aa00389b71");

    /// <summary>
    /// IEEE Float フォーマットの SubFormat GUID: {00000003-0000-0010-8000-00aa00389b71}
    /// </summary>
    private static readonly Guid IeeeFloatSubFormatGuid = new("00000003-0000-0010-8000-00aa00389b71");

    /// <summary>
    /// 指定ファイルが WAVE_FORMAT_EXTENSIBLE（SubFormat が PCM/IEEE Float）の WAV であれば、
    /// 非 Extensible 形式に正規化した一時ファイルを生成してそのパスを返す。
    /// 正規化が不要（対象外の拡張子、または既に Pcm/IeeeFloat）な場合は null を返す。
    /// </summary>
    public static string? NormalizeIfNeeded(string filePath)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        WaveFormat sourceFormat;
        try
        {
            using var probe = new WaveFileReader(filePath);
            sourceFormat = probe.WaveFormat;
        }
        catch
        {
            // ヘッダ解析に失敗した場合は AudioFileReader 側に処理を委ねる。
            return null;
        }

        if (sourceFormat.Encoding != WaveFormatEncoding.Extensible)
        {
            return null;
        }

        if (!TryGetSubFormat(sourceFormat, out var subFormat))
        {
            return null;
        }

        WaveFormatEncoding targetEncoding;
        if (subFormat == PcmSubFormatGuid)
        {
            targetEncoding = WaveFormatEncoding.Pcm;
        }
        else if (subFormat == IeeeFloatSubFormatGuid)
        {
            targetEncoding = WaveFormatEncoding.IeeeFloat;
        }
        else
        {
            // PCM/Float 以外の SubFormat はバイト列の互換性が保証できないため対象外とする。
            return null;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"playmuse_{Guid.NewGuid():N}.wav");
        using (var source = new WaveFileReader(filePath))
        using (var destination = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
        {
            WriteNormalizedWav(destination, source, targetEncoding);
        }

        return tempPath;
    }

    /// <summary>
    /// WaveFormatEncoding.Extensible なフォーマットから SubFormat GUID を取得する。
    /// <see cref="WaveFileReader"/> はヘッダ解析結果として <see cref="WaveFormatExtensible"/> ではなく
    /// <see cref="WaveFormatExtraData"/>（cbSize 以降の生バイト列を保持する型）を返すため、
    /// その ExtraData から SubFormat GUID（オフセット6, 16byte: validBitsPerSample(2)+channelMask(4)の直後）を読み取る。
    /// </summary>
    private static bool TryGetSubFormat(WaveFormat format, out Guid subFormat)
    {
        switch (format)
        {
            case WaveFormatExtensible extensible:
                subFormat = extensible.SubFormat;
                return true;

            case WaveFormatExtraData extraData when extraData.ExtraSize >= 22:
                subFormat = new Guid(extraData.ExtraData.AsSpan(6, 16));
                return true;

            default:
                subFormat = default;
                return false;
        }
    }

    private static void WriteNormalizedWav(Stream destination, WaveFileReader source, WaveFormatEncoding targetEncoding)
    {
        var format = source.WaveFormat;
        var dataLength = (int)source.Length;

        // fmt チャンク: PCM/IEEE Float は 16byte（IEEE Float 慣例として cbSize=0 の 18byte でも可だが、
        // 最も互換性の高い 16byte 固定長ヘッダで出力する）。
        const int fmtChunkSize = 16;
        var riffSize = 4 + (8 + fmtChunkSize) + (8 + dataLength);

        using var writer = new BinaryWriter(destination, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(riffSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(fmtChunkSize);
        writer.Write((short)(targetEncoding == WaveFormatEncoding.IeeeFloat ? 3 : 1));
        writer.Write((short)format.Channels);
        writer.Write(format.SampleRate);
        writer.Write(format.AverageBytesPerSecond);
        writer.Write((short)format.BlockAlign);
        writer.Write((short)format.BitsPerSample);

        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);

        writer.Flush();

        source.Position = 0;
        CopyInBlockAlignedChunks(source, destination, format.BlockAlign);
    }

    /// <summary>
    /// <see cref="WaveFileReader.Read"/> はブロック境界(BlockAlign)単位でしか読み取れないため、
    /// 既定の <see cref="Stream.CopyTo(Stream)"/> の固定バッファサイズ（BlockAlignの倍数とは限らない）では
    /// 例外が発生することがある。BlockAlign の倍数に切り詰めたバッファサイズで手動コピーする。
    /// </summary>
    private static void CopyInBlockAlignedChunks(WaveFileReader source, Stream destination, int blockAlign)
    {
        const int preferredBufferSize = 81920;
        var bufferSize = Math.Max(blockAlign, preferredBufferSize / blockAlign * blockAlign);
        var buffer = new byte[bufferSize];

        int bytesRead;
        while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            destination.Write(buffer, 0, bytesRead);
        }
    }
}
