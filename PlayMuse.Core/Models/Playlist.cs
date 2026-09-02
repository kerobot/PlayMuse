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

    public bool IsLoopEnabled { get; set; }

    public void Add(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        Tracks.Add(track);
        if (CurrentIndex < 0)
        {
            CurrentIndex = 0;
        }
    }

    /// <summary>
    /// 指定インデックス位置にトラックを挿入する。挿入位置が現在再生インデックス以下の場合、
    /// 現在再生中のトラック参照を維持するためCurrentIndexを後方へずらす。
    /// </summary>
    public void Insert(int index, Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        var clampedIndex = Math.Clamp(index, 0, Tracks.Count);
        Tracks.Insert(clampedIndex, track);

        if (CurrentIndex < 0)
        {
            CurrentIndex = 0;
        }
        else if (clampedIndex <= CurrentIndex)
        {
            CurrentIndex++;
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
        if (CurrentIndex < 0 || Tracks.Count == 0)
        {
            return false;
        }

        if (CurrentIndex >= Tracks.Count - 1)
        {
            if (IsLoopEnabled && Tracks.Count > 0)
            {
                CurrentIndex = 0;
                return true;
            }
            return false;
        }

        CurrentIndex++;
        return true;
    }

    public bool MovePrevious()
    {
        if (CurrentIndex < 0 || Tracks.Count == 0)
        {
            return false;
        }

        if (CurrentIndex <= 0)
        {
            if (IsLoopEnabled && Tracks.Count > 0)
            {
                CurrentIndex = Tracks.Count - 1;
                return true;
            }
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

    /// <summary>
    /// 指定したトラックを targetIndex（挿入先、移動前のインデックス基準）の位置へ移動する。
    /// 現在再生中のトラック参照は移動後も維持され、CurrentIndexは新しい位置へ追従する。
    /// </summary>
    public bool Move(Track track, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(track);

        var currentPosition = Tracks.IndexOf(track);
        if (currentPosition < 0)
        {
            return false;
        }

        var clampedTargetIndex = Math.Clamp(targetIndex, 0, Tracks.Count);
        var adjustedTargetIndex = currentPosition < clampedTargetIndex ? clampedTargetIndex - 1 : clampedTargetIndex;
        adjustedTargetIndex = Math.Clamp(adjustedTargetIndex, 0, Tracks.Count - 1);

        if (adjustedTargetIndex == currentPosition)
        {
            return false;
        }

        var currentTrackReference = CurrentTrack;

        Tracks.Move(currentPosition, adjustedTargetIndex);

        if (currentTrackReference is not null)
        {
            CurrentIndex = Tracks.IndexOf(currentTrackReference);
        }

        return true;
    }
}
