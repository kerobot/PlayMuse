using PlayMuse.Core.Services;

namespace PlayMuse.Tests.Services;

public class SupportedAudioFormatsTests
{
    [Theory]
    [InlineData("song.mp3")]
    [InlineData("song.MP3")]
    [InlineData(@"C:\Music\song.Mp3")]
    public void IsSupported_Mp3Extension_ReturnsTrue(string filePath)
    {
        Assert.True(SupportedAudioFormats.IsSupported(filePath));
    }

    [Theory]
    [InlineData("song.flac")]
    [InlineData("song.FLAC")]
    [InlineData(@"C:\Music\song.Flac")]
    public void IsSupported_FlacExtension_ReturnsTrue(string filePath)
    {
        Assert.True(SupportedAudioFormats.IsSupported(filePath));
    }

    [Theory]
    [InlineData("song.wav")]
    [InlineData("song")]
    [InlineData("song.txt")]
    public void IsSupported_UnsupportedExtension_ReturnsFalse(string filePath)
    {
        Assert.False(SupportedAudioFormats.IsSupported(filePath));
    }

    [Fact]
    public void BuildFileDialogFilter_ContainsMp3AndFlacPatterns()
    {
        var filter = SupportedAudioFormats.BuildFileDialogFilter();

        Assert.Contains("*.mp3", filter);
        Assert.Contains("*.flac", filter);
    }
}
