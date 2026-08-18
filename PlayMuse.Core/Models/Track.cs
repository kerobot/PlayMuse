using CommunityToolkit.Mvvm.ComponentModel;

namespace PlayMuse.Core.Models;

/// <summary>
/// 再生対象の1トラックを表すモデル。Title/Artist/Album/Durationはメタデータ読み込み完了時に
/// 非同期で更新されるため、UIへ即座に反映できるよう ObservableObject を継承する。
/// </summary>
public sealed partial class Track : ObservableObject
{
    public string FilePath { get; }

    public string FileName => Path.GetFileName(FilePath);

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string? Artist { get; set; }

    [ObservableProperty]
    public partial string? Album { get; set; }

    [ObservableProperty]
    public partial TimeSpan Duration { get; set; }

    [ObservableProperty]
    public partial int SampleRate { get; set; }

    [ObservableProperty]
    public partial int BitsPerSample { get; set; }

    [ObservableProperty]
    public partial string? ContainerFormatLabel { get; set; }

    [ObservableProperty]
    public partial byte[]? AlbumArtData { get; set; }

    public Track(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        FilePath = filePath;
        Title = Path.GetFileNameWithoutExtension(filePath);
    }
}
