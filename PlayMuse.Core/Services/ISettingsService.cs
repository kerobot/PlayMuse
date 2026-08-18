using PlayMuse.Core.Models;

namespace PlayMuse.Core.Services;

/// <summary>
/// <see cref="AppSettings"/> の永続化（読み込み・保存）を担うサービス。
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// 永続化済みの設定を読み込む。存在しない場合や読み込みに失敗した場合は既定値を返す。
    /// </summary>
    AppSettings Load();

    /// <summary>
    /// 設定を永続化する。
    /// </summary>
    void Save(AppSettings settings);
}
