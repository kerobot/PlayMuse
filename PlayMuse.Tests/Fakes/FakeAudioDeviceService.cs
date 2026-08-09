using PlayMuse.Core.Models;
using PlayMuse.Core.Services;

namespace PlayMuse.Tests.Fakes;

/// <summary>
/// <see cref="IAudioDeviceService"/> のテスト用フェイク。返却するデバイス一覧をテストから自由に設定できる。
/// </summary>
public sealed class FakeAudioDeviceService : IAudioDeviceService
{
    public List<AudioDeviceInfo> DevicesToReturn { get; set; } = [];

    public event EventHandler? DevicesChanged;

    public IReadOnlyList<AudioDeviceInfo> GetDevices() => DevicesToReturn;

    public AudioDeviceInfo? GetDefaultDevice() => DevicesToReturn.FirstOrDefault(d => d.IsDefault);

    public void RaiseDevicesChanged() => DevicesChanged?.Invoke(this, EventArgs.Empty);
}
