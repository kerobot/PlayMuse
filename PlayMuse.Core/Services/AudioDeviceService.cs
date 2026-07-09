using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using PlayMuse.Core.Models;

namespace PlayMuse.Core.Services;

/// <summary>
/// <see cref="MMDeviceEnumerator"/> を用いてWASAPIのレンダーエンドポイント（出力デバイス）を列挙するサービス。
/// </summary>
public sealed class AudioDeviceService : IAudioDeviceService
{
    public IReadOnlyList<AudioDeviceInfo> GetDevices()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var defaultDevice = TryGetDefaultDevice(enumerator);

            var devices = new List<AudioDeviceInfo>();

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                {
                    var isDefault = defaultDevice is not null && device.ID == defaultDevice.ID;
                    devices.Add(new AudioDeviceInfo(device.ID, device.FriendlyName, isDefault));
                }
            }

            return devices;
        }
        catch (COMException)
        {
            // オーディオデバイスが1台も存在しない、またはオーディオサブシステムが利用できない環境を考慮する。
            return [];
        }
    }

    public AudioDeviceInfo? GetDefaultDevice()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = TryGetDefaultDevice(enumerator);

            return device is null ? null : new AudioDeviceInfo(device.ID, device.FriendlyName, true);
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static MMDevice? TryGetDefaultDevice(MMDeviceEnumerator enumerator)
    {
        try
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch (COMException)
        {
            // 既定の再生デバイスが存在しない環境（デバイス未接続等）を考慮し、nullを返す。
            return null;
        }
    }
}
