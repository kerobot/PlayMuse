using System.Buffers.Binary;
using System.Text;

namespace PlayMuse.LevelMeterIpc;

/// <summary>
/// パイプ上で送受信されるメッセージ種別を表す先頭1バイトの値。
/// </summary>
internal enum LevelMeterMessageType : byte
{
    DeviceInfo = 0,
    Frame = 1,
}

/// <summary>
/// <see cref="ILevelMeterMessage"/> をバイナリ形式でストリームへ書き込むヘルパー。
/// メッセージ全体（種別バイト＋ペイロード）を1回の <see cref="Stream.WriteAsync(ReadOnlyMemory{byte}, CancellationToken)"/> で
/// 書き込むことで、複数スレッドからの書き込みが混在しても1メッセージ単位でのアトミック性を確保する。
/// </summary>
public static class LevelMeterMessageWriter
{
    /// <summary>
    /// <see cref="LevelMeterDeviceInfoMessage"/> をストリームへ書き込む。
    /// </summary>
    public static Task WriteDeviceInfoAsync(Stream stream, LevelMeterDeviceInfoMessage message, CancellationToken cancellationToken = default)
    {
        var nameBytes = message.DeviceName is null ? null : Encoding.UTF8.GetBytes(message.DeviceName);
        var payloadLength = 1 + (nameBytes is null ? 0 : 4 + nameBytes.Length) + 4 + 4 + 1;
        var buffer = new byte[1 + 4 + payloadLength];
        var span = buffer.AsSpan();

        span[0] = (byte)LevelMeterMessageType.DeviceInfo;
        BinaryPrimitives.WriteInt32LittleEndian(span[1..], payloadLength);

        var offset = 5;
        span[offset] = (byte)(nameBytes is null ? 0 : 1);
        offset += 1;

        if (nameBytes is not null)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span[offset..], nameBytes.Length);
            offset += 4;
            nameBytes.CopyTo(span[offset..]);
            offset += nameBytes.Length;
        }

        BinaryPrimitives.WriteInt32LittleEndian(span[offset..], message.SampleRate);
        offset += 4;
        BinaryPrimitives.WriteInt32LittleEndian(span[offset..], message.Channels);
        offset += 4;
        span[offset] = (byte)(message.IsExclusiveMode ? 1 : 0);

        return stream.WriteAsync(buffer, cancellationToken).AsTask();
    }

    /// <summary>
    /// <see cref="LevelMeterFrameMessage"/> をストリームへ書き込む。
    /// </summary>
    public static Task WriteFrameAsync(Stream stream, LevelMeterFrameMessage message, CancellationToken cancellationToken = default)
    {
        var spectrum = message.Spectrum;
        var payloadLength = 4 + 4 + 4 + (spectrum.Length * 4);
        var buffer = new byte[1 + 4 + payloadLength];
        var span = buffer.AsSpan();

        span[0] = (byte)LevelMeterMessageType.Frame;
        BinaryPrimitives.WriteInt32LittleEndian(span[1..], payloadLength);

        var offset = 5;
        BinaryPrimitives.WriteSingleLittleEndian(span[offset..], message.Left);
        offset += 4;
        BinaryPrimitives.WriteSingleLittleEndian(span[offset..], message.Right);
        offset += 4;
        BinaryPrimitives.WriteInt32LittleEndian(span[offset..], spectrum.Length);
        offset += 4;

        for (var i = 0; i < spectrum.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(span[offset..], spectrum[i]);
            offset += 4;
        }

        return stream.WriteAsync(buffer, cancellationToken).AsTask();
    }
}

/// <summary>
/// ストリームから <see cref="ILevelMeterMessage"/> を1件読み取るヘルパー。
/// 接続断（ストリームの途中終了）は <see cref="EndOfStreamException"/> として呼び出し側へ伝播させる。
/// </summary>
public static class LevelMeterMessageReader
{
    /// <summary>
    /// ストリームからメッセージを1件読み取る。
    /// </summary>
    /// <exception cref="EndOfStreamException">ストリームが途中で終了（切断）した場合。</exception>
    /// <exception cref="InvalidDataException">未知のメッセージ種別を受信した場合。</exception>
    public static async Task<ILevelMeterMessage> ReadMessageAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var header = new byte[5];
        await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);

        var type = (LevelMeterMessageType)header[0];
        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(1));

        var payload = new byte[payloadLength];
        await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false);

        return type switch
        {
            LevelMeterMessageType.DeviceInfo => ParseDeviceInfo(payload),
            LevelMeterMessageType.Frame => ParseFrame(payload),
            _ => throw new InvalidDataException($"未知のレベルメーターIPCメッセージ種別を受信しました: {type}"),
        };
    }

    private static LevelMeterDeviceInfoMessage ParseDeviceInfo(byte[] payload)
    {
        var span = payload.AsSpan();
        var offset = 0;

        var hasName = span[offset] != 0;
        offset += 1;

        string? deviceName = null;
        if (hasName)
        {
            var nameLength = BinaryPrimitives.ReadInt32LittleEndian(span[offset..]);
            offset += 4;
            deviceName = Encoding.UTF8.GetString(span.Slice(offset, nameLength));
            offset += nameLength;
        }

        var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(span[offset..]);
        offset += 4;
        var channels = BinaryPrimitives.ReadInt32LittleEndian(span[offset..]);
        offset += 4;
        var isExclusiveMode = span[offset] != 0;

        return new LevelMeterDeviceInfoMessage(deviceName, sampleRate, channels, isExclusiveMode);
    }

    private static LevelMeterFrameMessage ParseFrame(byte[] payload)
    {
        var span = payload.AsSpan();
        var offset = 0;

        var left = BinaryPrimitives.ReadSingleLittleEndian(span[offset..]);
        offset += 4;
        var right = BinaryPrimitives.ReadSingleLittleEndian(span[offset..]);
        offset += 4;
        var spectrumLength = BinaryPrimitives.ReadInt32LittleEndian(span[offset..]);
        offset += 4;

        var spectrum = new float[spectrumLength];
        for (var i = 0; i < spectrumLength; i++)
        {
            spectrum[i] = BinaryPrimitives.ReadSingleLittleEndian(span[offset..]);
            offset += 4;
        }

        return new LevelMeterFrameMessage(left, right, spectrum);
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("パイプ接続が切断されました。");
            }

            totalRead += read;
        }
    }
}
