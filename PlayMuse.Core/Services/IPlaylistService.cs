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
}
