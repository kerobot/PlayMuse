using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MuseWiser.Models;
using MuseWiser.Models.Enums;
using MuseWiser.Services.Interop;
using MuseWiser.Services.Settings;

namespace MuseWiser.ViewModels;

/// <summary>
/// 配置・サイズ設定画面（SettingsWindow）の ViewModel。
/// モニタ選択・アンカー選択・サイズ入力を行い、<see cref="ISettingsService"/> 経由で永続化する。
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    /// <summary>
    /// 表示設定の読み込み・保存を行うサービス。
    /// </summary>
    private readonly ISettingsService _settingsService;

    /// <summary>
    /// 選択中の配置対象モニタ。
    /// </summary>
    [ObservableProperty]
    private MonitorInfo? _selectedMonitor;

    /// <summary>
    /// 選択中のウィンドウ配置アンカー。
    /// </summary>
    [ObservableProperty]
    private ScreenAnchor _selectedAnchor;

    /// <summary>
    /// 表示値スケール（%）。
    /// </summary>
    [ObservableProperty]
    private int _scalePercent;

    /// <summary>
    /// 水平方向のオフセット。
    /// </summary>
    [ObservableProperty]
    private double _offsetX;

    /// <summary>
    /// 垂直方向のオフセット。
    /// </summary>
    [ObservableProperty]
    private double _offsetY;

    /// <summary>
    /// OpenWeather API キー。
    /// </summary>
    [ObservableProperty]
    private string? _weatherApiKey;

    /// <summary>
    /// 天気情報を取得する都市名（英語名）。
    /// </summary>
    [ObservableProperty]
    private string? _weatherCity;

    /// <summary>
    /// 天気情報の更新間隔（分）。
    /// </summary>
    [ObservableProperty]
    private int _weatherUpdateIntervalMinutes;

    /// <summary>
    /// ウィンドウを最前面表示するかどうか。
    /// </summary>
    [ObservableProperty]
    private bool _isTopmost;

    /// <summary>
    /// <see cref="IsTopmost"/> の反転値（最背面表示かどうか）。XAML のトグルボタン等で使用する。
    /// </summary>
    public bool IsBottommost
    {
        get => !IsTopmost;
        set => IsTopmost = !value;
    }

    /// <summary>
    /// <see cref="IsTopmost"/> が変更されたときに <see cref="IsBottommost"/> の変更通知を発行させる。
    /// </summary>
    /// <param name="value">変更後の <see cref="IsTopmost"/> の値。</param>
    partial void OnIsTopmostChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBottommost));
    }

    /// <summary>
    /// 自動起動が有効かどうか。
    /// </summary>
    [ObservableProperty]
    private bool _autoStartEnabled;

    /// <summary>
    /// 選択可能なモニタ一覧。
    /// </summary>
    public IReadOnlyList<MonitorInfo> Monitors { get; }

    /// <summary>
    /// 選択可能な配置アンカー一覧。
    /// </summary>
    public IReadOnlyList<ScreenAnchor> Anchors { get; } = Enum.GetValues<ScreenAnchor>();

    /// <summary>
    /// 設定が保存されたときに発生するイベント。メインウィンドウへの再適用通知に使用する。
    /// </summary>
    public event EventHandler<DisplaySettings>? SettingsSaved;

    /// <summary>
    /// 設定画面を閉じる要求が発生したときに発生するイベント。
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// <see cref="SettingsViewModel"/> の新しいインスタンスを初期化し、現在の表示設定を読み込む。
    /// </summary>
    /// <param name="settingsService">表示設定の読み込み・保存を行うサービス。</param>
    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        Monitors = MonitorHelper.GetMonitors();

        var current = _settingsService.Load();
        ApplyToFields(current);
    }

    /// <summary>
    /// 読み込んだ表示設定を各編集用プロパティに反映する。
    /// </summary>
    /// <param name="settings">読み込んだ表示設定。</param>
    private void ApplyToFields(DisplaySettings settings)
    {
        SelectedMonitor = Monitors.FirstOrDefault(m => m.DeviceName == settings.MonitorDeviceName)
            ?? Monitors.FirstOrDefault(m => m.IsPrimary)
            ?? Monitors.FirstOrDefault();
        SelectedAnchor = settings.Anchor;
        ScalePercent = (int)Math.Round(settings.Scale * 100, MidpointRounding.AwayFromZero);
        OffsetX = settings.OffsetX;
        OffsetY = settings.OffsetY;
        WeatherApiKey = settings.WeatherApiKey;
        WeatherCity = settings.WeatherCity;
        WeatherUpdateIntervalMinutes = settings.WeatherUpdateIntervalMinutes;
        IsTopmost = settings.WindowTopmostMode == WindowTopmostMode.Topmost;
        AutoStartEnabled = settings.AutoStartEnabled;
    }

    /// <summary>
    /// 編集中の値を <see cref="DisplaySettings"/> に変換して保存し、自動起動設定を適用して、
    /// <see cref="SettingsSaved"/> と <see cref="CloseRequested"/> を発行させる。
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        var settings = new DisplaySettings
        {
            MonitorDeviceName = SelectedMonitor?.DeviceName,
            Anchor = SelectedAnchor,
            Scale = (ScalePercent > 0 ? ScalePercent : 100) / 100.0,
            OffsetX = OffsetX,
            OffsetY = OffsetY,
            WeatherApiKey = WeatherApiKey,
            WeatherCity = string.IsNullOrWhiteSpace(WeatherCity) ? "Tokyo,JP" : WeatherCity,
            WeatherUpdateIntervalMinutes = WeatherUpdateIntervalMinutes > 0 ? WeatherUpdateIntervalMinutes : 60,
            WindowTopmostMode = IsTopmost ? WindowTopmostMode.Topmost : WindowTopmostMode.Bottommost,
            AutoStartEnabled = AutoStartEnabled,
        };

        _settingsService.Save(settings);
        AutoStartHelper.Apply(AutoStartEnabled);
        SettingsSaved?.Invoke(this, settings);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 保存せずに <see cref="CloseRequested"/> を発行させ、設定画面を閉じる。
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
