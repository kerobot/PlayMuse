using System.Runtime.InteropServices;
using NAudio.Wave;

namespace PlayMuse.Core.Services;

/// <summary>
/// 音量が最大(1.0)のときは一切サンプルを加工せずそのまま通過させる（ビットパーフェクト維持）。
/// 音量を下げた場合のみ、元の <see cref="WaveFormat"/>（ビット深度・エンコーディング）を変えずに
/// サンプル値をスケーリングする軽量な <see cref="IWaveProvider"/> ラッパー。
/// </summary>
internal sealed class PcmVolumeProvider(IWaveProvider source) : IWaveProvider
{
    /// <summary>
    /// この値以上の音量は「実質最大音量」とみなし、サンプル加工を完全にスキップする。
    /// </summary>
    private const float BitPerfectThreshold = 0.999f;

    private static readonly Guid IeeeFloatSubFormatGuid = new("00000003-0000-0010-8000-00aa00389b71");

    public WaveFormat WaveFormat => source.WaveFormat;

    /// <summary>0.0〜1.0 の音量。既定値は 1.0（ビットパーフェクト）。</summary>
    public float Volume { get; set; } = 1.0f;

    public int Read(byte[] buffer, int offset, int count)
    {
        var bytesRead = source.Read(buffer, offset, count);

        if (bytesRead <= 0 || Volume >= BitPerfectThreshold)
        {
            // 最大音量時はデコード結果をそのまま出力し、ビットパーフェクトを保つ。
            return bytesRead;
        }

        ApplyVolume(buffer, offset, bytesRead);
        return bytesRead;
    }

    private void ApplyVolume(byte[] buffer, int offset, int count)
    {
        var format = WaveFormat;
        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
            (format is WaveFormatExtensible extensible && extensible.SubFormat == IeeeFloatSubFormatGuid);

        if (isFloat)
        {
            ScaleFloat(buffer, offset, count);
            return;
        }

        switch (format.BitsPerSample)
        {
            case 8:
                ScaleUInt8(buffer, offset, count);
                break;
            case 16:
                ScaleInt16(buffer, offset, count);
                break;
            case 24:
                ScaleInt24(buffer, offset, count);
                break;
            case 32:
                ScaleInt32(buffer, offset, count);
                break;
        }
    }

    private void ScaleFloat(byte[] buffer, int offset, int count)
    {
        var span = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(offset, count - (count % 4)));
        for (var i = 0; i < span.Length; i++)
        {
            span[i] *= Volume;
        }
    }

    private void ScaleInt16(byte[] buffer, int offset, int count)
    {
        var span = MemoryMarshal.Cast<byte, short>(buffer.AsSpan(offset, count - (count % 2)));
        for (var i = 0; i < span.Length; i++)
        {
            span[i] = (short)Math.Clamp(span[i] * (double)Volume, short.MinValue, short.MaxValue);
        }
    }

    private void ScaleInt32(byte[] buffer, int offset, int count)
    {
        var span = MemoryMarshal.Cast<byte, int>(buffer.AsSpan(offset, count - (count % 4)));
        for (var i = 0; i < span.Length; i++)
        {
            span[i] = (int)Math.Clamp(span[i] * (double)Volume, int.MinValue, int.MaxValue);
        }
    }

    private void ScaleUInt8(byte[] buffer, int offset, int count)
    {
        for (var i = offset; i < offset + count; i++)
        {
            var centered = buffer[i] - 128;
            buffer[i] = (byte)Math.Clamp((centered * Volume) + 128, 0, 255);
        }
    }

    private void ScaleInt24(byte[] buffer, int offset, int count)
    {
        var end = offset + count - (count % 3);
        for (var i = offset; i < end; i += 3)
        {
            var sample = buffer[i] | (buffer[i + 1] << 8) | (buffer[i + 2] << 16);
            if ((sample & 0x800000) != 0)
            {
                sample |= unchecked((int)0xFF000000); // 符号拡張
            }

            var scaled = (int)Math.Clamp(sample * (double)Volume, -8388608, 8388607);
            buffer[i] = (byte)(scaled & 0xFF);
            buffer[i + 1] = (byte)((scaled >> 8) & 0xFF);
            buffer[i + 2] = (byte)((scaled >> 16) & 0xFF);
        }
    }
}
