using System.Globalization;
using System.Windows.Data;

namespace PlayMuse.Converters;

/// <summary>
/// values[0]（自身のトラック）と values[1]（現在ドラッグ中のトラック）が同一参照の場合、
/// ドラッグ中であることを示すため半透明の不透明度を返す。それ以外は通常表示。
/// </summary>
public class DraggingOpacityConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is [{ } current, { } dragging] && ReferenceEquals(current, dragging))
        {
            return 0.35;
        }

        return 1.0;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
