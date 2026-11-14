using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using MuseWiser.Models.Weather;
using MuseWiser.Services.Settings;
using MuseWiser.Services.Weather;

namespace MuseWiser.ViewModels;

/// <summary>
/// 天気情報の取得・定期更新・表示用プロパティを保持する ViewModel。
/// </summary>
public sealed partial class WeatherViewModel : ViewModelBase, IAsyncDisposable
{
    /// <summary>
    /// 天気情報を取得するサービス。
    /// </summary>
    private readonly IWeatherService _weatherService;

    /// <summary>
    /// 更新間隔などの設定を取得するサービス。
    /// </summary>
    private readonly ISettingsService _settingsService;

    /// <summary>
    /// ログ出力用のロガー。
    /// </summary>
    private readonly ILogger<WeatherViewModel> _logger;

    /// <summary>
    /// 定期更新ループの実行間隔。
    /// </summary>
    private TimeSpan _updateInterval;

    /// <summary>
    /// 現在の更新ループを停止するためのキャンセルトークンソース。
    /// </summary>
    private CancellationTokenSource _cts = new();

    /// <summary>
    /// 実行中の定期更新ループタスク。未開始の場合は null。
    /// </summary>
    private Task? _updateLoopTask;

    /// <summary>
    /// 都市名。
    /// </summary>
    [ObservableProperty]
    private string _cityName = string.Empty;

    /// <summary>
    /// 気温表示文字列（都市名付き）。
    /// </summary>
    [ObservableProperty]
    private string _temperatureText = string.Empty;

    /// <summary>
    /// 天気の説明文字列。
    /// </summary>
    [ObservableProperty]
    private string _description = string.Empty;

    /// <summary>
    /// 体感温度表示文字列。
    /// </summary>
    [ObservableProperty]
    private string _feelsLikeText = string.Empty;

    /// <summary>
    /// 湿度表示文字列。
    /// </summary>
    [ObservableProperty]
    private string _humidityText = string.Empty;

    /// <summary>
    /// 気圧表示文字列。
    /// </summary>
    [ObservableProperty]
    private string _pressureText = string.Empty;

    /// <summary>
    /// 降水量表示文字列。
    /// </summary>
    [ObservableProperty]
    private string _rainVolumeText = string.Empty;

    /// <summary>
    /// 風速表示文字列。
    /// </summary>
    [ObservableProperty]
    private string _windSpeedText = string.Empty;

    /// <summary>
    /// 風向き表示文字列。
    /// </summary>
    [ObservableProperty]
    private string _windDirectionText = string.Empty;

    /// <summary>
    /// 体感温度の値部分のみの表示文字列。
    /// </summary>
    [ObservableProperty]
    private string _feelsLikeValue = string.Empty;

    /// <summary>
    /// 湿度の値部分のみの表示文字列。
    /// </summary>
    [ObservableProperty]
    private string _humidityValue = string.Empty;

    /// <summary>
    /// 気圧の値部分のみの表示文字列。
    /// </summary>
    [ObservableProperty]
    private string _pressureValue = string.Empty;

    /// <summary>
    /// 降水量の値部分のみの表示文字列。
    /// </summary>
    [ObservableProperty]
    private string _rainVolumeValue = string.Empty;

    /// <summary>
    /// 風速の値部分のみの表示文字列。
    /// </summary>
    [ObservableProperty]
    private string _windSpeedValue = string.Empty;

    /// <summary>
    /// 風向きの値部分のみの表示文字列。
    /// </summary>
    [ObservableProperty]
    private string _windDirectionValue = string.Empty;

    /// <summary>
    /// 天気データを正常に取得できているかどうか。
    /// </summary>
    [ObservableProperty]
    private bool _hasData;

    /// <summary>
    /// 天気アイコン画像の URL。
    /// </summary>
    [ObservableProperty]
    private string _iconUrl = string.Empty;

    /// <summary>
    /// <see cref="WeatherViewModel"/> の新しいインスタンスを初期化する。
    /// </summary>
    /// <param name="weatherService">天気情報取得サービス。</param>
    /// <param name="settingsService">設定取得サービス。</param>
    /// <param name="logger">ログ出力に使用するロガー。</param>
    public WeatherViewModel(IWeatherService weatherService, ISettingsService settingsService, ILogger<WeatherViewModel> logger)
    {
        _weatherService = weatherService;
        _settingsService = settingsService;
        _logger = logger;
        _updateInterval = TimeSpan.FromMinutes(Math.Max(1, _settingsService.Load().WeatherUpdateIntervalMinutes));
    }

    /// <summary>
    /// 設定変更時などに即時で天気情報を再取得し、更新間隔も再読込して定期更新ループを再起動する。
    /// </summary>
    public void RefreshNow()
    {
        var intervalMinutes = Math.Max(1, _settingsService.Load().WeatherUpdateIntervalMinutes);
        var newInterval = TimeSpan.FromMinutes(intervalMinutes);

        if (newInterval == _updateInterval && _updateLoopTask is not null)
        {
            _ = RefreshAsync(_cts.Token);
            return;
        }

        _updateInterval = newInterval;
        RestartUpdateLoop();
    }

    /// <summary>
    /// 現在の更新ループを停止し、新しいキャンセルトークンとタスクで定期更新ループを再起動する。
    /// </summary>
    private void RestartUpdateLoop()
    {
        var previousCts = _cts;
        var previousLoopTask = _updateLoopTask;

        _cts = new CancellationTokenSource();

        _updateLoopTask = RunUpdateLoopAsync(_cts.Token);

        _ = StopPreviousLoopAsync(previousCts, previousLoopTask);
    }

    /// <summary>
    /// 以前の更新ループをキャンセルし、完了を待機していらなくなったリソースを解放する。
    /// </summary>
    /// <param name="previousCts">以前のループで使用していたキャンセルトークンソース。</param>
    /// <param name="previousLoopTask">以前のループタスク。未開始の場合は null。</param>
    private static async Task StopPreviousLoopAsync(CancellationTokenSource previousCts, Task? previousLoopTask)
    {
        previousCts.Cancel();

        if (previousLoopTask is not null)
        {
            try
            {
                await previousLoopTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        previousCts.Dispose();
    }

    /// <summary>
    /// 定期更新ループを開始する（初回は即時取得）。
    /// </summary>
    public void Start()
    {
        _updateLoopTask = RunUpdateLoopAsync(_cts.Token);
    }

    /// <summary>
    /// 初回取得を行った後、<see cref="_updateInterval"/> ごとに天気情報を取得し続ける。
    /// </summary>
    /// <param name="cancellationToken">ループを停止するためのキャンセルトークン。</param>
    private async Task RunUpdateLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_updateInterval);

        await RefreshAsync(cancellationToken);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // アプリ終了時のキャンセルは無視する。
        }
    }

    /// <summary>
    /// 天気情報をサービスから取得し、表示プロパティへ反映する。取得に失敗した場合はエラーログを出力する。
    /// </summary>
    /// <param name="cancellationToken">処理を取り消すためのキャンセルトークン。</param>
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var weather = await _weatherService.GetCurrentWeatherAsync(cancellationToken);
            if (weather is not null)
            {
                Apply(weather);
            }
            else
            {
                Clear();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "天気情報の更新中にエラーが発生しました。");
        }
    }

    /// <summary>
    /// 天気情報が取得できなかった場合に、表示を初期化する。
    /// </summary>
    private void Clear()
    {
        HasData = false;
        IconUrl = string.Empty;
    }

    /// <summary>
    /// 取得した天気情報を各表示用プロパティに反映する。
    /// </summary>
    /// <param name="weather">表示する天気情報。</param>
    private void Apply(WeatherInfo weather)
    {
        CityName = weather.CityName;
        TemperatureText = $"{weather.Temperature:F0}°C · {weather.CityName}";
        Description = weather.Description;
        FeelsLikeText = $"Feels like {weather.FeelsLike:F0}°C";
        HumidityText = $"Humidity {weather.Humidity}%";
        PressureText = $"Pressure {weather.Pressure:F0} hPa";
        RainVolumeText = $"Rain {weather.RainVolume:F1} mm";
        WindSpeedText = $"Wind {weather.WindSpeed.ToString("F2", CultureInfo.InvariantCulture)} m/s";
        WindDirectionText = $"Direction {weather.WindDirection}";
        FeelsLikeValue = $"{weather.FeelsLike:F0} °C";
        HumidityValue = $"{weather.Humidity} %";
        PressureValue = $"{weather.Pressure:F0} hPa";
        RainVolumeValue = $"{weather.RainVolume:F1} mm";
        WindSpeedValue = $"{weather.WindSpeed.ToString("F1", CultureInfo.InvariantCulture)} m/s";
        WindDirectionValue = weather.WindDirection;
        IconUrl = string.IsNullOrWhiteSpace(weather.IconCode)
            ? string.Empty
            : $"https://openweathermap.org/img/wn/{weather.IconCode}@2x.png";
        HasData = true;
    }

    /// <summary>
    /// 定期更新ループを停止し、リソースを解放する。
    /// </summary>
    /// <returns>解放処理を表す <see cref="ValueTask"/>。</returns>
    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();

        if (_updateLoopTask is not null)
        {
            try
            {
                await _updateLoopTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
    }
}
