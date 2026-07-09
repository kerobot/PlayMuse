namespace PlayMuse.Core.Services;

/// <summary>
/// アプリが対応する音楽ファイルの拡張子を一元管理する。
/// Phase 2でFLACに対応する際は、ここに拡張子を追加するだけで済むようにする。
/// </summary>
public static class SupportedAudioFormats
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3",
    };

    public static bool IsSupported(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(extension) && Extensions.Contains(extension);
    }

    /// <summary>
    /// <see cref="Microsoft.Win32.OpenFileDialog.Filter"/> に指定できる形式の文字列を生成する。
    /// </summary>
    public static string BuildFileDialogFilter()
    {
        var patterns = string.Join(';', Extensions.Select(ext => $"*{ext}"));
        return $"対応する音楽ファイル ({patterns})|{patterns}";
    }
}
