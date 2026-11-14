using System.Globalization;
using System.Windows.Data;

namespace MuseWiser.Converters;

/// <summary>
/// 音量レベル（values[0]）とセグメントインデックス（values[1]）から、
/// そのセグメントを点灯させるべきか判定する。16 セグメント構成を前提とする。
/// </summary>
public sealed class LevelSegmentActiveMultiConverter : IMultiValueConverter
{
    /// <summary>
    /// LED セグメントの総数。
    /// </summary>
    public const int SegmentCount = 16;

    /// <summary>
    /// 音量レベルとセグメントインデックスから、そのセグメントを点灯させるべきかを判定する。
    /// </summary>
    /// <param name="values">values[0] に音量レベル（double、0.0～1.0）、values[1] にセグメントインデックス（int）を指定する。</param>
    /// <param name="targetType">変換先の型（未使用）。</param>
    /// <param name="parameter">コンバーターパラメーター（未使用）。</param>
    /// <param name="culture">カルチャ情報（未使用）。</param>
    /// <returns>セグメントを点灯させる場合は true。values の形式が不正な場合は false。</returns>
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [double level, int index])
        {
            return false;
        }

        var threshold = (index + 1) / (double)SegmentCount;
        return level >= threshold;
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
