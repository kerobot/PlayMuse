using System.Globalization;
using System.Windows.Data;

namespace PlayMuse.Converters;

/// <summary>
/// ビット数（bit）を表示用文字列に変換する（例: 24 → "24 bit"）。
/// 値が 0 以下の場合は空文字列を返す。
/// </summary>
public sealed class BitsPerSampleToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var bitsPerSample = value is int bits ? bits : 0;
        return bitsPerSample > 0
            ? $"{bitsPerSample} bit"
            : string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
