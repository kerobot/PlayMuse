namespace MuseWiser.Models;

using MuseWiser.Models.Enums;

/// <summary>
/// デスクトップ上でのウィンドウ表示位置・サイズに関する設定。
/// </summary>
public sealed class DisplaySettings
{
    /// <summary>
    /// 配置先モニタのデバイス名（<see cref="System.Windows.Forms.Screen.DeviceName"/> 相当）。
    /// 未指定の場合はプライマリモニタを使用する。
    /// </summary>
    public string? MonitorDeviceName { get; set; }

    /// <summary>
    /// モニタ内での配置アンカー。
    /// </summary>
    public ScreenAnchor Anchor { get; set; } = ScreenAnchor.TopCenter;

    /// <summary>
    /// アンカー基準位置からのXオフセット（ピクセル）。
    /// </summary>
    public double OffsetX { get; set; }

    /// <summary>
    /// アンカー基準位置からのYオフセット（ピクセル）。
    /// </summary>
    public double OffsetY { get; set; }

    /// <summary>
    /// デザイン基準サイズ（540×540）に対する表示倍率。
    /// </summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>
    /// OpenWeather API キー。未設定の場合は天気情報欄を表示しない。
    /// </summary>
    public string? WeatherApiKey { get; set; }

    /// <summary>
    /// 天気情報を取得する都市名（英語名、OpenWeather の "q" パラメータに渡す値。例: "Tokyo,JP"）。
    /// 未設定の場合は既定値（appsettings.json の設定）を使用する。
    /// </summary>
    public string? WeatherCity { get; set; } = "Tokyo,JP";

    /// <summary>
    /// 天気情報の更新間隔（分）。未指定の場合は60分とする。
    /// </summary>
    public int WeatherUpdateIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// ウィンドウの Z オーダー（最前面表示/最背面表示）。
    /// </summary>
    public WindowTopmostMode WindowTopmostMode { get; set; } = WindowTopmostMode.Topmost;

    /// <summary>
    /// OS 起動時にアプリを自動起動するかどうか。
    /// </summary>
    public bool AutoStartEnabled { get; set; }
}
