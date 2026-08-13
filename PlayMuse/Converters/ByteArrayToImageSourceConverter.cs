using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PlayMuse.Converters;

/// <summary>
/// アルバムアートのバイト配列（<see cref="Track.AlbumArtData"/>）を <see cref="ImageSource"/> に変換する。
/// データが無い、または画像として解釈できない場合は null を返す。
/// </summary>
public sealed class ByteArrayToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } bytes)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.None;
            // 表示サイズ（40x40程度）に対して高DPI環境でも滲まないよう、
            // 実表示より大きめの解像度でデコードしてから縮小表示させる。
            image.DecodePixelWidth = 128;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
