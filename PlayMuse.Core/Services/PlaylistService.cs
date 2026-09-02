using System.Collections.ObjectModel;
using System.Text.Json;
using PlayMuse.Core.Models;

namespace PlayMuse.Core.Services;

/// <summary>
/// <see cref="Playlist"/>（純粋ロジックモデル）をラップし、現在トラック変更の通知イベントを追加するサービス。
/// </summary>
public sealed class PlaylistService : IPlaylistService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Playlist playlist = new();

    public ObservableCollection<Track> Tracks => playlist.Tracks;

    public Track? CurrentTrack => playlist.CurrentTrack;

    public int CurrentIndex => playlist.CurrentIndex;

    public event EventHandler? CurrentTrackChanged;

    public bool IsLoopEnabled
    {
        get => playlist.IsLoopEnabled;
        set => playlist.IsLoopEnabled = value;
    }

    public void Add(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        var previousTrack = playlist.CurrentTrack;
        playlist.Add(track);
        RaiseIfCurrentTrackChanged(previousTrack);
    }

    public void Insert(int index, Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        var previousTrack = playlist.CurrentTrack;
        playlist.Insert(index, track);
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

    public bool Move(Track track, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(track);

        return playlist.Move(track, targetIndex);
    }

    public void SavePlaylist(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var data = new PlaylistFileData
        {
            TrackFilePaths = [.. playlist.Tracks.Select(track => track.FilePath)],
        };

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(data, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public PlaylistLoadResult LoadPlaylist(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var json = File.ReadAllText(filePath);
        var data = JsonSerializer.Deserialize<PlaylistFileData>(json, JsonOptions) ?? new PlaylistFileData();

        var previousTrack = playlist.CurrentTrack;
        playlist.Clear();

        var missingFilePaths = new List<string>();
        foreach (var trackFilePath in data.TrackFilePaths)
        {
            if (File.Exists(trackFilePath))
            {
                playlist.Add(new Track(trackFilePath));
            }
            else
            {
                missingFilePaths.Add(trackFilePath);
            }
        }

        RaiseIfCurrentTrackChanged(previousTrack);

        return new PlaylistLoadResult(playlist.Tracks.Count, missingFilePaths);
    }

    private void RaiseIfCurrentTrackChanged(Track? previousTrack)
    {
        if (!ReferenceEquals(previousTrack, playlist.CurrentTrack))
        {
            CurrentTrackChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// プレイリストファイル（.plm）のJSONスキーマ。トラックの絶対パス一覧のみを保持する。
    /// </summary>
    private sealed class PlaylistFileData
    {
        public List<string> TrackFilePaths { get; set; } = [];
    }
}
