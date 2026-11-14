using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MuseWiser.ViewModels;

namespace MuseWiser.Converters;

/// <summary>
/// スペクトラムバンドの点灯ブロック数（values[0]）とセグメントインデックス（values[1]）から、
/// <see cref="LevelColorPalette"/> を用いた LED 風の色（緑→黄→赤）を返す。消灯時は暗いグレーを返す。
/// 音量メーター用の <see cref="LevelSegmentBrushMultiConverter"/> とは異なり、
/// 量子化済みの整数値（ActiveSegmentCount）を前提とする。
/// </summary>
public sealed class SpectrumSegmentBrushMultiConverter : IMultiValueConverter
{
    /// <summary>
    /// 点灯ブロック数とセグメントインデックスから、そのセグメントの発光色を返す。
    /// </summary>
    /// <param name="values">values[0] に点灯ブロック数（int）、values[1] にセグメントインデックス（int）を指定する。</param>
    /// <param name="targetType">変換先の型（未使用）。</param>
    /// <param name="parameter">コンバーターパラメーター（未使用）。</param>
    /// <param name="culture">カルチャ情報（未使用）。</param>
    /// <returns>点灯対象の場合は発光色、それ以外は <see cref="LevelColorPalette.OffBrush"/>。</returns>
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [int activeSegmentCount, int segmentIndex] || segmentIndex >= activeSegmentCount)
        {
            return LevelColorPalette.OffBrush;
        }

        var position = (segmentIndex + 1) / (double)SpectrumBandViewModel.SegmentCount;
        return LevelColorPalette.ResolveColor(position);
    }

    /// <summary>
    /// この変換は逆変換をサポートしない。
    /// </summary>
    /// <param name="value">逆変換対象の値（未使用）。</param>
    /// <param name="targetTypes">逆変換先の型一覧（未使用）。</param>
    /// <param name="parameter">コンバーターパラメーター（未使用）。</param>
    /// <param name="culture">カルチャ情報（未使用）。</param>
    /// <returns>常に例外をスローするため戻り値なし。</returns>
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
