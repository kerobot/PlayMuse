using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using PlayMuse.Core.Models;

namespace PlayMuse.Core.Services;

/// <summary>
/// <see cref="MMDeviceEnumerator"/> を用いてWASAPIのレンダーエンドポイント（出力デバイス）を列挙するサービス。
/// あわせて<see cref="MMDeviceNotificationClient"/>（<see cref="MMDeviceEnumerator.CreateNotificationClient"/>）
/// のイベントによりデバイスの追加/削除/切断や既定デバイスの変更を検知し、<see cref="DevicesChanged"/>イベントとして通知する。
/// </summary>
public sealed class AudioDeviceService : IAudioDeviceService, IDisposable
{
    private readonly MMDeviceEnumerator notificationEnumerator = new();
    private readonly ILogger<AudioDeviceService> logger;
    private readonly MMDeviceNotificationClient? notificationClient;

    public AudioDeviceService(ILogger<AudioDeviceService>? logger = null)
    {
        this.logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AudioDeviceService>.Instance;

        try
        {
            notificationClient = notificationEnumerator.CreateNotificationClient();
            notificationClient.DeviceStateChanged += OnDeviceStateChanged;
            notificationClient.DeviceAdded += OnDeviceAdded;
            notificationClient.DeviceRemoved += OnDeviceRemoved;
            notificationClient.DefaultDeviceChanged += OnDefaultDeviceChanged;
            notificationClient.PropertyValueChanged += OnPropertyValueChanged;
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

    private void OnDeviceStateChanged(object? sender, DeviceStateChangedEventArgs e)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("デバイスの状態が変化しました: {DeviceId} -> {NewState}", e.DeviceId, e.NewState);
        }
        RaiseDevicesChanged();
    }

    private void OnDeviceAdded(object? sender, DeviceNotificationEventArgs e)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("デバイスが追加されました: {DeviceId}", e.DeviceId);
        }
        RaiseDevicesChanged();
    }

    private void OnDeviceRemoved(object? sender, DeviceNotificationEventArgs e)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("デバイスが削除されました: {DeviceId}", e.DeviceId);
        }
        RaiseDevicesChanged();
    }

    private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
    {
        // 再生(Render)側の既定デバイス変更のみを対象とする。
        if (e.Flow == DataFlow.Render)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("既定出力デバイスが変更されました: {DeviceId}", e.DeviceId);
            }
            RaiseDevicesChanged();
        }
    }

    private void OnPropertyValueChanged(object? sender, DevicePropertyChangedEventArgs e)
    {
        // プロパティ変更（名称等）は一覧の再構築対象外。
    }

    private void RaiseDevicesChanged()
    {
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (notificationClient is not null)
        {
            notificationClient.DeviceStateChanged -= OnDeviceStateChanged;
            notificationClient.DeviceAdded -= OnDeviceAdded;
            notificationClient.DeviceRemoved -= OnDeviceRemoved;
            notificationClient.DefaultDeviceChanged -= OnDefaultDeviceChanged;
            notificationClient.PropertyValueChanged -= OnPropertyValueChanged;
            notificationClient.Dispose();
        }

        notificationEnumerator.Dispose();
    }
}
