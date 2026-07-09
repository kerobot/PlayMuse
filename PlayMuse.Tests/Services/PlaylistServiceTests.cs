using PlayMuse.Core.Models;
using PlayMuse.Core.Services;

namespace PlayMuse.Tests.Services;

public class PlaylistServiceTests
{
    private static Track CreateTrack(string fileName) => new(Path.Combine(Path.GetTempPath(), fileName));

    [Fact]
    public void Add_WhenEmpty_RaisesCurrentTrackChanged()
    {
        var service = new PlaylistService();
        var raised = false;
        service.CurrentTrackChanged += (_, _) => raised = true;

        service.Add(CreateTrack("a.mp3"));

        Assert.True(raised);
        Assert.NotNull(service.CurrentTrack);
    }

    [Fact]
    public void Add_WhenNotFirstTrack_DoesNotChangeCurrentTrack()
    {
        var service = new PlaylistService();
        service.Add(CreateTrack("a.mp3"));

        var raised = false;
        service.CurrentTrackChanged += (_, _) => raised = true;
        service.Add(CreateTrack("b.mp3"));

        Assert.False(raised);
    }

    [Fact]
    public void MoveNext_AtPlaylistEnd_DoesNotRaiseCurrentTrackChanged()
    {
        var service = new PlaylistService();
        service.Add(CreateTrack("a.mp3"));

        var raised = false;
        service.CurrentTrackChanged += (_, _) => raised = true;
        var moved = service.MoveNext();

        Assert.False(moved);
        Assert.False(raised);
    }

    [Fact]
    public void MoveNext_ToNextTrack_RaisesCurrentTrackChanged()
    {
        var service = new PlaylistService();
        service.Add(CreateTrack("a.mp3"));
        service.Add(CreateTrack("b.mp3"));

        var raised = false;
        service.CurrentTrackChanged += (_, _) => raised = true;
        var moved = service.MoveNext();

        Assert.True(moved);
        Assert.True(raised);
        Assert.Equal(1, service.CurrentIndex);
    }

    [Fact]
    public void MovePrevious_AtPlaylistStart_DoesNotRaiseCurrentTrackChanged()
    {
        var service = new PlaylistService();
        service.Add(CreateTrack("a.mp3"));
        service.Add(CreateTrack("b.mp3"));

        var raised = false;
        service.CurrentTrackChanged += (_, _) => raised = true;
        var moved = service.MovePrevious();

        Assert.False(moved);
        Assert.False(raised);
    }

    [Fact]
    public void Remove_NonCurrentTrack_DoesNotRaiseCurrentTrackChanged()
    {
        var service = new PlaylistService();
        var a = CreateTrack("a.mp3");
        var b = CreateTrack("b.mp3");
        service.Add(a);
        service.Add(b);

        var raised = false;
        service.CurrentTrackChanged += (_, _) => raised = true;
        service.Remove(b);

        Assert.False(raised);
        Assert.Same(a, service.CurrentTrack);
    }

    [Fact]
    public void MoveNext_AcrossMixedFormats_TransitionsRegardlessOfExtension()
    {
        // mp3/flac混在プレイリストでもフォーマットを意識せずNext/Previousが機能することを確認する。
        var service = new PlaylistService();
        var mp3Track = CreateTrack("a.mp3");
        var flacTrack = CreateTrack("b.flac");
        service.Add(mp3Track);
        service.Add(flacTrack);

        var moved = service.MoveNext();

        Assert.True(moved);
        Assert.Same(flacTrack, service.CurrentTrack);

        var movedBack = service.MovePrevious();

        Assert.True(movedBack);
        Assert.Same(mp3Track, service.CurrentTrack);
    }
}
