using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PlayMuse.Core.Models;

namespace PlayMuse.Converters;

/// <summary>
/// values[0]（自身のトラック）と values[1]（現在再生中のトラック）が同一参照で、
/// values[2]（再生状態）が Stopped 以外の場合に、再生中であることを示すオレンジ色の文字色を返す。
/// それ以外は既定の文字色（null）を返す。
/// </summary>
public class CurrentTrackForegroundConverter : IMultiValueConverter
{
    private static readonly Brush PlayingBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x5A));

    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is [{ } current, { } playing, PlaybackState status]
            && ReferenceEquals(current, playing)
            && status != PlaybackState.Stopped)
        {
            return PlayingBrush;
        }

        return DependencyProperty.UnsetValue;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
