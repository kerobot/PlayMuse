using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MuseWiser.Converters;

/// <summary>
/// スペクトラムバンドのピークホールド位置（values[0]）とセグメントインデックス（values[1]）から、
/// ピークホールドを表示すべきセグメントだけ発光色（現在のレベル色と同系統）を返すオーバーレイ用コンバータ。
/// 該当セグメント以外は透明を返し、通常の点灯表示（<see cref="SpectrumSegmentBrushMultiConverter"/>）に
/// 重ねて描画することでピークホールドの表示を実現する。
/// </summary>
public sealed class SpectrumPeakSegmentBrushMultiConverter : IMultiValueConverter
{
    /// <summary>
    /// ピークホールド位置とセグメントインデックスから、そのセグメントがピーク位置であるかを判定し発光色を返す。
    /// </summary>
    /// <param name="values">values[0] にピークホールド位置（int）、values[1] にセグメントインデックス（int）を指定する。</param>
    /// <param name="targetType">変換先の型（未使用）。</param>
    /// <param name="parameter">コンバーターパラメーター（未使用）。</param>
    /// <param name="culture">カルチャ情報（未使用）。</param>
    /// <returns>該当セグメントの場合は発光色、それ以外は <see cref="Brushes.Transparent"/>。</returns>
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [int peakSegmentIndex, int segmentIndex] || segmentIndex != peakSegmentIndex)
        {
            return Brushes.Transparent;
        }

        var position = (segmentIndex + 1) / (double)ViewModels.SpectrumBandViewModel.SegmentCount;
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
