using CommunityToolkit.Mvvm.ComponentModel;
using MuseWiser.Services.Audio;

namespace MuseWiser.ViewModels;

/// <summary>
/// 既定の再生デバイスの名前・サンプルレート・チャンネル数を表示するための ViewModel。
/// <see cref="IAudioCaptureService.DeviceChanged"/> を購読し、既定デバイスの切り替えに追従する。
/// </summary>
public sealed partial class AudioDeviceInfoViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// デバイス変更通知を受信する対象の音声キャプチャサービス。
    /// </summary>
    private readonly IAudioCaptureService _captureService;

    /// <summary>
    /// 既定デバイスが見つかっているかどうか。
    /// </summary>
    [ObservableProperty]
    private bool _hasDevice;

    /// <summary>
    /// 既定デバイスのフレンドリ名。
    /// </summary>
    [ObservableProperty]
    private string _deviceName = string.Empty;

    /// <summary>
    /// 画面表示用のデバイス情報文字列。
    /// </summary>
    [ObservableProperty]
    private string _displayText = "既定の再生デバイスが見つかりません";

    /// <summary>
    /// <see cref="AudioDeviceInfoViewModel"/> の新しいインスタンスを初期化し、デバイス変更通知の購読を開始する。
    /// </summary>
    /// <param name="captureService">音声キャプチャサービス。</param>
    public AudioDeviceInfoViewModel(IAudioCaptureService captureService)
    {
        _captureService = captureService;
        _captureService.DeviceChanged += OnDeviceChanged;
    }

    /// <summary>
    /// デバイス情報が変化したときに呼び出され、表示プロパティを更新する。
    /// </summary>
    /// <param name="sender">イベント送信元。</param>
    /// <param name="info">更新後のデバイス情報。</param>
    private void OnDeviceChanged(object? sender, AudioCaptureDeviceInfo info)
    {
        if (string.IsNullOrEmpty(info.DeviceName))
        {
            HasDevice = false;
            DeviceName = string.Empty;
            DisplayText = "既定の再生デバイスが見つかりません";
            return;
        }

        HasDevice = true;
        DeviceName = info.DeviceName;
        DisplayText = $"{info.DeviceName}（{FormatSampleRate(info.SampleRate)} {FormatChannels(info.Channels)}）";
    }

    /// <summary>
    /// サンプルレート（Hz）を KHz 表記の文字列に整形する。
    /// </summary>
    /// <param name="sampleRate">サンプルレート（Hz）。</param>
    /// <returns>「XX.XKHz」形式の文字列。</returns>
    private static string FormatSampleRate(int sampleRate)
    {
        var kiloHertz = sampleRate / 1000.0;
        return $"{kiloHertz:0.0}KHz";
    }

    /// <summary>
    /// チャンネル数を表示用の文字列（Mono/Stereo/Multi）に変換する。
    /// </summary>
    /// <param name="channels">チャンネル数。</param>
    /// <returns>チャンネル数に対応する表示文字列。</returns>
    private static string FormatChannels(int channels) => channels switch
    {
        1 => "Mono",
        2 => "Stereo",
        _ => "Multi",
    };

    /// <summary>
    /// デバイス変更通知の購読を解除する。
    /// </summary>
    public void Dispose()
    {
        _captureService.DeviceChanged -= OnDeviceChanged;
    }
}
