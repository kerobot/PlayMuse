using PlayMuse.Core.Models;

namespace PlayMuse.Core.Services;

/// <summary>
/// WASAPIレンダーエンドポイント（出力デバイス）の列挙を担うサービス。
/// </summary>
public interface IAudioDeviceService
{
    IReadOnlyList<AudioDeviceInfo> GetDevices();

    AudioDeviceInfo? GetDefaultDevice();
}
