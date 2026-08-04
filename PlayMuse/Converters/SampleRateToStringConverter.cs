using System.Globalization;
using System.Windows.Data;

namespace PlayMuse.Converters;

/// <summary>
/// サンプリングレート（Hz）を kHz 表記の表示用文字列に変換する（例: 44100 → "44.1 kHz"）。
/// 値が 0 以下の場合は空文字列を返す。
/// </summary>
public sealed class SampleRateToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var sampleRate = value is int rate ? rate : 0;
        return sampleRate > 0
            ? $"{sampleRate / 1000.0:0.#} kHz"
            : string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
