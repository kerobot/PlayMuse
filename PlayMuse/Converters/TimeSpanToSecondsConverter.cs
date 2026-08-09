using System.Globalization;
using System.Windows.Data;

namespace PlayMuse.Converters;

/// <summary>
/// <see cref="Slider"/>のValue(double, 秒)と<see cref="TimeSpan"/>を相互変換する。
/// </summary>
public sealed class TimeSpanToSecondsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var seconds = value is TimeSpan timeSpan ? timeSpan.TotalSeconds : 0d;

        // Slider.Maximum が 0（未再生・曲未読込時）のままだと Minimum と等しくなり、
        // つまみが右端に表示されてしまうため、Maximum 用途では最小値を確保する。
        if (seconds <= 0 && string.Equals(parameter as string, "EnsureNonZero", StringComparison.Ordinal))
        {
            return 1d;
        }

        return seconds;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double seconds && seconds >= 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
}
