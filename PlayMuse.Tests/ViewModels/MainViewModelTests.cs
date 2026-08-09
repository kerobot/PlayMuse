using PlayMuse.Core.Models;
using PlayMuse.Core.Services;
using PlayMuse.Core.ViewModels;
using PlayMuse.Tests.Fakes;

namespace PlayMuse.Tests.ViewModels;

/// <summary>
/// <see cref="MainViewModel"/> のコマンド活性/非活性制御と主要な再生操作フローを検証する。
/// <see cref="IAudioPlaybackService"/> はフェイクを使用し、<see cref="PlaylistService"/> は実装をそのまま利用する。
/// </summary>
public class MainViewModelTests
{
    private sealed record Fixture(
        MainViewModel ViewModel,
        FakeAudioPlaybackService PlaybackService,
        PlaylistService PlaylistService,
        FakeAudioDeviceService DeviceService,
        FakeFileDialogService FileDialogService,
        FakeMetadataService MetadataService,
        FakeSettingsService SettingsService);

    private static Fixture CreateFixture()
    {
        var playbackService = new FakeAudioPlaybackService();
        var playlistService = new PlaylistService();
        var deviceService = new FakeAudioDeviceService();
        var fileDialogService = new FakeFileDialogService();
        var metadataService = new FakeMetadataService();
        var settingsService = new FakeSettingsService();

        var viewModel = new MainViewModel(
            playbackService,
            deviceService,
            playlistService,
            fileDialogService,
            new FakeDispatcherService(),
            metadataService,
            settingsService);

        return new Fixture(viewModel, playbackService, playlistService, deviceService, fileDialogService, metadataService, settingsService);
    }

    private static Track CreateTrack(string fileName) =>
        new(Path.Combine(Path.GetTempPath(), fileName));

    [Fact]
    public void PlayPauseCommand_WhenPlaylistEmpty_CannotExecute()
    {
        var fixture = CreateFixture();

        Assert.False(fixture.ViewModel.PlayPauseCommand.CanExecute(null));
    }

    [Fact]
    public void PlayPauseCommand_WhenCurrentTrackExists_CanExecute()
    {
        var fixture = CreateFixture();
        fixture.PlaylistService.Add(CreateTrack("a.mp3"));

        Assert.True(fixture.ViewModel.PlayPauseCommand.CanExecute(null));
    }

    [Fact]
    public void PlayPauseCommand_WhenNoCurrentTrackButSelectedTrackExists_CanExecute()
    {
        var fixture = CreateFixture();
        var track = CreateTrack("a.mp3");
        fixture.ViewModel.SelectedTrack = track;

        Assert.True(fixture.ViewModel.PlayPauseCommand.CanExecute(null));
    }

    [Fact]
    public void PlayPauseCommand_Execute_WhenStopped_StartsPlayback()
    {
        var fixture = CreateFixture();
        fixture.PlaylistService.Add(CreateTrack("a.mp3"));

        fixture.ViewModel.PlayPauseCommand.Execute(null);

        Assert.Equal(1, fixture.PlaybackService.PlayCallCount);
        Assert.Equal(PlaybackState.Playing, fixture.ViewModel.PlaybackStatus);
    }

    [Fact]
    public void PlayPauseCommand_Execute_WhenPlaying_Pauses()
    {
        var fixture = CreateFixture();
        fixture.PlaylistService.Add(CreateTrack("a.mp3"));
        fixture.ViewModel.PlayPauseCommand.Execute(null);

        fixture.ViewModel.PlayPauseCommand.Execute(null);

        Assert.Equal(1, fixture.PlaybackService.PauseCallCount);
        Assert.Equal(PlaybackState.Paused, fixture.ViewModel.PlaybackStatus);
    }

    [Fact]
    public void PlayCommand_WhenNoCurrentTrack_CannotExecute()
    {
        var fixture = CreateFixture();

        Assert.False(fixture.ViewModel.PlayCommand.CanExecute(null));
    }

    [Fact]
    public void PlayCommand_WhenAlreadyPlaying_CannotExecute()
    {
        var fixture = CreateFixture();
        fixture.PlaylistService.Add(CreateTrack("a.mp3"));
        fixture.ViewModel.PlayCommand.Execute(null);

        Assert.False(fixture.ViewModel.PlayCommand.CanExecute(null));
    }

    [Fact]
    public void PauseCommand_WhenNotPlaying_CannotExecute()
    {
        var fixture = CreateFixture();
        fixture.PlaylistService.Add(CreateTrack("a.mp3"));

        Assert.False(fixture.ViewModel.PauseCommand.CanExecute(null));
    }

    [Fact]
    public void PauseCommand_WhenPlaying_CanExecute()
    {
        var fixture = CreateFixture();
        fixture.PlaylistService.Add(CreateTrack("a.mp3"));
        fixture.ViewModel.PlayCommand.Execute(null);

        Assert.True(fixture.ViewModel.PauseCommand.CanExecute(null));
    }

    [Fact]
    public void StopCommand_WhenStopped_CannotExecute()
    {
        var fixture = CreateFixture();

        Assert.False(fixture.ViewModel.StopCommand.CanExecute(null));
    }

    [Fact]
    public void StopCommand_WhenPlaying_CanExecute()
    {
        var fixture = CreateFixture();
        fixture.PlaylistService.Add(CreateTrack("a.mp3"));
        fixture.ViewModel.PlayCommand.Execute(null);

        Assert.True(fixture.ViewModel.StopCommand.CanExecute(null));
    }

    [Fact]
    public void NextCommand_WithSingleTrack_CannotExecute()
    {
        var fixture = CreateFixture();
        fixture.PlaylistService.Add(CreateTrack("a.mp3"));

        Assert.False(fixture.ViewModel.NextCommand.CanExecute(null));
    }

    [Fact]
    public void NextCommand_WithMultipleTracksAtFirst_CanExecute()
    {
        var fixture = CreateFixture();
        fixture.PlaylistService.Add(CreateTrack("a.mp3"));
        fixture.PlaylistService.Add(CreateTrack("b.mp3"));

        Assert.True(fixture.ViewModel.NextCommand.CanExecute(null));
    }

    [Fact]
    public void NextCommand_AtLastTrack_CannotExecute()
    {
        var fixture = CreateFixture();
        fixture.PlaylistService.Add(CreateTrack("a.mp3"));
        fixture.PlaylistService.Add(CreateTrack("b.mp3"));
        fixture.PlaylistService.MoveNext();

        Assert.False(fixture.ViewModel.NextCommand.CanExecute(null));
    }

    [Fact]
    public void PreviousCommand_AtFirstTrack_CannotExecute()
    {
        var fixture = CreateFixture();
        fixture.PlaylistService.Add(CreateTrack("a.mp3"));
        fixture.PlaylistService.Add(CreateTrack("b.mp3"));

        Assert.False(fixture.ViewModel.PreviousCommand.CanExecute(null));
    }

    [Fact]
    public void PreviousCommand_AfterMovingNext_CanExecute()
    {
        var fixture = CreateFixture();
        fixture.PlaylistService.Add(CreateTrack("a.mp3"));
        fixture.PlaylistService.Add(CreateTrack("b.mp3"));
        fixture.PlaylistService.MoveNext();

        Assert.True(fixture.ViewModel.PreviousCommand.CanExecute(null));
    }

    [Fact]
    public void NextCommand_Execute_WhilePlaying_ResumesPlaybackOnNewTrack()
    {
        var fixture = CreateFixture();
        fixture.PlaylistService.Add(CreateTrack("a.mp3"));
        fixture.PlaylistService.Add(CreateTrack("b.mp3"));
        fixture.ViewModel.PlayCommand.Execute(null);
        var playCountBeforeNext = fixture.PlaybackService.PlayCallCount;

        fixture.ViewModel.NextCommand.Execute(null);

        Assert.Equal(1, fixture.PlaylistService.CurrentIndex);
        Assert.True(fixture.PlaybackService.PlayCallCount > playCountBeforeNext);
    }

    [Fact]
    public void NextCommand_Execute_WhileStopped_DoesNotStartPlayback()
    {
        var fixture = CreateFixture();
        fixture.PlaylistService.Add(CreateTrack("a.mp3"));
        fixture.PlaylistService.Add(CreateTrack("b.mp3"));

        fixture.ViewModel.NextCommand.Execute(null);

        Assert.Equal(1, fixture.PlaylistService.CurrentIndex);
        Assert.Equal(0, fixture.PlaybackService.PlayCallCount);
    }

    [Fact]
    public void PreviousCommand_Execute_MovesCurrentIndexBack()
    {
        var fixture = CreateFixture();
        fixture.PlaylistService.Add(CreateTrack("a.mp3"));
        fixture.PlaylistService.Add(CreateTrack("b.mp3"));
        fixture.PlaylistService.MoveNext();

        fixture.ViewModel.PreviousCommand.Execute(null);

        Assert.Equal(0, fixture.PlaylistService.CurrentIndex);
    }

    [Fact]
    public void AddFiles_WithSupportedFormats_AddsAllToPlaylist()
    {
        var fixture = CreateFixture();
        var mp3Path = CreateTempAudioFile("a.mp3");
        var flacPath = CreateTempAudioFile("b.flac");

        try
        {
            fixture.ViewModel.AddFiles([mp3Path, flacPath]);

            Assert.Equal(2, fixture.ViewModel.Tracks.Count);
        }
        finally
        {
            File.Delete(mp3Path);
            File.Delete(flacPath);
        }
    }

    [Fact]
    public void AddFiles_WithUnsupportedFormat_SkipsFileAndSetsStatusMessage()
    {
        var fixture = CreateFixture();
        var txtPath = CreateTempAudioFile("readme.txt");

        try
        {
            fixture.ViewModel.AddFiles([txtPath]);

            Assert.Empty(fixture.ViewModel.Tracks);
            Assert.NotNull(fixture.ViewModel.StatusMessage);
        }
        finally
        {
            File.Delete(txtPath);
        }
    }

    [Fact]
    public void AddFiles_WithMixedSupportedAndUnsupportedFormats_AddsOnlySupported()
    {
        var fixture = CreateFixture();
        var mp3Path = CreateTempAudioFile("a.mp3");
        var txtPath = CreateTempAudioFile("readme.txt");

        try
        {
            fixture.ViewModel.AddFiles([mp3Path, txtPath]);

            Assert.Single(fixture.ViewModel.Tracks);
            Assert.Equal(mp3Path, fixture.ViewModel.Tracks[0].FilePath);
        }
        finally
        {
            File.Delete(mp3Path);
            File.Delete(txtPath);
        }
    }

    private static string CreateTempAudioFile(string fileName)
    {
        var path = Path.Combine(Path.GetTempPath(), $"playmuse_test_{Guid.NewGuid():N}_{fileName}");
        File.WriteAllBytes(path, [0x00]);
        return path;
    }

    [Fact]
    public void RemoveTrackCommand_RemovesTrackFromPlaylist()
    {
        var fixture = CreateFixture();
        var track = CreateTrack("a.mp3");
        fixture.PlaylistService.Add(track);

        fixture.ViewModel.RemoveTrackCommand.Execute(track);

        Assert.Empty(fixture.ViewModel.Tracks);
    }

    [Fact]
    public void RemoveTrackCommand_WithNullTrack_DoesNothing()
    {
        var fixture = CreateFixture();
        fixture.PlaylistService.Add(CreateTrack("a.mp3"));

        fixture.ViewModel.RemoveTrackCommand.Execute(null);

        Assert.Single(fixture.ViewModel.Tracks);
    }

    [Fact]
    public void PlayTrackCommand_SetsCurrentTrackAndStartsPlayback()
    {
        var fixture = CreateFixture();
        var trackA = CreateTrack("a.mp3");
        var trackB = CreateTrack("b.mp3");
        fixture.PlaylistService.Add(trackA);
        fixture.PlaylistService.Add(trackB);

        fixture.ViewModel.PlayTrackCommand.Execute(trackB);

        Assert.Equal(1, fixture.PlaylistService.CurrentIndex);
        Assert.Equal(1, fixture.PlaybackService.PlayCallCount);
    }

    [Fact]
    public void Volume_WhenChangedAfterInitialization_UpdatesPlaybackServiceAndSavesSettings()
    {
        var fixture = CreateFixture();

        fixture.ViewModel.Volume = 0.5;

        Assert.Equal(0.5f, fixture.PlaybackService.Volume);
        Assert.Equal(1, fixture.SettingsService.SaveCallCount);
    }

    [Fact]
    public void IsLoopEnabled_WhenChanged_PropagatesToPlaylistService()
    {
        var fixture = CreateFixture();

        fixture.ViewModel.IsLoopEnabled = true;

        Assert.True(fixture.PlaylistService.IsLoopEnabled);
    }
}
