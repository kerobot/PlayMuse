using NAudio.Flac;
using NAudio.Wave;

namespace PlayMuse.Core.Services;

/// <summary>
/// 拡張子に応じて、元のサンプルフォーマット（ビット深度・サンプルレート）を保持したまま
/// デコードする <see cref="WaveStream"/> を選択して生成するファクトリ。
/// </summary>
/// <remarks>
/// <see cref="AudioFileReader"/> は内部で必ず IEEE Float 32bit の <see cref="ISampleProvider"/>
/// パイプラインへ変換するため、24bit FLAC 等の元のビット深度を保持できない。
/// ビットパーフェクト再生には、デコード結果を素のPCM/Floatのまま返す
/// <see cref="WaveStream"/> 実装を直接使用する必要がある。
/// </remarks>
internal static class NativeAudioFileReaderFactory
{
    /// <summary>
    /// 指定ファイルを、元のビット深度・サンプルレートを保持したまま読み込む
    /// <see cref="WaveStream"/> を生成する。
    /// </summary>
    public static WaveStream Create(string filePath)
    {
        var extension = Path.GetExtension(filePath);

        if (string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase))
        {
            // WAVはコンテナのPCM/Floatデータをそのまま保持するため、WaveFileReaderで直接読み込む。
            // ただし、WAVE_FORMAT_EXTENSIBLE 形式の WAV でサブフォーマットが PCM/IEEE Float の場合、
            // ACM 経由の変換を回避するため、WaveFormat を再解釈したラッパーを返す。
            return WavFormatNormalizer.TryOpenReinterpreted(filePath) ?? new WaveFileReader(filePath);
        }

        if (string.Equals(extension, ".mp3", StringComparison.OrdinalIgnoreCase))
        {
            // Mp3FileReaderはデコード結果を素のPCM（通常16bit）のまま返す。
            return new Mp3FileReader(filePath);
        }

        if (string.Equals(extension, ".flac", StringComparison.OrdinalIgnoreCase))
        {
            // FLACはMedia Foundationではビット深度維持が保証されないため、
            // FLACのSTREAMINFO（SampleRate/Channels/BitsPerSample）を直接WaveFormatへ
            // 反映するFlacReader（BunLabs.NAudio.Flac）でデコードし、整数PCMをそのまま取得する。
            return new FlacReader(filePath);
        }

        // AAC/M4A等、FLAC以外の非対応フォーマットはMedia Foundationでデコードする。
        // これらはビットパーフェクト再生の対象外（元のビット深度維持が保証されない）。
        // RequestFloatOutput = false により、可能な限り元のビット深度の整数PCMを要求する。
        var settings = new MediaFoundationReader.MediaFoundationReaderSettings
        {
            RequestFloatOutput = false,
        };

        return new MediaFoundationReader(filePath, settings);
    }
}
