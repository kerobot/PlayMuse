using System.Collections.ObjectModel;
using PlayMuse.Core.Models;

namespace PlayMuse.Core.Services;

/// <summary>
/// プレイリストの追加/削除/クリアと、現在トラックのNext/Previous遷移を提供するサービス。
/// </summary>
public interface IPlaylistService
{
    ObservableCollection<Track> Tracks { get; }

    Track? CurrentTrack { get; }

    int CurrentIndex { get; }

    event EventHandler? CurrentTrackChanged;

    bool IsLoopEnabled { get; set; }

    void Add(Track track);

    bool Remove(Track track);

    void Clear();

    bool MoveNext();

    bool MovePrevious();

    bool TrySetCurrentIndex(int index);

    bool Move(Track track, int targetIndex);

    /// <summary>
    /// 現在のプレイリスト（トラックの絶対パス一覧）をJSON形式で指定パスへ保存する。
    /// </summary>
    void SavePlaylist(string filePath);

    /// <summary>
    /// 指定パスのプレイリストファイルを読み込み、現在のプレイリストを置き換える。
    /// 存在しないトラックファイルはスキップされ、読込結果として返される。
    /// </summary>
    PlaylistLoadResult LoadPlaylist(string filePath);
}
