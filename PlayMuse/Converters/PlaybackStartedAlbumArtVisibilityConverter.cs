using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PlayMuse.Converters;

/// <summary>
/// 「再生が一度でも開始されたか」（<see cref="PlayMuse.Core.ViewModels.MainViewModel.HasPlaybackStarted"/>）と
/// 「アルバムアートの有無」（<see cref="PlayMuse.Core.Models.Track.AlbumArtData"/>）の2値から、
/// アルバムアート画像またはプレースホルダーの表示可否を算出するマルチバインディング用コンバータ。
/// 起動直後の自動読み込みで先頭トラックが選択されただけの状態ではアートを表示しないため、
/// 再生開始前は常に非表示（<see cref="Invert"/> 側はプレースホルダーとして表示）とする。
/// </summary>
public sealed class PlaybackStartedAlbumArtVisibilityConverter : IMultiValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasPlaybackStarted = values.Length > 0 && values[0] is true;
        var hasAlbumArt = values.Length > 1 && values[1] is byte[] { Length: > 0 };

        var visible = hasPlaybackStarted && hasAlbumArt;
        if (Invert)
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
