using PlayMuse.Core.Models;

namespace PlayMuse.Tests.Models;

public class PlaylistTests
{
    private static Track CreateTrack(string fileName) => new(Path.Combine(Path.GetTempPath(), fileName));

    [Fact]
    public void Add_FirstTrack_SetsCurrentIndexToZero()
    {
        var playlist = new Playlist();

        playlist.Add(CreateTrack("a.mp3"));

        Assert.Equal(0, playlist.CurrentIndex);
        Assert.NotNull(playlist.CurrentTrack);
    }

    [Fact]
    public void MoveNext_AtLastTrack_ReturnsFalseAndKeepsIndex()
    {
        var playlist = new Playlist();
        playlist.Add(CreateTrack("a.mp3"));
        playlist.Add(CreateTrack("b.mp3"));
        playlist.TrySetCurrentIndex(1);

        var moved = playlist.MoveNext();

        Assert.False(moved);
        Assert.Equal(1, playlist.CurrentIndex);
    }

    [Fact]
    public void MovePrevious_AtFirstTrack_ReturnsFalseAndKeepsIndex()
    {
        var playlist = new Playlist();
        playlist.Add(CreateTrack("a.mp3"));
        playlist.Add(CreateTrack("b.mp3"));

        var moved = playlist.MovePrevious();

        Assert.False(moved);
        Assert.Equal(0, playlist.CurrentIndex);
    }

    [Fact]
    public void MoveNext_MiddleTrack_AdvancesIndex()
    {
        var playlist = new Playlist();
        playlist.Add(CreateTrack("a.mp3"));
        playlist.Add(CreateTrack("b.mp3"));
        playlist.Add(CreateTrack("c.mp3"));

        var moved = playlist.MoveNext();

        Assert.True(moved);
        Assert.Equal(1, playlist.CurrentIndex);
    }

    [Fact]
    public void Remove_CurrentTrack_ClampsIndexToNewLastTrack()
    {
        var playlist = new Playlist();
        var a = CreateTrack("a.mp3");
        var b = CreateTrack("b.mp3");
        playlist.Add(a);
        playlist.Add(b);
        playlist.TrySetCurrentIndex(1);

        playlist.Remove(b);

        Assert.Equal(0, playlist.CurrentIndex);
        Assert.Same(a, playlist.CurrentTrack);
    }

    [Fact]
    public void Remove_LastRemainingTrack_ResetsToEmpty()
    {
        var playlist = new Playlist();
        var a = CreateTrack("a.mp3");
        playlist.Add(a);

        playlist.Remove(a);

        Assert.Equal(-1, playlist.CurrentIndex);
        Assert.Null(playlist.CurrentTrack);
    }

    [Fact]
    public void Clear_ResetsIndexAndTracks()
    {
        var playlist = new Playlist();
        playlist.Add(CreateTrack("a.mp3"));
        playlist.Add(CreateTrack("b.mp3"));

        playlist.Clear();

        Assert.Equal(-1, playlist.CurrentIndex);
        Assert.Empty(playlist.Tracks);
    }

    [Fact]
    public void TrySetCurrentIndex_OutOfRange_ReturnsFalse()
    {
        var playlist = new Playlist();
        playlist.Add(CreateTrack("a.mp3"));

        Assert.False(playlist.TrySetCurrentIndex(5));
        Assert.False(playlist.TrySetCurrentIndex(-1));
    }
}
