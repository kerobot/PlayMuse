namespace PlayMuse.Core.Services;

/// <summary>
/// 音楽ファイルを選択するダイアログ表示を抽象化する。
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// 音楽ファイル選択ダイアログを表示する。キャンセル時は空のリストを返す。
    /// </summary>
    IReadOnlyList<string> ShowOpenAudioFilesDialog();

    /// <summary>
    /// プレイリスト保存先を選択する保存ダイアログを表示する。キャンセル時はnullを返す。
    /// </summary>
    string? ShowSavePlaylistFileDialog();

    /// <summary>
    /// 読み込むプレイリストファイルを選択するダイアログを表示する。キャンセル時はnullを返す。
    /// </summary>
    string? ShowOpenPlaylistFileDialog();
}
