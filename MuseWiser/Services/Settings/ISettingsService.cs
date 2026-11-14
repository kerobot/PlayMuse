using MuseWiser.Models;

namespace MuseWiser.Services.Settings;

/// <summary>
/// アプリケーション設定（配置位置・サイズ等）の読み込み・保存を行うサービス。
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// 現在の表示設定を取得する。
    /// </summary>
    /// <returns>現在の <see cref="DisplaySettings"/>。</returns>
    DisplaySettings Load();

    /// <summary>
    /// 表示設定を保存する。
    /// </summary>
    /// <param name="settings">保存する表示設定。</param>
    void Save(DisplaySettings settings);
}
