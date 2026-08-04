using PlayMuse.Core.ViewModels;

namespace PlayMuse.Tests.ViewModels;

public class MainViewModelExpandToAudioFilePathsTests
{
    [Fact]
    public void ExpandToAudioFilePaths_SingleFile_ReturnsThatFile()
    {
        var tempFile = CreateTempFile("song.mp3");
        try
        {
            var result = MainViewModel.ExpandToAudioFilePaths([tempFile]).ToList();

            Assert.Single(result);
            Assert.Equal(tempFile, result[0]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ExpandToAudioFilePaths_Folder_ReturnsSupportedFilesRecursively()
    {
        var tempDir = Directory.CreateTempSubdirectory("playmuse_test_");
        try
        {
            var subDir = tempDir.CreateSubdirectory("sub");
            var mp3Path = Path.Combine(tempDir.FullName, "a.mp3");
            var flacPath = Path.Combine(subDir.FullName, "b.flac");
            var txtPath = Path.Combine(tempDir.FullName, "readme.txt");

            File.WriteAllText(mp3Path, string.Empty);
            File.WriteAllText(flacPath, string.Empty);
            File.WriteAllText(txtPath, string.Empty);

            var result = MainViewModel.ExpandToAudioFilePaths([tempDir.FullName]).ToList();

            Assert.Contains(mp3Path, result);
            Assert.Contains(flacPath, result);
            Assert.DoesNotContain(txtPath, result);
        }
        finally
        {
            Directory.Delete(tempDir.FullName, recursive: true);
        }
    }

    [Fact]
    public void ExpandToAudioFilePaths_NonExistentPath_IsIgnored()
    {
        var result = MainViewModel.ExpandToAudioFilePaths([@"C:\NonExistent\Path\song.mp3"]).ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void ExpandToAudioFilePaths_MixedFilesAndFolder_PreservesFileThenFolderOrder()
    {
        var tempFile = CreateTempFile("standalone.mp3");
        var tempDir = Directory.CreateTempSubdirectory("playmuse_test_");
        try
        {
            var nestedPath = Path.Combine(tempDir.FullName, "nested.mp3");
            File.WriteAllText(nestedPath, string.Empty);

            var result = MainViewModel.ExpandToAudioFilePaths([tempFile, tempDir.FullName]).ToList();

            Assert.Equal(tempFile, result[0]);
            Assert.Contains(nestedPath, result);
        }
        finally
        {
            File.Delete(tempFile);
            Directory.Delete(tempDir.FullName, recursive: true);
        }
    }

    private static string CreateTempFile(string fileName)
    {
        var path = Path.Combine(Path.GetTempPath(), $"playmuse_test_{Guid.NewGuid():N}_{fileName}");
        File.WriteAllText(path, string.Empty);
        return path;
    }
}
