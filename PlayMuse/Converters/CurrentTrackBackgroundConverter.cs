using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PlayMuse.Core.Models;

namespace PlayMuse.Converters;

/// <summary>
/// values[0]（自身のトラック）と values[1]（現在再生中のトラック）が同一参照で、
/// values[2]（再生状態）が Stopped 以外の場合に、再生中であることを示すハイライト背景色を返す。
/// それ以外（起動直後や停止中）は透明とする。
/// </summary>
public class CurrentTrackBackgroundConverter : IMultiValueConverter
{
    private static readonly Brush HighlightBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xEB, 0xFF));

    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is [{ } current, { } playing, PlaybackState status]
            && ReferenceEquals(current, playing)
            && status != PlaybackState.Stopped)
        {
            return HighlightBrush;
        }

        return Brushes.Transparent;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
