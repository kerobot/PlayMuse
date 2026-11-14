namespace MuseWiser.ViewModels;

/// <summary>
/// メインウィンドウ全体の ViewModel。各機能の ViewModel を集約する。
/// </summary>
public sealed class MainViewModel : ViewModelBase, IAsyncDisposable
{
    /// <summary>
    /// 時計表示用 ViewModel。
    /// </summary>
    public ClockViewModel Clock { get; }

    /// <summary>
    /// 天気表示用 ViewModel。
    /// </summary>
    public WeatherViewModel Weather { get; }

    /// <summary>
    /// 音量メーター表示用 ViewModel。
    /// </summary>
    public VolumeMeterViewModel VolumeMeter { get; }

    /// <summary>
    /// 音声デバイス情報表示用 ViewModel。
    /// </summary>
    public AudioDeviceInfoViewModel AudioDevice { get; }

    /// <summary>
    /// <see cref="MainViewModel"/> の新しいインスタンスを初期化し、天気・音量メーターの取得/キャプチャを開始する。
    /// </summary>
    /// <param name="clock">時計表示用 ViewModel。</param>
    /// <param name="weather">天気表示用 ViewModel。</param>
    /// <param name="volumeMeter">音量メーター表示用 ViewModel。</param>
    /// <param name="audioDevice">音声デバイス情報表示用 ViewModel。</param>
    public MainViewModel(ClockViewModel clock, WeatherViewModel weather, VolumeMeterViewModel volumeMeter, AudioDeviceInfoViewModel audioDevice)
    {
        Clock = clock;
        Weather = weather;
        VolumeMeter = volumeMeter;
        AudioDevice = audioDevice;
        Weather.Start();
        VolumeMeter.Start();
    }

    /// <summary>
    /// 各機能の ViewModel が保持するリソースを非同期に解放する。
    /// </summary>
    /// <returns>解放処理を表す <see cref="ValueTask"/>。</returns>
    public async ValueTask DisposeAsync()
    {
        AudioDevice.Dispose();
        VolumeMeter.Dispose();
        Clock.Dispose();
        await Weather.DisposeAsync();
    }
}
