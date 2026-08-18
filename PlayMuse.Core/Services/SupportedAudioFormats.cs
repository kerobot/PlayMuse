namespace PlayMuse.Core.Services;

/// <summary>
/// アプリが対応する音楽ファイルの拡張子を一元管理する。
/// 新しい形式に対応する際は、<see cref="FormatDefinitions"/> に追加するだけで済むようにする。
/// </summary>
public static class SupportedAudioFormats
{
    private static readonly (string Extension, string DisplayName, bool IsLossy)[] FormatDefinitions =
    [
        (".mp3", "MP3", true),
        (".flac", "FLAC", false),
        (".wav", "WAV", false),
        (".aac", "AAC", true),
        (".m4a", "M4A", true),
    ];

    private static readonly HashSet<string> Extensions = new(
        FormatDefinitions.Select(format => format.Extension),
        StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> DisplayNamesByExtension = new(
        FormatDefinitions.Select(format => new KeyValuePair<string, string>(format.Extension, format.DisplayName)),
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> LossyExtensions = new(
        FormatDefinitions.Where(format => format.IsLossy).Select(format => format.Extension),
        StringComparer.OrdinalIgnoreCase);

    public static bool IsSupported(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(extension) && Extensions.Contains(extension);
    }

    /// <summary>
    /// 指定ファイルが非可逆圧縮形式(MP3/AAC/M4Aなど、ビット深度の概念を持たない形式)かどうかを判定する。
    /// </summary>
    public static bool IsLossyFormat(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(extension) && LossyExtensions.Contains(extension);
    }

    /// <summary>
    /// 指定ファイルの拡張子に対応する表示名(例: "MP3", "M4A")を取得する。
    /// 未対応拡張子の場合は、拡張子を大文字化した文字列を返す。
    /// </summary>
    public static string GetDisplayName(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(extension) && DisplayNamesByExtension.TryGetValue(extension, out var displayName))
        {
            return displayName;
        }

        return extension?.ToUpperInvariant().TrimStart('.') ?? string.Empty;
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
