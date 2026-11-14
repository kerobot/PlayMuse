namespace MuseWiser.Models.Weather;

/// <summary>
/// OpenWeather API (Current Weather Data) のレスポンスをデシリアライズするための内部モデル。
/// </summary>
internal sealed class OpenWeatherResponse
{
    /// <summary>
    /// 天気状態の一覧（通常は先頭要素のみ使用する）。
    /// </summary>
    public OpenWeatherConditionItem[]? Weather { get; set; }

    /// <summary>
    /// 気温・体感温度・湿度・気圧の情報。
    /// </summary>
    public OpenWeatherMain? Main { get; set; }

    /// <summary>
    /// 風速・風向の情報。
    /// </summary>
    public OpenWeatherWind? Wind { get; set; }

    /// <summary>
    /// 降水量の情報。降水がない場合は null。
    /// </summary>
    public OpenWeatherRain? Rain { get; set; }

    /// <summary>
    /// 地点（都市）名。
    /// </summary>
    public string? Name { get; set; }
}

/// <summary>
/// 天気状態 1 件分の情報（概要・詳細説明・アイコンコード）。
/// </summary>
internal sealed class OpenWeatherConditionItem
{
    /// <summary>
    /// 天気の概要（Clear、Rain など）。
    /// </summary>
    public string? Main { get; set; }

    /// <summary>
    /// 天気の詳細説明。
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 天気アイコンコード。
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("icon")]
    public string? Icon { get; set; }
}

/// <summary>
/// 気温・体感温度・湿度・気圧の情報。
/// </summary>
internal sealed class OpenWeatherMain
{
    /// <summary>
    /// 気温（摂氏）。
    /// </summary>
    public double Temp { get; set; }

    /// <summary>
    /// 体感温度（摂氏）。
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("feels_like")]
    public double FeelsLike { get; set; }

    /// <summary>
    /// 湿度（%）。
    /// </summary>
    public int Humidity { get; set; }

    /// <summary>
    /// 気圧（hPa）。
    /// </summary>
    public double Pressure { get; set; }
}

/// <summary>
/// 風速・風向の情報。
/// </summary>
internal sealed class OpenWeatherWind
{
    /// <summary>
    /// 風速（m/s）。
    /// </summary>
    public double Speed { get; set; }

    /// <summary>
    /// 風向（度、北を 0 とした時計回り）。
    /// </summary>
    public double Deg { get; set; }
}

/// <summary>
/// 降水量の情報。
/// </summary>
internal sealed class OpenWeatherRain
{
    /// <summary>
    /// 直近 1 時間の降水量（mm）。
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("1h")]
    public double OneHour { get; set; }
}
