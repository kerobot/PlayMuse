using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MuseWiser.Converters;

/// <summary>
/// bool 値を <see cref="Visibility"/> に変換する。true = Visible, false = Collapsed。
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// bool 値を <see cref="Visibility"/> に変換する。
    /// </summary>
    /// <param name="value">変換対象の bool 値。</param>
    /// <param name="targetType">変換先の型。</param>
    /// <param name="parameter">コンバーターパラメーター（未使用）。</param>
    /// <param name="culture">カルチャ情報（未使用）。</param>
    /// <returns>true なら <see cref="Visibility.Visible"/>、それ以外は <see cref="Visibility.Collapsed"/>。</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// <see cref="Visibility"/> を bool 値に逆変換する。
    /// </summary>
    /// <param name="value">変換対象の <see cref="Visibility"/> 値。</param>
    /// <param name="targetType">変換先の型。</param>
    /// <param name="parameter">コンバーターパラメーター（未使用）。</param>
    /// <param name="culture">カルチャ情報（未使用）。</param>
    /// <returns><see cref="Visibility.Visible"/> なら true、それ以外は false。</returns>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Visibility.Visible;
    }
}
