using NAudio.Wave;

namespace PlayMuse.Core.Services;

/// <summary>
/// 24bit PCM（3バイト/サンプル）のソースを、32bitコンテナに24bit有効データを左詰め格納する
/// 「24-in-32」PCM形式（4バイト/サンプル、下位1バイトは0埋め）へ無劣化で変換する<see cref="IWaveProvider"/>。
/// 一部のUSB DAC（例: Fiio DM15 R2R）はWASAPI排他モードでこの形式のみを受理するため、
/// ビットパーフェクト再生のために使用する。
/// </summary>
/// <remarks>
/// <see cref="Read"/> は要求バイト数 <c>count</c> を出力コンテナサイズ（4バイト/サンプル）の倍数と
/// 見なして処理しており、端数（3バイト境界に満たない余り）は次回呼び出しへ繰り越さない。
/// WASAPI排他モードの<see cref="NAudio.Wave.WasapiOut"/>は常にBlockAlign（フレーム単位）に
/// 整列したバイト数でReadを要求するため、この経路で端数が発生することはない。
/// 本プロバイダーはWASAPI排他専用の内部実装であり、共有モードや他の汎用<see cref="IWaveProvider"/>
/// パイプラインから利用する場合は、端数の繰り越し処理を追加する必要がある。
/// </remarks>
internal sealed class Pack24In32WaveProvider : IWaveProvider
{
    private readonly IWaveProvider source;
    private byte[] sourceBuffer = [];

    public Pack24In32WaveProvider(IWaveProvider source, WaveFormat packedFormat)
    {
        this.source = source;
        WaveFormat = packedFormat;
    }

    public WaveFormat WaveFormat { get; }

    public int Read(byte[] buffer, int offset, int count)
    {
        // 出力(4バイト/サンプル)で要求されたバイト数に対応する、ソース側(3バイト/サンプル)の読み取りバイト数を算出する。
        var sampleCount = count / 4;
        var sourceBytesNeeded = sampleCount * 3;

        if (sourceBuffer.Length < sourceBytesNeeded)
        {
            sourceBuffer = new byte[sourceBytesNeeded];
        }

        var sourceBytesRead = source.Read(sourceBuffer, 0, sourceBytesNeeded);
        var samplesRead = sourceBytesRead / 3;

        for (var i = 0; i < samplesRead; i++)
        {
            var srcIndex = i * 3;
            var dstIndex = offset + (i * 4);

            // 24bit有効データを32bitコンテナへ左詰め格納（下位1バイトは0埋め）。
            buffer[dstIndex] = 0;
            buffer[dstIndex + 1] = sourceBuffer[srcIndex];
            buffer[dstIndex + 2] = sourceBuffer[srcIndex + 1];
            buffer[dstIndex + 3] = sourceBuffer[srcIndex + 2];
        }

        return samplesRead * 4;
    }
}
