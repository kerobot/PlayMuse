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
    private readonly IMetadataService metadataService;
    private readonly ISettingsService settingsService;
    private bool isInitializing = true;

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
    private bool isExclusiveMode;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? audioInfoText;

    public MainViewModel(
        IAudioPlaybackService playbackService,
        IAudioDeviceService deviceService,
        IPlaylistService playlistService,
        IFileDialogService fileDialogService,
        IDispatcherService dispatcherService,
        IMetadataService metadataService,
        ISettingsService settingsService)
    {
        this.playbackService = playbackService;
        this.deviceService = deviceService;
        this.playlistService = playlistService;
        this.fileDialogService = fileDialogService;
        this.dispatcherService = dispatcherService;
        this.metadataService = metadataService;
        this.settingsService = settingsService;

        this.playbackService.StateChanged += OnPlaybackStateChanged;
        this.playbackService.PlaybackCompleted += OnPlaybackCompleted;
        this.playbackService.ErrorOccurred += OnPlaybackErrorOccurred;
        this.playlistService.CurrentTrackChanged += OnCurrentTrackChanged;
        this.playlistService.Tracks.CollectionChanged += OnTracksCollectionChanged;

        LoadDevices();
        ApplyPersistedSettings();
        isInitializing = false;
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

            var track = new Track(filePath);
            playlistService.Add(track);
            addedCount++;

            _ = LoadMetadataAsync(track);
        }

        if (addedCount > 0)
        {
            StatusMessage = $"{addedCount} 件のファイルをプレイリストに追加しました。";
        }
    }

    /// <summary>
    /// タグ情報をバックグラウンドで取得し、TrackのObservableプロパティを介してUIへ反映する。
    /// タグ読み取り失敗時はMetadataService側でファイル名ベース表示を維持するため、ここでは例外を握りつぶす。
    /// </summary>
    private async Task LoadMetadataAsync(Track track)
    {
        try
        {
            await metadataService.ApplyMetadataAsync(track);
        }
        catch (Exception ex)
        {
            dispatcherService.Invoke(() => StatusMessage = $"'{track.FileName}' のタグ読み取りに失敗しました。");
            _ = ex;
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

    partial void OnVolumeChanged(double value)
    {
        playbackService.Volume = (float)value;
        SaveSettingsIfReady();
    }

    partial void OnSelectedDeviceChanged(AudioDeviceInfo? value)
    {
        if (value is not null)
        {
            playbackService.SetOutputDevice(value);
            UpdateAudioInfo();
        }

        SaveSettingsIfReady();
    }

    partial void OnIsExclusiveModeChanged(bool value)
    {
        playbackService.SetShareMode(value ? AudioShareMode.Exclusive : AudioShareMode.Shared);
        UpdateAudioInfo();
        SaveSettingsIfReady();
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
            UpdateAudioInfo();
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
            UpdateAudioInfo();

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

    /// <summary>
    /// 永続化済みの設定（出力デバイス/共有モード/音量）を読み込み、各サービスへ適用する。
    /// 初期化中はプロパティ変更通知による設定への書き戻し（無限ループや不要な即時保存）を避けるため、
    /// isInitializingフラグで<see cref="SaveSettingsIfReady"/>をガードする。
    /// </summary>
    private void ApplyPersistedSettings()
    {
        var settings = settingsService.Load();

        Volume = Math.Clamp(settings.Volume, 0f, 1f);
        IsExclusiveMode = settings.ShareMode == AudioShareMode.Exclusive;

        if (settings.OutputDeviceId is not null)
        {
            var savedDevice = Devices.FirstOrDefault(d => d.Id == settings.OutputDeviceId);
            if (savedDevice is not null)
            {
                SelectedDevice = savedDevice;
            }
        }

        playbackService.SetShareMode(IsExclusiveMode ? AudioShareMode.Exclusive : AudioShareMode.Shared);
    }

    private void SaveSettingsIfReady()
    {
        if (isInitializing)
        {
            return;
        }

        settingsService.Save(new AppSettings
        {
            ShareMode = IsExclusiveMode ? AudioShareMode.Exclusive : AudioShareMode.Shared,
            OutputDeviceId = SelectedDevice?.Id,
            Volume = (float)Volume,
        });
    }

    private void UpdateAudioInfo()
    {
        if (CurrentTrack is null || playbackService.SourceFormat is null)
        {
            AudioInfoText = null;
            return;
        }

        var sourceFormat = playbackService.SourceFormat;
        var outputFormat = playbackService.OutputFormat;
        var actualShareMode = playbackService.ActualShareMode;
        var isResampling = playbackService.IsResampling;
        var outputDevice = playbackService.OutputDevice;

        var lines = new List<string>();

        // ファイル情報
        lines.Add($"📁 ソース: {sourceFormat.SampleRate / 1000.0:0.#} kHz / {sourceFormat.BitsPerSample} bit / {sourceFormat.Channels} ch / {sourceFormat.Encoding}");

        // 出力情報
        if (outputFormat is not null)
        {
            lines.Add($"🔊 出力: {outputFormat.SampleRate / 1000.0:0.#} kHz / {outputFormat.BitsPerSample} bit / {outputFormat.Channels} ch / {outputFormat.Encoding}");
        }

        // リサンプリング状態
        if (isResampling)
        {
            lines.Add($"⚙️ リサンプリング: 有効 ({sourceFormat.SampleRate / 1000.0:0.#} kHz → {outputFormat?.SampleRate / 1000.0:0.#} kHz)");
        }
        else
        {
            lines.Add("⚙️ リサンプリング: なし");
        }

        // デバイスと共有モード
        var deviceName = outputDevice?.Name ?? "デフォルトデバイス";
        var shareModeText = actualShareMode == AudioShareMode.Exclusive ? "排他" : "共有";
        lines.Add($"🎧 デバイス: {deviceName} ({shareModeText}モード)");

        // ビットパーフェクト判定
        var isBitPerfect = !isResampling && actualShareMode == AudioShareMode.Exclusive;
        var bitPerfectText = isBitPerfect ? "✓ ビットパーフェクト再生" : "△ 非ビットパーフェクト";
        lines.Add($"💎 {bitPerfectText}");

        AudioInfoText = string.Join("\n", lines);
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
