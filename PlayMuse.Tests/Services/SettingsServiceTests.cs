using PlayMuse.Core.Models;
using PlayMuse.Core.Services;

namespace PlayMuse.Tests.Services;

public class SettingsServiceTests : IDisposable
{
    private readonly string tempFilePath = Path.Combine(Path.GetTempPath(), $"playmuse-settings-{Guid.NewGuid():N}.json");

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsDefaultSettings()
    {
        var sut = new SettingsService(tempFilePath);

        var settings = sut.Load();

        Assert.Equal(AudioShareMode.Shared, settings.ShareMode);
        Assert.Null(settings.OutputDeviceId);
        Assert.Equal(1.0f, settings.Volume);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsValues()
    {
        var sut = new SettingsService(tempFilePath);
        var original = new AppSettings
        {
            ShareMode = AudioShareMode.Exclusive,
            OutputDeviceId = "device-123",
            Volume = 0.42f,
        };

        sut.Save(original);
        var loaded = sut.Load();

        Assert.Equal(original.ShareMode, loaded.ShareMode);
        Assert.Equal(original.OutputDeviceId, loaded.OutputDeviceId);
        Assert.Equal(original.Volume, loaded.Volume);
    }

    [Fact]
    public void Load_WhenFileContentIsCorrupted_ReturnsDefaultSettings()
    {
        var directory = Path.GetDirectoryName(tempFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(tempFilePath, "{ this is not valid json");
        var sut = new SettingsService(tempFilePath);

        var settings = sut.Load();

        Assert.Equal(AudioShareMode.Shared, settings.ShareMode);
        Assert.Null(settings.OutputDeviceId);
        Assert.Equal(1.0f, settings.Volume);
    }

    public void Dispose()
    {
        if (File.Exists(tempFilePath))
        {
            File.Delete(tempFilePath);
        }
    }
}
