using PlayMuse.Core.Models;

namespace PlayMuse.Core.Services;

/// <summary>
/// WASAPIレンダーエンドポイント（出力デバイス）の列挙を担うサービス。
/// </summary>
public interface IAudioDeviceService
{
    /// <summary>
    /// 出力デバイスの追加/削除/状態変化、または既定デバイスの変更を通知するイベント。
    /// COMのイベントスレッドから発火する場合があるため、購読側でUIスレッドへのマーシャリングが必要。
    /// </summary>
    event EventHandler? DevicesChanged;

    IReadOnlyList<AudioDeviceInfo> GetDevices();

    AudioDeviceInfo? GetDefaultDevice();
}
