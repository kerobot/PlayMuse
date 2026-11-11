using System.Globalization;
using System.Windows.Data;
using PlayMuse.Core.Models;

namespace PlayMuse.Converters;

/// <summary>
/// <see cref="PlaybackLoopMode"/>の値をループボタンに表示するアイコン文字列に変換する。
/// </summary>
public class LoopModeToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            PlaybackLoopMode.RepeatOne => "🔂",
            _ => "🔁",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
