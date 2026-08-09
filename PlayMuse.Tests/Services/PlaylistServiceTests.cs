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

    private static string CreateTempAudioFile(string fileName)
    {
        var path = Path.Combine(Path.GetTempPath(), fileName);
        File.WriteAllBytes(path, [0x00]);
        return path;
    }

    [Fact]
    public void SaveThenLoadPlaylist_RoundTrips_RestoresTracksAndOrder()
    {
        var plmPath = Path.Combine(Path.GetTempPath(), $"playmuse-test-{Guid.NewGuid():N}.plm");
        var trackAPath = CreateTempAudioFile($"playmuse-test-a-{Guid.NewGuid():N}.mp3");
        var trackBPath = CreateTempAudioFile($"playmuse-test-b-{Guid.NewGuid():N}.flac");

        try
        {
            var saveService = new PlaylistService();
            saveService.Add(new Track(trackAPath));
            saveService.Add(new Track(trackBPath));

            saveService.SavePlaylist(plmPath);

            var loadService = new PlaylistService();
            var result = loadService.LoadPlaylist(plmPath);

            Assert.Equal(2, result.LoadedCount);
            Assert.Empty(result.MissingFilePaths);
            Assert.Equal(2, loadService.Tracks.Count);
            Assert.Equal(trackAPath, loadService.Tracks[0].FilePath);
            Assert.Equal(trackBPath, loadService.Tracks[1].FilePath);
            Assert.Equal(0, loadService.CurrentIndex);
            Assert.Same(loadService.Tracks[0], loadService.CurrentTrack);
        }
        finally
        {
            File.Delete(plmPath);
            File.Delete(trackAPath);
            File.Delete(trackBPath);
        }
    }

    [Fact]
    public void LoadPlaylist_WithMissingTrackFile_SkipsMissingAndReportsIt()
    {
        var plmPath = Path.Combine(Path.GetTempPath(), $"playmuse-test-{Guid.NewGuid():N}.plm");
        var existingTrackPath = CreateTempAudioFile($"playmuse-test-exists-{Guid.NewGuid():N}.mp3");
        var missingTrackPath = Path.Combine(Path.GetTempPath(), $"playmuse-test-missing-{Guid.NewGuid():N}.mp3");

        try
        {
            var saveService = new PlaylistService();
            saveService.Add(new Track(existingTrackPath));
            saveService.Add(new Track(missingTrackPath));
            saveService.SavePlaylist(plmPath);

            // 保存後にファイルを削除して「見つからないトラック」を再現する。
            File.Delete(missingTrackPath);

            var loadService = new PlaylistService();
            var result = loadService.LoadPlaylist(plmPath);

            Assert.Equal(1, result.LoadedCount);
            Assert.Single(result.MissingFilePaths);
            Assert.Equal(missingTrackPath, result.MissingFilePaths[0]);
            Assert.Single(loadService.Tracks);
            Assert.Equal(existingTrackPath, loadService.Tracks[0].FilePath);
        }
        finally
        {
            File.Delete(plmPath);
            File.Delete(existingTrackPath);
        }
    }

    [Fact]
    public void SaveThenLoadPlaylist_WithEmptyPlaylist_RoundTrips_ResultsInEmptyPlaylist()
    {
        var plmPath = Path.Combine(Path.GetTempPath(), $"playmuse-test-{Guid.NewGuid():N}.plm");

        try
        {
            var saveService = new PlaylistService();
            saveService.SavePlaylist(plmPath);

            var loadService = new PlaylistService();
            var result = loadService.LoadPlaylist(plmPath);

            Assert.Equal(0, result.LoadedCount);
            Assert.Empty(result.MissingFilePaths);
            Assert.Empty(loadService.Tracks);
            Assert.Null(loadService.CurrentTrack);
            Assert.Equal(-1, loadService.CurrentIndex);
        }
        finally
        {
            File.Delete(plmPath);
        }
    }

    [Fact]
    public void LoadPlaylist_ReplacesExistingPlaylist_ResettingCurrentIndex()
    {
        var plmPath = Path.Combine(Path.GetTempPath(), $"playmuse-test-{Guid.NewGuid():N}.plm");
        var newTrackPath = CreateTempAudioFile($"playmuse-test-new-{Guid.NewGuid():N}.mp3");

        try
        {
            var saveService = new PlaylistService();
            saveService.Add(new Track(newTrackPath));
            saveService.SavePlaylist(plmPath);

            var service = new PlaylistService();
            service.Add(CreateTrack("old-a.mp3"));
            service.Add(CreateTrack("old-b.mp3"));

            var raised = false;
            service.CurrentTrackChanged += (_, _) => raised = true;

            var result = service.LoadPlaylist(plmPath);

            Assert.Equal(1, result.LoadedCount);
            Assert.Single(service.Tracks);
            Assert.Equal(newTrackPath, service.Tracks[0].FilePath);
            Assert.True(raised);
        }
        finally
        {
            File.Delete(plmPath);
            File.Delete(newTrackPath);
        }
    }
}
