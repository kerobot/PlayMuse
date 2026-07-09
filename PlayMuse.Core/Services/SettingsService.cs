using System.Text.Json;
using PlayMuse.Core.Models;

namespace PlayMuse.Core.Services;

/// <summary>
/// <see cref="AppSettings"/>を%AppData%配下のJSONファイルへ永続化するサービス。
/// 読み込み失敗（初回起動でファイルが存在しない、JSON破損等）時は既定値を返す。
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string settingsFilePath;

    public SettingsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PlayMuse",
            "settings.json"))
    {
    }

    internal SettingsService(string settingsFilePath)
    {
        this.settingsFilePath = settingsFilePath;
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(settingsFilePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(settingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            // 破損した設定ファイルや読み取り権限エラー時は、既定値にフォールバックする。
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var directory = Path.GetDirectoryName(settingsFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(settingsFilePath, json);
        }
        catch (Exception)
        {
            // 設定保存の失敗はアプリの継続動作を妨げるべきではないため、ここで握りつぶす。
        }
    }
}
