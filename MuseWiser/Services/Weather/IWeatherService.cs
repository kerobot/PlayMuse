namespace MuseWiser.Services.Weather;

/// <summary>
/// OpenWeather API から天気情報を取得するサービス。
/// </summary>
public interface IWeatherService
{
    /// <summary>
    /// 指定した地点の現在の天気情報を取得する。
    /// </summary>
    /// <param name="cancellationToken">処理を取り消すためのキャンセルトークン。</param>
    /// <returns>取得できた場合は <see cref="WeatherInfo"/>、失敗した場合は null。</returns>
    Task<WeatherInfo?> GetCurrentWeatherAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 画面表示用に整形された天気情報。
/// </summary>
public sealed class WeatherInfo
{
    /// <summary>
    /// 都市名。
    /// </summary>
    public string CityName { get; init; } = string.Empty;

    /// <summary>
    /// 気温（摂氏）。
    /// </summary>
    public double Temperature { get; init; }

    /// <summary>
    /// 体感温度（摂氏）。
    /// </summary>
    public double FeelsLike { get; init; }

    /// <summary>
    /// 天気の説明文。
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// 湿度（%）。
    /// </summary>
    public int Humidity { get; init; }

    /// <summary>
    /// 気圧（hPa）。
    /// </summary>
    public double Pressure { get; init; }

    /// <summary>
    /// 降水量（mm）。
    /// </summary>
    public double RainVolume { get; init; }

    /// <summary>
    /// 風速（m/s）。
    /// </summary>
    public double WindSpeed { get; init; }

    /// <summary>
    /// 風向き（方角表記）。
    /// </summary>
    public string WindDirection { get; init; } = string.Empty;

    /// <summary>
    /// 天気アイコンのコード。
    /// </summary>
    public string IconCode { get; init; } = string.Empty;
}
