using System.Globalization;
using System.Windows.Data;

namespace PlayMuse.Converters;

/// <summary>
/// オーディオ形式情報を表示文字列に変換するマルチバリューコンバーター。
/// BitsPerSample と ContainerFormatLabel を受け取り、適切な表示文字列を返す。
/// </summary>
public class AudioFormatToStringConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
        {
            return string.Empty;
        }

        var bitsPerSample = values[0] as int? ?? 0;
        var containerFormatLabel = values[1] as string;

        // ビット数とフォーマット名の両方が有効な場合は両方を表示(例: "24bit FLAC")
        if (bitsPerSample > 0 && !string.IsNullOrEmpty(containerFormatLabel))
        {
            return $"{bitsPerSample} bit {containerFormatLabel}";
        }

        // ビット数のみ有効な場合はビット数を表示
        if (bitsPerSample > 0)
        {
            return $"{bitsPerSample} bit";
        }

        // ビット数が無効(0)でフォーマットラベルがある場合はフォーマット名のみ表示(非可逆圧縮形式など)
        if (!string.IsNullOrEmpty(containerFormatLabel))
        {
            return containerFormatLabel;
        }

        // どちらも無効な場合は空文字
        return string.Empty;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
