using Microsoft.Win32;

namespace MuseWiser.Services.Interop;

/// <summary>
/// レジストリの自動起動設定（HKEY_CURRENT_USER\...\Run）を操作するヘルパー。
/// 管理者権限を必要としないユーザーごとの自動起動設定を使用する。
/// </summary>
public static class AutoStartHelper
{
    /// <summary>
    /// 自動起動設定を書き込むレジストリキーのパス。
    /// </summary>
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// レジストリ値の名前（アプリケーション識別用）。
    /// </summary>
    private const string ValueName = "MuseWiser";

    /// <summary>
    /// 指定した状態に応じて、自動起動のレジストリ設定を追加・削除する。
    /// </summary>
    /// <param name="enabled">true の場合は自動起動を有効化し、false の場合は無効化する。</param>
    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executablePath))
            {
                return;
            }

            key.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
