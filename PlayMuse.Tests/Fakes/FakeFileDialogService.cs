using PlayMuse.Core.Services;

namespace PlayMuse.Tests.Fakes;

/// <summary>
/// <see cref="IFileDialogService"/> のテスト用フェイク。ダイアログを表示せず、事前に設定した値を返す。
/// </summary>
public sealed class FakeFileDialogService : IFileDialogService
{
    public IReadOnlyList<string> FilesToReturn { get; set; } = [];

    public string? SavePathToReturn { get; set; }

    public string? OpenPlaylistPathToReturn { get; set; }

    public IReadOnlyList<string> ShowOpenAudioFilesDialog() => FilesToReturn;

    public string? ShowSavePlaylistFileDialog() => SavePathToReturn;

    public string? ShowOpenPlaylistFileDialog() => OpenPlaylistPathToReturn;
}
