using System.Globalization;
using System.Windows.Data;
using PlayMuse.Core.Models;

namespace PlayMuse.Converters;

/// <summary>
/// <see cref="PlaybackLoopMode"/>の値をループボタンのツールチップ文言に変換する。
/// </summary>
public class LoopModeToToolTipConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            PlaybackLoopMode.RepeatAll => "ループ再生：プレイリスト全曲",
            PlaybackLoopMode.RepeatOne => "ループ再生：1曲のみ",
            _ => "ループ再生：オフ",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
