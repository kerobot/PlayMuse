using System.Globalization;
using System.Windows.Data;

namespace PlayMuse.Converters;

/// <summary>
/// <see cref="Slider"/>のValue(double, 秒)と<see cref="TimeSpan"/>を相互変換する。
/// </summary>
public sealed class TimeSpanToSecondsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is TimeSpan timeSpan ? timeSpan.TotalSeconds : 0d;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double seconds && seconds >= 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
}
