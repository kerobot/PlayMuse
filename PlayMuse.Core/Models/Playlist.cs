using System.Collections.ObjectModel;

namespace PlayMuse.Core.Models;

/// <summary>
/// トラックの順序付きコレクションと現在再生インデックスを管理する、UIフレームワーク非依存の純粋なロジックモデル。
/// </summary>
public sealed class Playlist
{
    public ObservableCollection<Track> Tracks { get; } = [];

    public int CurrentIndex { get; private set; } = -1;

    public Track? CurrentTrack => CurrentIndex >= 0 && CurrentIndex < Tracks.Count
        ? Tracks[CurrentIndex]
        : null;

    public void Add(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        Tracks.Add(track);
        if (CurrentIndex < 0)
        {
            CurrentIndex = 0;
        }
    }

    public bool Remove(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        var removedIndex = Tracks.IndexOf(track);
        if (removedIndex < 0)
        {
            return false;
        }

        Tracks.RemoveAt(removedIndex);

        if (Tracks.Count == 0)
        {
            CurrentIndex = -1;
        }
        else if (removedIndex < CurrentIndex)
        {
            CurrentIndex--;
        }
        else if (removedIndex == CurrentIndex)
        {
            CurrentIndex = Math.Min(CurrentIndex, Tracks.Count - 1);
        }

        return true;
    }

    public void Clear()
    {
        Tracks.Clear();
        CurrentIndex = -1;
    }

    public bool MoveNext()
    {
        if (CurrentIndex < 0 || CurrentIndex >= Tracks.Count - 1)
        {
            return false;
        }

        CurrentIndex++;
        return true;
    }

    public bool MovePrevious()
    {
        if (CurrentIndex <= 0)
        {
            return false;
        }

        CurrentIndex--;
        return true;
    }

    public bool TrySetCurrentIndex(int index)
    {
        if (index < 0 || index >= Tracks.Count)
        {
            return false;
        }

        CurrentIndex = index;
        return true;
    }
}
