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
    private string title;

    [ObservableProperty]
    private string? artist;

    [ObservableProperty]
    private string? album;

    [ObservableProperty]
    private TimeSpan duration;

    public Track(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        FilePath = filePath;
        title = Path.GetFileNameWithoutExtension(filePath);
    }
}
