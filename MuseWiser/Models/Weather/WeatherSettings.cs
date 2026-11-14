namespace MuseWiser.Models.Weather;

/// <summary>
/// 天気情報取得に関する設定（appsettings.json の "Weather" セクションにバインドされる）。
/// </summary>
public sealed class WeatherSettings
{
    /// <summary>
    /// 天気情報を取得する都市名（OpenWeather の "q" パラメータに渡す値。例: "Tokyo,JP"）。
    /// </summary>
    public string City { get; set; } = "Tokyo,JP";

    /// <summary>
    /// 単位系（"metric" = 摂氏・m/s）。
    /// </summary>
    public string Units { get; set; } = "metric";

    /// <summary>
    /// レスポンスの言語コード。
    /// </summary>
    public string Language { get; set; } = "ja";

    /// <summary>
    /// 天気情報の更新間隔（分）。
    /// </summary>
    public int UpdateIntervalMinutes { get; set; } = 60;
}
