namespace PlayMuse.Core.Services;

/// <summary>
/// アプリが対応する音楽ファイルの拡張子を一元管理する。
/// 新しい形式に対応する際は、<see cref="FormatDefinitions"/> に追加するだけで済むようにする。
/// </summary>
public static class SupportedAudioFormats
{
    private static readonly (string Extension, string DisplayName)[] FormatDefinitions =
    [
        (".mp3", "MP3"),
        (".flac", "FLAC"),
        (".wav", "WAV"),
        (".aac", "AAC"),
    ];

    private static readonly HashSet<string> Extensions = new(
        FormatDefinitions.Select(format => format.Extension),
        StringComparer.OrdinalIgnoreCase);

    public static bool IsSupported(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(extension) && Extensions.Contains(extension);
    }

    /// <summary>
    /// <see cref="Microsoft.Win32.OpenFileDialog.Filter"/> に指定できる形式の文字列を生成する。
    /// 「対応する音楽ファイル」の統合フィルタに続けて、形式ごとの個別フィルタを列挙する。
    /// </summary>
    public static string BuildFileDialogFilter()
    {
        var allPatterns = string.Join(';', FormatDefinitions.Select(format => $"*{format.Extension}"));

        var filters = new List<string>
        {
            $"対応する音楽ファイル ({allPatterns})|{allPatterns}",
        };

        filters.AddRange(FormatDefinitions.Select(format =>
            $"{format.DisplayName} ファイル (*{format.Extension})|*{format.Extension}"));

        return string.Join('|', filters);
    }
}
