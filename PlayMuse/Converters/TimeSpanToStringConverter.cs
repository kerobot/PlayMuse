using System.Globalization;
using System.Windows.Data;

namespace PlayMuse.Converters;

/// <summary>
/// <see cref="TimeSpan"/>を "m:ss" または "h:mm:ss" 形式の表示用文字列に変換する。
/// </summary>
public sealed class TimeSpanToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var timeSpan = value is TimeSpan ts ? ts : TimeSpan.Zero;
        return timeSpan.Hours > 0
            ? timeSpan.ToString(@"h\:mm\:ss", culture)
            : timeSpan.ToString(@"m\:ss", culture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
