using NAudio.Wave;

namespace PlayMuse.Core.Services;

/// <summary>
/// L/Rチャンネルの縦型音量レベルメーターの1チャンネル分の表示レベル。
/// <see cref="Level"/> は現在値、<see cref="PeakLevel"/> は減衰付きピークホールド値（いずれも0〜10の連続値）。
/// UI側で段位置に応じた部分塗りつぶしを行うことで、なめらかな表示にできる。
/// </summary>
public readonly record struct ChannelLevel(double Level, double PeakLevel);

/// <summary>
/// 再生中の音声波形をキャプチャし、L/Rチャンネル別のピーク音量レベルを算出するサービス。
/// モノラル(1ch)の場合はL値をRにも複製し、2chを超えるフォーマットは先頭2ch（L/R）のみを対象とする。
/// スペクトラムアナライザーとは独立した経路で、より高速な追従性を持つレベル表示を提供する。
/// </summary>
public interface IChannelLevelMeterService
{
    /// <summary>
    /// 再生波形のバイト列を受け取り、L/Rチャンネル別にピークレベルを更新する。オーディオ出力スレッドから呼び出される想定。
    /// </summary>
    void PushSamples(ReadOnlySpan<byte> buffer, WaveFormat format);

    /// <summary>
    /// 直近の更新結果から、L/Rチャンネルそれぞれの表示レベル（0〜10、ピークホールド込み）を取得する。
    /// </summary>
    (ChannelLevel Left, ChannelLevel Right) GetLevels();

    /// <summary>
    /// 保持しているレベル・ピークホールド状態をクリアする。再生停止時やフォーマット変更時に呼ぶ。
    /// </summary>
    void Reset();
}
