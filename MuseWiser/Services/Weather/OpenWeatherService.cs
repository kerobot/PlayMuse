using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MuseWiser.Models.Weather;
using MuseWiser.Services.Settings;

namespace MuseWiser.Services.Weather;

/// <summary>
/// OpenWeather の Current Weather Data API を呼び出して天気情報を取得するサービス。
/// API キーは表示設定（settings.json）の "WeatherApiKey" から取得する。
/// </summary>
public sealed class OpenWeatherService : IWeatherService
{
    /// <summary>
    /// OpenWeather Current Weather Data API のエンドポイント URL。
    /// </summary>
    private const string BaseUrl = "https://api.openweathermap.org/data/2.5/weather";

    /// <summary>
    /// 16方位の風向き表記。<see cref="ToCompassDirection"/> で角度をインデックス化する際に使用する。
    /// </summary>
    private static readonly string[] WindDirections16 =
    [
        "N", "NNE", "NE", "ENE",
        "E", "ESE", "SE", "SSE",
        "S", "SSW", "SW", "WSW",
        "W", "WNW", "NW", "NNW",
    ];

    /// <summary>
    /// OpenWeather API 呼び出しに使用する HTTP クライアント。
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <summary>
    /// 天気取得設定（都市・単位・言語）。
    /// </summary>
    private readonly WeatherSettings _settings;

    /// <summary>
    /// API キーなどのアプリケーション設定を取得するためのサービス。
    /// </summary>
    private readonly ISettingsService _settingsService;

    /// <summary>
    /// ログ出力用のロガー。
    /// </summary>
    private readonly ILogger<OpenWeatherService> _logger;

    /// <summary>
    /// <see cref="OpenWeatherService"/> の新しいインスタンスを初期化する。
    /// </summary>
    /// <param name="httpClient">API 呼び出しに使用する HTTP クライアント。</param>
    /// <param name="settings">天気取得設定。</param>
    /// <param name="settingsService">API キーを含むアプリケーション設定を取得するサービス。</param>
    /// <param name="logger">ログ出力に使用するロガー。</param>
    public OpenWeatherService(HttpClient httpClient, IOptions<WeatherSettings> settings, ISettingsService settingsService, ILogger<OpenWeatherService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <summary>
    /// 設定された都市の現在の天気情報を OpenWeather API から取得する。
    /// API キーが未設定、または通信・解析に失敗した場合は null を返す。
    /// </summary>
    /// <param name="cancellationToken">処理を取り消すためのキャンセルトークン。</param>
    /// <returns>取得できた場合は <see cref="WeatherInfo"/>、失敗した場合は null。</returns>
    public async Task<WeatherInfo?> GetCurrentWeatherAsync(CancellationToken cancellationToken = default)
    {
        var displaySettings = _settingsService.Load();
        var apiKey = displaySettings.WeatherApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("設定ファイルに OpenWeather APIキーが設定されていないため、天気情報を取得できません。");
            return null;
        }

        var city = string.IsNullOrWhiteSpace(displaySettings.WeatherCity) ? _settings.City : displaySettings.WeatherCity;

        var requestUri = $"{BaseUrl}?q={Uri.EscapeDataString(city)}" +
                          $"&appid={apiKey}&units={_settings.Units}&lang={_settings.Language}";

        try
        {
            var response = await _httpClient.GetFromJsonAsync<OpenWeatherResponse>(requestUri, cancellationToken);
            if (response is null)
            {
                return null;
            }

            return MapToWeatherInfo(response);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _logger.LogError(ex, "天気情報の取得に失敗しました。");
            return null;
        }
    }

    /// <summary>
    /// OpenWeather API の応答（<see cref="OpenWeatherResponse"/>）を表示用の <see cref="WeatherInfo"/> に変換する。
    /// </summary>
    /// <param name="response">OpenWeather API からの応答データ。</param>
    /// <returns>変換された天気情報。</returns>
    private static WeatherInfo MapToWeatherInfo(OpenWeatherResponse response)
    {
        var condition = response.Weather?.FirstOrDefault();

        return new WeatherInfo
        {
            CityName = response.Name ?? string.Empty,
            Temperature = response.Main?.Temp ?? 0,
            FeelsLike = response.Main?.FeelsLike ?? 0,
            Description = FormatDescription(condition?.Main, condition?.Description),
            Humidity = response.Main?.Humidity ?? 0,
            Pressure = response.Main?.Pressure ?? 0,
            RainVolume = response.Rain?.OneHour ?? 0,
            WindSpeed = response.Wind?.Speed ?? 0,
            WindDirection = ToCompassDirection(response.Wind?.Deg ?? 0),
            IconCode = condition?.Icon ?? string.Empty,
        };
    }

    /// <summary>
    /// 天気の大分類（main）と詳細説明（description）を結合し、表示用の説明文字列を生成する。
    /// </summary>
    /// <param name="main">天気の大分類。</param>
    /// <param name="description">天気の詳細説明。</param>
    /// <returns>整形された説明文字列。</returns>
    private static string FormatDescription(string? main, string? description)
    {
        if (string.IsNullOrWhiteSpace(main))
        {
            return Capitalize(description) ?? string.Empty;
        }

        return string.IsNullOrWhiteSpace(description) ? main : $"{main} · {Capitalize(description)}";
    }

    /// <summary>
    /// 文字列の先頭文字を大文字化する。
    /// </summary>
    /// <param name="value">対象の文字列。</param>
    /// <returns>先頭文字を大文字化した文字列。null または空文字列の場合はそのまま返す。</returns>
    private static string? Capitalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    /// <summary>
    /// 風向きの角度（度）を 16 方位の文字表記（N/NNE/NE など）に変換する。
    /// </summary>
    /// <param name="degrees">風向きの角度（0〜360）。</param>
    /// <returns>16 方位の風向き文字列。</returns>
    private static string ToCompassDirection(double degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        var index = (int)Math.Round(normalized / 22.5, MidpointRounding.AwayFromZero) % 16;
        return WindDirections16[index];
    }
}
