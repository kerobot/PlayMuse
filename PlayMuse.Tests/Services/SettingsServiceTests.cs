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
        Assert.Null(settings.WindowLeft);
        Assert.Null(settings.WindowTop);
        Assert.Null(settings.WindowWidth);
        Assert.Null(settings.WindowHeight);
        Assert.Null(settings.WindowState);
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
            WindowLeft = 100.5,
            WindowTop = 200.5,
            WindowWidth = 800,
            WindowHeight = 600,
            WindowState = "Maximized",
        };

        sut.Save(original);
        var loaded = sut.Load();

        Assert.Equal(original.ShareMode, loaded.ShareMode);
        Assert.Equal(original.OutputDeviceId, loaded.OutputDeviceId);
        Assert.Equal(original.Volume, loaded.Volume);
        Assert.Equal(original.WindowLeft, loaded.WindowLeft);
        Assert.Equal(original.WindowTop, loaded.WindowTop);
        Assert.Equal(original.WindowWidth, loaded.WindowWidth);
        Assert.Equal(original.WindowHeight, loaded.WindowHeight);
        Assert.Equal(original.WindowState, loaded.WindowState);
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
