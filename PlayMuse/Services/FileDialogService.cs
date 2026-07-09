using Microsoft.Win32;
using PlayMuse.Core.Services;

namespace PlayMuse.Services;

/// <summary>
/// <see cref="OpenFileDialog"/>を用いて<see cref="IFileDialogService"/>を実装する。
/// </summary>
public sealed class FileDialogService : IFileDialogService
{
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
}
