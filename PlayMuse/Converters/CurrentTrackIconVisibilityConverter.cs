using System.Globalization;
using System.Windows;
using System.Windows.Data;
using PlayMuse.Core.Models;

namespace PlayMuse.Converters;

/// <summary>
/// values[0]（自身のトラック）と values[1]（現在再生中のトラック）が同一参照で、
/// values[2]（再生状態）が Stopped 以外の場合に、再生中アイコンを表示するために Visible を返す。
/// それ以外（起動直後や停止中）は Collapsed。
/// </summary>
public class CurrentTrackIconVisibilityConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is [{ } current, { } playing, PlaybackState status]
            && ReferenceEquals(current, playing)
            && status != PlaybackState.Stopped)
        {
            return Visibility.Visible;
        }

        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
