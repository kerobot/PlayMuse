using PlayMuse.Core.Models;
using PlayMuse.Core.Services;

namespace PlayMuse.Tests.Fakes;

/// <summary>
/// <see cref="ISettingsService"/> のテスト用フェイク。ディスクへの読み書きを行わず、メモリ上で値を保持する。
/// </summary>
public sealed class FakeSettingsService : ISettingsService
{
    public AppSettings SettingsToLoad { get; set; } = new();

    public AppSettings? SavedSettings { get; private set; }

    public int SaveCallCount { get; private set; }

    public AppSettings Load() => SettingsToLoad;

    public void Save(AppSettings settings)
    {
        SavedSettings = settings;
        SaveCallCount++;
    }
}
