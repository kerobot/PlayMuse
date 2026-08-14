using NAudio.Wave;

namespace PlayMuse.Core.Services;

/// <summary>
/// スペクトラムアナライザーの1バンド分の表示レベル。
/// <see cref="Level"/> は現在値、<see cref="PeakLevel"/> は減衰付きピークホールド値（いずれも0〜10の連続値）。
/// UI側で段位置に応じた部分塗りつぶしを行うことで、なめらかな表示にできる。
/// </summary>
public readonly record struct SpectrumBandLevel(double Level, double PeakLevel);

/// <summary>
/// 再生中の音声波形をキャプチャし、FFTによって16バンドのスペクトラムレベルを算出するサービス。
/// </summary>
public interface ISpectrumAnalyzerService
{
    /// <summary>
    /// 再生波形のバイト列をキャプチャする。オーディオ出力スレッドから呼び出される想定。
    /// </summary>
    void PushSamples(ReadOnlySpan<byte> buffer, WaveFormat format);

    /// <summary>
    /// 直近のキャプチャデータからFFTを実行し、16バンド分のレベル（0〜10、ピークホールド込み）を取得する。
    /// </summary>
    IReadOnlyList<SpectrumBandLevel> GetBandLevels();

    /// <summary>
    /// キャプチャバッファとピークホールド状態をクリアする。再生停止時やフォーマット変更時に呼び出す。
    /// </summary>
    void Reset();
}
