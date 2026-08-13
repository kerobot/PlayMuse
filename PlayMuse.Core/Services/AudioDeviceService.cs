using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using PlayMuse.Core.Models;

namespace PlayMuse.Core.Services;

/// <summary>
/// <see cref="MMDeviceEnumerator"/> を用いてWASAPIのレンダーエンドポイント（出力デバイス）を列挙するサービス。
/// あわせて<see cref="IMMNotificationClient"/>によりデバイスの追加/削除/切断や既定デバイスの変更を検知し、
/// <see cref="DevicesChanged"/>イベントとして通知する。
/// </summary>
public sealed class AudioDeviceService : IAudioDeviceService, IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator notificationEnumerator = new();
    private readonly ILogger<AudioDeviceService> logger;
    private readonly bool isNotificationRegistered;

    public AudioDeviceService(ILogger<AudioDeviceService>? logger = null)
    {
        this.logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AudioDeviceService>.Instance;

        try
        {
            notificationEnumerator.RegisterEndpointNotificationCallback(this);
            isNotificationRegistered = true;
        }
        catch (COMException ex)
        {
            // 通知の登録に失敗しても、デバイス列挙自体は引き続き利用できるため致命的ではない。
            this.logger.LogWarning(ex, "デバイス変更通知の登録に失敗しました。");
        }
    }

    public event EventHandler? DevicesChanged;

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

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("デバイスの状態が変化しました: {DeviceId} -> {NewState}", deviceId, newState);
        }
        RaiseDevicesChanged();
    }

    void IMMNotificationClient.OnDeviceAdded(string deviceId)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("デバイスが追加されました: {DeviceId}", deviceId);
        }
        RaiseDevicesChanged();
    }

    void IMMNotificationClient.OnDeviceRemoved(string deviceId)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("デバイスが削除されました: {DeviceId}", deviceId);
        }
        RaiseDevicesChanged();
    }

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        // 再生(Render)側の既定デバイス変更のみを対象とする。
        if (flow == DataFlow.Render)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("既定出力デバイスが変更されました: {DeviceId}", defaultDeviceId);
            }
            RaiseDevicesChanged();
        }
    }

    void IMMNotificationClient.OnPropertyValueChanged(string deviceId, PropertyKey key)
    {
        // プロパティ変更（名称等）は一覧の再構築対象外。
    }

    private void RaiseDevicesChanged()
    {
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (isNotificationRegistered)
        {
            try
            {
                notificationEnumerator.UnregisterEndpointNotificationCallback(this);
            }
            catch (COMException ex)
            {
                // 破棄時の解除失敗は無視する。
                logger.LogWarning(ex, "デバイス変更通知の解除に失敗しました。");
            }
        }

        notificationEnumerator.Dispose();
    }
}
