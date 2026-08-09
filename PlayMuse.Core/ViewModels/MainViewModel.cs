using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json;
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
    private string? currentPlaylistFilePath;
    private int trackUnavailableSkipCount;

    [ObservableProperty]
    private Track? currentTrack;

    [ObservableProperty]
    private Track? selectedTrack;

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

    [ObservableProperty]
    private bool isLoopEnabled;

    /// <summary>
    /// プレイリスト上でドラッグ操作中のトラック。Viewはこれを参照してドラッグ中アイテムの半透明表示を行う。
    /// </summary>
    [ObservableProperty]
    private Track? draggingTrack;

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
        this.deviceService.DevicesChanged += OnDevicesChanged;

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

        AddFiles(filePaths);
    }

    /// <summary>
    /// 現在のプレイリストをJSON形式（.plm）で指定パスへ保存する。
    /// </summary>
    [RelayCommand]
    private void SavePlaylist()
    {
        var filePath = fileDialogService.ShowSavePlaylistFileDialog();
        if (filePath is null)
        {
            return;
        }

        try
        {
            playlistService.SavePlaylist(filePath);
            currentPlaylistFilePath = filePath;
            StatusMessage = $"プレイリストを保存しました。（{Path.GetFileName(filePath)}）";
            SaveSettingsIfReady();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "プレイリストの保存に失敗しました。（アクセス権限を確認してください）";
        }
        catch (Exception)
        {
            StatusMessage = "プレイリストの保存に失敗しました。";
        }
    }

    /// <summary>
    /// プレイリストファイル（.plm）を読み込み、現在のプレイリストを置き換える。
    /// 存在しないトラックファイルはスキップされ、ステータスメッセージで通知される。
    /// </summary>
    [RelayCommand]
    private void OpenPlaylist()
    {
        var filePath = fileDialogService.ShowOpenPlaylistFileDialog();
        if (filePath is null)
        {
            return;
        }

        LoadPlaylistFromFile(filePath, isAutoLoad: false);
    }

    /// <summary>
    /// 指定パスのプレイリストファイルを読み込む。読込結果とあわせて異常系（ファイル不在、
    /// JSON形式不正、アクセス権限なし等）をクラッシュさせずステータスメッセージで通知する。
    /// isAutoLoadはアプリ起動時の自動読込用で、メッセージ文言のみ切り替える。
    /// </summary>
    private void LoadPlaylistFromFile(string filePath, bool isAutoLoad)
    {
        if (!File.Exists(filePath))
        {
            StatusMessage = isAutoLoad
                ? "前回開いていたプレイリストファイルが見つからないため、自動読み込みをスキップしました。"
                : "指定されたプレイリストファイルが見つかりません。";
            return;
        }

        try
        {
            var result = playlistService.LoadPlaylist(filePath);
            currentPlaylistFilePath = filePath;

            foreach (var track in Tracks)
            {
                _ = LoadMetadataAsync(track);
            }

            StatusMessage = result.MissingFilePaths.Count > 0
                ? $"{result.LoadedCount} 件のトラックを読み込みました。（{result.MissingFilePaths.Count} 件のファイルが見つからずスキップしました）"
                : $"{result.LoadedCount} 件のトラックを読み込みました。";

            SaveSettingsIfReady();
        }
        catch (JsonException)
        {
            StatusMessage = "プレイリストファイルの形式が不正です。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "プレイリストファイルの読み込みに失敗しました。（アクセス権限を確認してください）";
        }
        catch (Exception)
        {
            StatusMessage = "プレイリストの読み込みに失敗しました。";
        }
    }

    /// <summary>
    /// 指定されたパス群（ファイル/フォルダ混在可）をプレイリストに追加する。
    /// ドラッグ&ドロップおよび「ファイルを開く」ダイアログの両方から共通で利用される。
    /// フォルダが指定された場合は配下（サブフォルダ含む）の対応音楽ファイルをすべて追加する。
    /// 非対応拡張子のファイルはスキップし、ステータスメッセージで通知する。
    /// </summary>
    public void AddFiles(IEnumerable<string> paths)
    {
        var resolvedFilePaths = ExpandToAudioFilePaths(paths);

        var addedCount = 0;
        var skippedCount = 0;

        foreach (var filePath in resolvedFilePaths)
        {
            if (!SupportedAudioFormats.IsSupported(filePath))
            {
                skippedCount++;
                continue;
            }

            var track = new Track(filePath);
            playlistService.Add(track);
            addedCount++;

            _ = LoadMetadataAsync(track);
        }

        if (addedCount > 0 && skippedCount > 0)
        {
            StatusMessage = $"{addedCount} 件のファイルをプレイリストに追加しました。（{skippedCount} 件は非対応形式のためスキップしました）";
        }
        else if (addedCount > 0)
        {
            StatusMessage = $"{addedCount} 件のファイルをプレイリストに追加しました。";
        }
        else if (skippedCount > 0)
        {
            StatusMessage = "対応していない形式のため、ファイルを追加できませんでした。";
        }
    }

    /// <summary>
    /// 指定したトラックをプレイリストから削除する。削除対象が再生中の場合は、
    /// PlaylistService側でCurrentTrackChangedが発火し、後続トラックの再生準備が行われる。
    /// </summary>
    [RelayCommand]
    private void RemoveTrack(Track? track)
    {
        if (track is null)
        {
            return;
        }

        playlistService.Remove(track);
    }

    /// <summary>
    /// パス群を走査し、フォルダはサブフォルダを含めて配下のファイルパスへ展開する。
    /// ファイルはそのまま列挙し、順序は入力順を維持する。
    /// </summary>
    internal static IEnumerable<string> ExpandToAudioFilePaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                IEnumerable<string> filesInDirectory;
                try
                {
                    filesInDirectory = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                        .Where(SupportedAudioFormats.IsSupported)
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var file in filesInDirectory)
                {
                    yield return file;
                }
            }
            else if (File.Exists(path))
            {
                yield return path;
            }
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
        catch (Exception)
        {
            dispatcherService.Invoke(() => StatusMessage = $"'{track.FileName}' のタグ読み取りに失敗しました。");
        }
    }

    /// <summary>
    /// 再生/一時停止を切り替えるトグルコマンド。
    /// 停止中または一時停止中→再生、再生中→一時停止。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPlayPause))]
    private void PlayPause()
    {
        if (PlaybackStatus == PlaybackState.Playing)
        {
            playbackService.Pause();
        }
        else
        {
            // CurrentTrackがnullだがSelectedTrackがある場合、そのトラックをCurrentに設定
            if (CurrentTrack is null && SelectedTrack is not null)
            {
                var index = Tracks.IndexOf(SelectedTrack);
                if (index >= 0)
                {
                    playlistService.TrySetCurrentIndex(index);
                }
            }

            playbackService.Play();
        }
    }

    private bool CanPlayPause() => CurrentTrack is not null || SelectedTrack is not null;

    [ObservableProperty]
    private string playPauseButtonText = "PLAY";

    partial void OnSelectedTrackChanged(Track? value)
    {
        PlayPauseCommand.NotifyCanExecuteChanged();
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
    /// プレイリスト内で指定トラックをドラッグ＆ドロップで並べ替える。
    /// 現在再生中のトラック参照は移動後も維持される（PlaylistService/Playlist側で保証）。
    /// </summary>
    public void MoveTrack(Track track, int targetIndex)
    {
        playlistService.Move(track, targetIndex);
    }

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

    partial void OnIsLoopEnabledChanged(bool value)
    {
        playlistService.IsLoopEnabled = value;
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
            if (e == PlaybackState.Playing)
            {
                // 正常に再生が開始できたので、自動スキップの連続カウントをリセットする。
                trackUnavailableSkipCount = 0;

                // Track.Durationはメタデータの非同期読み込み完了時に更新されるため、
                // 起動直後などメタデータ読み込みが完了する前に再生を開始すると0のまま取り残されることがある。
                // 実際にデコードされた正しい長さ（playbackService.Duration）で必ず上書きし、
                // シークバーのMaximumがずれる（Position加算に伴い右端に張り付いて見える）事象を防ぐ。
                var actualDuration = playbackService.Duration;
                if (actualDuration > TimeSpan.Zero)
                {
                    Duration = actualDuration;
                }
            }

            PlaybackStatus = e;
            UpdateAudioInfo();
            UpdatePlayPauseButton();
            PlayCommand.NotifyCanExecuteChanged();
            PauseCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
            PlayPauseCommand.NotifyCanExecuteChanged();
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
        dispatcherService.Invoke(() =>
        {
            StatusMessage = e.Message;

            switch (e.Kind)
            {
                case AudioErrorKind.TrackUnavailable:
                    TrySkipUnavailableTrack();
                    break;
                case AudioErrorKind.DeviceDisconnected:
                    RecoverFromDeviceDisconnection();
                    break;
            }
        });
    }

    /// <summary>
    /// ファイル削除/移動などで現在トラックが再生不可能だった場合に、次のトラックへ自動でスキップして再生を継続する。
    /// 全トラックが連続で失敗するケースでは無限ループにならないようカウントで上限を設ける。
    /// </summary>
    private void TrySkipUnavailableTrack()
    {
        if (Tracks.Count == 0 || trackUnavailableSkipCount >= Tracks.Count)
        {
            trackUnavailableSkipCount = 0;
            StatusMessage = "再生可能なトラックが見つかりませんでした。";
            return;
        }

        trackUnavailableSkipCount++;

        if (playlistService.MoveNext())
        {
            playbackService.Play();
        }
        else
        {
            trackUnavailableSkipCount = 0;
        }
    }

    /// <summary>
    /// 出力デバイスが切断された場合に、他の利用可能なデバイスへ自動切り替えて再生を継続する。
    /// 切り替え先がない場合はユーザーにメッセージを表示して再生を停止する。
    /// </summary>
    private void RecoverFromDeviceDisconnection()
    {
        RefreshDevicesAndRecoverIfNeeded(preferResume: true);
    }

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        dispatcherService.Invoke(() => RefreshDevicesAndRecoverIfNeeded(preferResume: true));
    }

    /// <summary>
    /// デバイス一覧を再取得し、選択中のデバイスが利用不可になっていれば他のデバイスへ自動で切り替える。
    /// 利用可能なデバイスが1つもない場合は再生を停止してユーザーに通知する。
    /// </summary>
    private void RefreshDevicesAndRecoverIfNeeded(bool preferResume)
    {
        var previousSelectedId = SelectedDevice?.Id;

        Devices.Clear();

        IReadOnlyList<AudioDeviceInfo> devices;
        try
        {
            devices = deviceService.GetDevices();
        }
        catch (Exception)
        {
            StatusMessage = "再生デバイスの取得に失敗しました。";
            return;
        }

        foreach (var device in devices)
        {
            Devices.Add(device);
        }

        if (Devices.Count == 0)
        {
            StatusMessage = "利用可能な再生デバイスが見つかりませんでした。再生を停止します。";
            playbackService.Stop();
            SelectedDevice = null;
            return;
        }

        if (previousSelectedId is not null && Devices.Any(d => d.Id == previousSelectedId))
        {
            // 選択中のデバイスは引き続き利用可能。一覧のみ更新済み。
            return;
        }

        var fallback = Devices.FirstOrDefault(d => d.IsDefault) ?? Devices.First();

        if (preferResume)
        {
            StatusMessage = $"出力デバイスが切断されたため、'{fallback.Name}' に切り替えて再生を継続します。";
        }

        // SetOutputDeviceを伴うSelectedDeviceの変更により、再生中であった場合は位置を保持したまま自動で再生を継続する。
        SelectedDevice = fallback;
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
            else
            {
                // プレイリストが空になった、または削除により現在トラックが失われた場合は再生を停止する。
                playbackService.Stop();
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
        IsLoopEnabled = settings.IsLoopEnabled;
        playlistService.IsLoopEnabled = settings.IsLoopEnabled;

        if (settings.OutputDeviceId is not null)
        {
            var savedDevice = Devices.FirstOrDefault(d => d.Id == settings.OutputDeviceId);
            if (savedDevice is not null)
            {
                SelectedDevice = savedDevice;
            }
        }

        playbackService.SetShareMode(IsExclusiveMode ? AudioShareMode.Exclusive : AudioShareMode.Shared);

        if (!string.IsNullOrWhiteSpace(settings.LastPlaylistFilePath))
        {
            LoadPlaylistFromFile(settings.LastPlaylistFilePath, isAutoLoad: true);
        }
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
            IsLoopEnabled = IsLoopEnabled,
            LastPlaylistFilePath = currentPlaylistFilePath,
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

    private void UpdatePlayPauseButton()
    {
        PlayPauseButtonText = PlaybackStatus == PlaybackState.Playing ? "STOP" : "PLAY";
    }

    public void Dispose()
    {
        playbackService.StateChanged -= OnPlaybackStateChanged;
        playbackService.PlaybackCompleted -= OnPlaybackCompleted;
        playbackService.ErrorOccurred -= OnPlaybackErrorOccurred;
        playlistService.CurrentTrackChanged -= OnCurrentTrackChanged;
        playlistService.Tracks.CollectionChanged -= OnTracksCollectionChanged;
        deviceService.DevicesChanged -= OnDevicesChanged;
    }
}
