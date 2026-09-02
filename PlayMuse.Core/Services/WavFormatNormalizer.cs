using NAudio.Wave;
using System.Reflection;

namespace PlayMuse.Core.Services;

/// <summary>
/// WAVE_FORMAT_EXTENSIBLE 形式の WAV ファイル(SubFormat が PCM または IEEE Float)を、
/// ACM 経由の変換を回避しつつ読み込むためのヘルパー。
/// </summary>
/// <remarks>
/// <para>
/// NAudio の <see cref="WaveFileReader"/> が返す <see cref="WaveFormat"/> が
/// <see cref="WaveFormatEncoding.Extensible"/> の場合、一部の処理経路で ACM を介した
/// 変換が試行され、環境によっては ACM ドライバが存在せず
/// <c>NAudio.MmException: NoDriver calling acmFormatSuggest</c> 例外が発生する。
/// </para>
/// <para>
/// WAVE_FORMAT_EXTENSIBLE の SubFormat が PCM/IEEE Float である場合、
/// 音声データのバイト列自体は通常の PCM/Float と完全に同一である。
/// そのため、<see cref="WaveFormat"/> プロパティのみを非 Extensible 相当
/// (<see cref="WaveFormatEncoding.Pcm"/> / <see cref="WaveFormatEncoding.IeeeFloat"/>)
/// に読み替える軽量なラッパー <see cref="WaveStream"/> を返すことで、
/// ファイル I/O を発生させずに ACM 経路を回避できる。
/// </para>
/// </remarks>
public static class WavFormatNormalizer
{
    /// <summary>
    /// <see cref="WaveFormatExtensible"/> の非公開privateフィールド wValidBitsPerSample。
    /// NAudio 2.3.0/2.4.0のいずれもこのフィールドへの公開プロパティを提供していないため、リフレクションで取得（NAudio 2.4.0でも公開APIなし）
    /// </summary>
    private static readonly FieldInfo? WaveFormatExtensibleValidBitsField =
        typeof(WaveFormatExtensible).GetField("wValidBitsPerSample", BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>
    /// PCM フォーマットの SubFormat GUID: {00000001-0000-0010-8000-00aa00389b71}
    /// </summary>
    private static readonly Guid PcmSubFormatGuid = new("00000001-0000-0010-8000-00aa00389b71");

    /// <summary>
    /// IEEE Float フォーマットの SubFormat GUID: {00000003-0000-0010-8000-00aa00389b71}
    /// </summary>
    private static readonly Guid IeeeFloatSubFormatGuid = new("00000003-0000-0010-8000-00aa00389b71");

    /// <summary>
    /// 指定されたWAVファイルがWAVE_FORMAT_EXTENSIBLEの場合、wValidBitsPerSampleから有効ビット数を取得する。
    /// 表示専用。デコード用のWaveFormatには影響を与えない。
    /// </summary>
    /// <param name="filePath">WAVファイルのパス。</param>
    /// <returns>
    /// 有効ビット数が取得できた場合はその値。
    /// 取得できない場合(非WAV、非Extensible、ExtraDataなし、無効値等)はnull。
    /// </returns>
    public static int? TryGetEffectiveBitsPerSample(string filePath)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        WaveFileReader? reader = null;
        try
        {
            reader = new WaveFileReader(filePath);
            var format = reader.WaveFormat;

            if (format.Encoding != WaveFormatEncoding.Extensible)
            {
                return null;
            }

            // WaveFormatExtensible の ExtraData 構造:
            // offset 0-1: wValidBitsPerSample (2 bytes)
            // offset 2-5: dwChannelMask (4 bytes)
            // offset 6-21: SubFormat GUID (16 bytes)
            if (!TryGetExtensibleExtraData(format, out var validBitsPerSample, out _))
            {
                return null;
            }

            // validBitsPerSample が 0 の場合はコンテナ幅と同じを意味する慣習があるため null とする
            // validBitsPerSample がコンテナ幅を超える場合も無効とする
            if (validBitsPerSample > 0 && validBitsPerSample <= format.BitsPerSample)
            {
                return validBitsPerSample;
            }

            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            reader?.Dispose();
        }
    }

    /// <summary>
    /// 指定ファイルが WAVE_FORMAT_EXTENSIBLE(SubFormat が PCM/IEEE Float)の WAV であれば、
    /// <see cref="WaveFormat"/> を非 Extensible 形式に再解釈した <see cref="WaveStream"/> を返す。
    /// 再解釈が不要(対象外の拡張子、または既に Pcm/IeeeFloat、非対応 SubFormat)な場合は null を返す。
    /// </summary>
    /// <param name="filePath">読み込む WAV ファイルのパス。</param>
    /// <returns>
    /// 再解釈が必要な場合は再解釈済み <see cref="WaveStream"/>(呼び出し側が Dispose 責務を持つ)。
    /// 不要または非対応の場合は null(呼び出し側は通常通り <see cref="WaveFileReader"/> を開く)。
    /// </returns>
    public static WaveStream? TryOpenReinterpreted(string filePath)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        WaveFileReader reader;
        try
        {
            reader = new WaveFileReader(filePath);
        }
        catch
        {
            // ヘッダ解析に失敗した場合は null を返し、後続処理(通常の WaveFileReader オープン)に委ねる。
            return null;
        }

        var sourceFormat = reader.WaveFormat;

        if (sourceFormat.Encoding != WaveFormatEncoding.Extensible)
        {
            // Extensible ではない場合は再解釈不要なので reader を破棄して null を返す。
            reader.Dispose();
            return null;
        }

        if (!TryGetExtensibleExtraData(sourceFormat, out _, out var subFormat))
        {
            reader.Dispose();
            return null;
        }

        WaveFormat reinterpretedFormat;
        if (subFormat == PcmSubFormatGuid)
        {
            // SubFormat が PCM の場合、非 Extensible の PCM WaveFormat に再解釈する。
            reinterpretedFormat = new WaveFormat(
                sourceFormat.SampleRate,
                sourceFormat.BitsPerSample,
                sourceFormat.Channels);
        }
        else if (subFormat == IeeeFloatSubFormatGuid)
        {
            // SubFormat が IEEE Float の場合、非 Extensible の IEEE Float WaveFormat に再解釈する。
            reinterpretedFormat = WaveFormat.CreateIeeeFloatWaveFormat(
                sourceFormat.SampleRate,
                sourceFormat.Channels);
        }
        else
        {
            // PCM/Float 以外の SubFormat はバイト列の互換性が保証できないため対象外とする。
            reader.Dispose();
            return null;
        }

        // 開いた WaveFileReader を、再解釈済み WaveFormat でラップして返す。
        return new ReinterpretedWaveStream(reader, reinterpretedFormat);
    }

    /// <summary>
    /// WaveFormatEncoding.Extensible なフォーマットから wValidBitsPerSample と SubFormat GUID を取得する。
    /// <see cref="WaveFileReader"/> はヘッダ解析結果として <see cref="WaveFormatExtensible"/> ではなく
    /// <see cref="WaveFormatExtraData"/>(cbSize 以降の生バイト列を保持する型)を返すため、
    /// その ExtraData から wValidBitsPerSample(オフセット0, 2byte) と SubFormat GUID(オフセット6, 16byte: validBitsPerSample(2)+channelMask(4)の直後)を読み取る。
    /// </summary>
    private static bool TryGetExtensibleExtraData(WaveFormat format, out int validBitsPerSample, out Guid subFormat)
    {
        switch (format)
        {
            case WaveFormatExtensible extensible:
                // wValidBitsPerSample はprivateフィールドのため、リフレクションで取得（NAudio 2.4.0でも公開APIなし）
                if (WaveFormatExtensibleValidBitsField != null)
                {
                    var validBits = WaveFormatExtensibleValidBitsField.GetValue(extensible);
                    validBitsPerSample = validBits is short s ? s : 0;
                }
                else
                {
                    validBitsPerSample = 0;
                }
                subFormat = extensible.SubFormat;
                return true;

            case WaveFormatExtraData extraData when extraData.ExtraSize >= 22:
                // ExtraData 構造:
                // offset 0-1: wValidBitsPerSample (2 bytes, little-endian)
                // offset 2-5: dwChannelMask (4 bytes)
                // offset 6-21: SubFormat GUID (16 bytes)
                validBitsPerSample = BitConverter.ToInt16(extraData.ExtraData.AsSpan(0, 2));
                subFormat = new Guid(extraData.ExtraData.AsSpan(6, 16));
                return true;

            default:
                validBitsPerSample = 0;
                subFormat = default;
                return false;
        }
    }

    /// <summary>
    /// WAVE_FORMAT_EXTENSIBLE の <see cref="WaveFileReader"/> を、
    /// <see cref="WaveFormat"/> のみを非 Extensible 形式に読み替えたラッパー <see cref="WaveStream"/>。
    /// データのバイト列自体はそのまま inner reader から読み取るため、ファイル I/O は発生しない。
    /// </summary>
    private sealed class ReinterpretedWaveStream(WaveFileReader innerReader, WaveFormat reinterpretedFormat) : WaveStream
    {
        public override WaveFormat WaveFormat => reinterpretedFormat;

        public override long Length => innerReader.Length;

        public override long Position
        {
            get => innerReader.Position;
            set => innerReader.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
            => innerReader.Read(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                innerReader.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
