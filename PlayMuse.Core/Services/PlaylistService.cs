using System.Collections.ObjectModel;
using PlayMuse.Core.Models;

namespace PlayMuse.Core.Services;

/// <summary>
/// <see cref="Playlist"/>（純粋ロジックモデル）をラップし、現在トラック変更の通知イベントを追加するサービス。
/// </summary>
public sealed class PlaylistService : IPlaylistService
{
    private readonly Playlist playlist = new();

    public ObservableCollection<Track> Tracks => playlist.Tracks;

    public Track? CurrentTrack => playlist.CurrentTrack;

    public int CurrentIndex => playlist.CurrentIndex;

    public event EventHandler? CurrentTrackChanged;

    public void Add(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        var previousTrack = playlist.CurrentTrack;
        playlist.Add(track);
        RaiseIfCurrentTrackChanged(previousTrack);
    }

    public bool Remove(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        var previousTrack = playlist.CurrentTrack;
        var removed = playlist.Remove(track);
        if (removed)
        {
            RaiseIfCurrentTrackChanged(previousTrack);
        }

        return removed;
    }

    public void Clear()
    {
        var previousTrack = playlist.CurrentTrack;
        playlist.Clear();
        RaiseIfCurrentTrackChanged(previousTrack);
    }

    public bool MoveNext()
    {
        var previousTrack = playlist.CurrentTrack;
        var moved = playlist.MoveNext();
        if (moved)
        {
            RaiseIfCurrentTrackChanged(previousTrack);
        }

        return moved;
    }

    public bool MovePrevious()
    {
        var previousTrack = playlist.CurrentTrack;
        var moved = playlist.MovePrevious();
        if (moved)
        {
            RaiseIfCurrentTrackChanged(previousTrack);
        }

        return moved;
    }

    public bool TrySetCurrentIndex(int index)
    {
        var previousTrack = playlist.CurrentTrack;
        var changed = playlist.TrySetCurrentIndex(index);
        if (changed)
        {
            RaiseIfCurrentTrackChanged(previousTrack);
        }

        return changed;
    }

    private void RaiseIfCurrentTrackChanged(Track? previousTrack)
    {
        if (!ReferenceEquals(previousTrack, playlist.CurrentTrack))
        {
            CurrentTrackChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
