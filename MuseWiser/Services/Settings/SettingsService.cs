using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MuseWiser.Models;

namespace MuseWiser.Services.Settings;

/// <summary>
/// <see cref="DisplaySettings"/> を JSON ファイル（%AppData%\MuseWiser\settings.json）として
/// 読み込み・保存する <see cref="ISettingsService"/> の実装。
/// </summary>
public sealed class SettingsService : ISettingsService
{
    /// <summary>
    /// JSON のシリアライズ/デシリアライズに使用する共通オプション（インデント付き出力）。
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// 設定ファイル（settings.json）のフルパス。
    /// </summary>
    private readonly string _filePath;

    /// <summary>
    /// ログ出力用のロガー。
    /// </summary>
    private readonly ILogger<SettingsService> _logger;

    /// <summary>
    /// <see cref="SettingsService"/> の新しいインスタンスを初期化し、%AppData%\MuseWiser\settings.json を保存先として設定する。
    /// </summary>
    /// <param name="logger">ログ出力に使用するロガー。</param>
    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MuseWiser");

        _filePath = Path.Combine(folder, "settings.json");
    }

    /// <summary>
    /// 設定ファイルから表示設定を読み込む。ファイルが存在しない場合や読み込みに失敗した場合は、既定値の
    /// <see cref="DisplaySettings"/> を返す。
    /// </summary>
    /// <returns>読み込んだ、または既定の表示設定。</returns>
    public DisplaySettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<DisplaySettings>(json, SerializerOptions);
                if (settings is not null)
                {
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 読み込みに失敗した場合は既定値を使用する。
            _logger.LogWarning(ex, "表示設定の読み込みに失敗したため、既定値を使用します。");
        }

        return new DisplaySettings();
    }

    /// <summary>
    /// 表示設定を JSON として設定ファイルに保存する。保存先フォルダが存在しない場合は作成する。
    /// 保存に失敗した場合はエラーログを出力し、例外は投げない。
    /// </summary>
    /// <param name="settings">保存する表示設定。</param>
    public void Save(DisplaySettings settings)
    {
        try
        {
            var folder = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var json = JsonSerializer.Serialize(settings, SerializerOptions);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "表示設定の保存に失敗しました。");
        }
    }
}
