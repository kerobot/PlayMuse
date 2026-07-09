using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlayMuse.Core.Models;
using PlayMuse.Core.Services;

namespace PlayMuse.Core.ViewModels;

/// <summary>
/// MainWindowのDataContextとなるルートViewModel。
/// 再生操作(Open/Play/Pause/Stop/Next/Previous)、シーク、音量、出力デバイス選択を提供する。
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IAudioPlaybackService playbackService;
    private readonly IAudioDeviceService deviceService;
    private readonly IPlaylistService playlistService;
    private readonly IFileDialogService fileDialogService;
    private readonly IDispatcherService dispatcherService;

    [ObservableProperty]
    private Track? currentTrack;

    [ObservableProperty]
    private PlaybackState playbackStatus = PlaybackState.Stopped;

    [ObservableProperty]
    private TimeSpan position;

    [ObservableProperty]
    private TimeSpan duration;

    [ObservableProperty]
    private double volume = 1.0;

    [ObservableProperty]
    private AudioDeviceInfo? selectedDevice;

    [ObservableProperty]
    private string? statusMessage;

    public MainViewModel(
        IAudioPlaybackService playbackService,
        IAudioDeviceService deviceService,
        IPlaylistService playlistService,
        IFileDialogService fileDialogService,
        IDispatcherService dispatcherService)
    {
        this.playbackService = playbackService;
        this.deviceService = deviceService;
        this.playlistService = playlistService;
        this.fileDialogService = fileDialogService;
        this.dispatcherService = dispatcherService;

        this.playbackService.StateChanged += OnPlaybackStateChanged;
        this.playbackService.PlaybackCompleted += OnPlaybackCompleted;
        this.playbackService.ErrorOccurred += OnPlaybackErrorOccurred;
        this.playlistService.CurrentTrackChanged += OnCurrentTrackChanged;
        this.playlistService.Tracks.CollectionChanged += OnTracksCollectionChanged;

        LoadDevices();
    }

    public ObservableCollection<Track> Tracks => playlistService.Tracks;

    public ObservableCollection<AudioDeviceInfo> Devices { get; } = [];

    /// <summary>
    /// 位置スライダーのドラッグ中は再生位置ポーリングによる上書きを止めるためのフラグ。View側から設定される。
    /// </summary>
    public bool IsSeeking { get; set; }

    [RelayCommand]
    private void OpenFiles()
    {
        var filePaths = fileDialogService.ShowOpenAudioFilesDialog();
        if (filePaths.Count == 0)
        {
            return;
        }

        var addedCount = 0;
        foreach (var filePath in filePaths)
        {
            if (!SupportedAudioFormats.IsSupported(filePath))
            {
                StatusMessage = $"'{Path.GetFileName(filePath)}' は対応していない形式のため、スキップしました。";
                continue;
            }

            playlistService.Add(new Track(filePath));
            addedCount++;
        }

        if (addedCount > 0)
        {
            StatusMessage = $"{addedCount} 件のファイルをプレイリストに追加しました。";
        }
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play() => playbackService.Play();

    private bool CanPlay() => CurrentTrack is not null && PlaybackStatus != PlaybackState.Playing;

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause() => playbackService.Pause();

    private bool CanPause() => PlaybackStatus == PlaybackState.Playing;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => playbackService.Stop();

    private bool CanStop() => PlaybackStatus != PlaybackState.Stopped;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        var wasPlaying = PlaybackStatus == PlaybackState.Playing;
        if (playlistService.MoveNext() && wasPlaying)
        {
            playbackService.Play();
        }
    }

    private bool CanGoNext() => playlistService.CurrentIndex >= 0 && playlistService.CurrentIndex < Tracks.Count - 1;

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void Previous()
    {
        var wasPlaying = PlaybackStatus == PlaybackState.Playing;
        if (playlistService.MovePrevious() && wasPlaying)
        {
            playbackService.Play();
        }
    }

    private bool CanGoPrevious() => playlistService.CurrentIndex > 0;

    /// <summary>
    /// プレイリスト上のトラックをダブルクリックした際に、そのトラックへ即座に切り替えて再生する。
    /// </summary>
    [RelayCommand]
    private void PlayTrack(Track? track)
    {
        if (track is null)
        {
            return;
        }

        var index = Tracks.IndexOf(track);
        if (index < 0)
        {
            return;
        }

        playlistService.TrySetCurrentIndex(index);
        playbackService.Play();
    }

    partial void OnVolumeChanged(double value) => playbackService.Volume = (float)value;

    partial void OnSelectedDeviceChanged(AudioDeviceInfo? value)
    {
        if (value is not null)
        {
            playbackService.SetOutputDevice(value);
        }
    }

    partial void OnPositionChanged(TimeSpan value)
    {
        if (IsSeeking)
        {
            playbackService.Position = value;
        }
    }

    /// <summary>
    /// ViewのDispatcherTimer（UIスレッド上で動作）から一定間隔で呼び出され、再生位置表示を更新する。
    /// </summary>
    public void RefreshPosition()
    {
        if (IsSeeking)
        {
            return;
        }

        Position = playbackService.Position;
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackState e)
    {
        // NAudioのコールバックはUIスレッド以外から発火し得るため、Dispatcherへマーシャリングする。
        dispatcherService.Invoke(() =>
        {
            PlaybackStatus = e;
            PlayCommand.NotifyCanExecuteChanged();
            PauseCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
        });
    }

    private void OnPlaybackCompleted(object? sender, EventArgs e)
    {
        dispatcherService.Invoke(() =>
        {
            if (playlistService.MoveNext())
            {
                playbackService.Play();
            }
        });
    }

    private void OnPlaybackErrorOccurred(object? sender, AudioErrorEventArgs e)
    {
        dispatcherService.Invoke(() => StatusMessage = e.Message);
    }

    private void OnCurrentTrackChanged(object? sender, EventArgs e)
    {
        dispatcherService.Invoke(() =>
        {
            CurrentTrack = playlistService.CurrentTrack;
            Position = TimeSpan.Zero;

            if (CurrentTrack is not null)
            {
                playbackService.Load(CurrentTrack);
            }

            Duration = CurrentTrack?.Duration ?? TimeSpan.Zero;

            PlayCommand.NotifyCanExecuteChanged();
            NextCommand.NotifyCanExecuteChanged();
            PreviousCommand.NotifyCanExecuteChanged();
        });
    }

    private void OnTracksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        dispatcherService.Invoke(() =>
        {
            NextCommand.NotifyCanExecuteChanged();
            PreviousCommand.NotifyCanExecuteChanged();
        });
    }

    private void LoadDevices()
    {
        Devices.Clear();

        try
        {
            foreach (var device in deviceService.GetDevices())
            {
                Devices.Add(device);
            }
        }
        catch (Exception)
        {
            StatusMessage = "再生デバイスの取得に失敗しました。";
            return;
        }

        if (Devices.Count == 0)
        {
            StatusMessage = "利用可能な再生デバイスが見つかりませんでした。";
        }

        SelectedDevice = Devices.FirstOrDefault(d => d.IsDefault) ?? Devices.FirstOrDefault();
    }

    public void Dispose()
    {
        playbackService.StateChanged -= OnPlaybackStateChanged;
        playbackService.PlaybackCompleted -= OnPlaybackCompleted;
        playbackService.ErrorOccurred -= OnPlaybackErrorOccurred;
        playlistService.CurrentTrackChanged -= OnCurrentTrackChanged;
        playlistService.Tracks.CollectionChanged -= OnTracksCollectionChanged;
    }
}
