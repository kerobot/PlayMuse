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
}
