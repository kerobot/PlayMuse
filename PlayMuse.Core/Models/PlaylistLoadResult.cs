namespace PlayMuse.Core.Models;

/// <summary>
/// プレイリストファイルの読込結果。読み込めたトラック数と、
/// ファイルが見つからずスキップされたパス一覧を保持する。
/// </summary>
public sealed record PlaylistLoadResult(int LoadedCount, IReadOnlyList<string> MissingFilePaths);
