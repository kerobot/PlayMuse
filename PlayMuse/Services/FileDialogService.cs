using Microsoft.Win32;
using PlayMuse.Core.Services;

namespace PlayMuse.Services;

/// <summary>
/// <see cref="OpenFileDialog"/>を用いて<see cref="IFileDialogService"/>を実装する。
/// </summary>
public sealed class FileDialogService : IFileDialogService
{
    private const string PlaylistFilter = "PlayMuseプレイリスト (*.plm)|*.plm";
    private const string PlaylistDefaultExt = ".plm";

    public IReadOnlyList<string> ShowOpenAudioFilesDialog()
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = SupportedAudioFormats.BuildFileDialogFilter(),
            Title = "音楽ファイルを開く",
        };

        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }

    public string? ShowSavePlaylistFileDialog()
    {
        var dialog = new SaveFileDialog
        {
            Filter = PlaylistFilter,
            DefaultExt = PlaylistDefaultExt,
            AddExtension = true,
            Title = "プレイリストを保存",
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ShowOpenPlaylistFileDialog()
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = false,
            Filter = PlaylistFilter,
            DefaultExt = PlaylistDefaultExt,
            Title = "プレイリストを開く",
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
