using System.Windows.Forms;
using MuseWiser.Models;

namespace MuseWiser.Services.Interop;

/// <summary>
/// 接続されているモニタの一覧取得を行うヘルパー。
/// </summary>
public static class MonitorHelper
{
    /// <summary>
    /// 現在接続されているすべてのモニタ情報を取得する。
    /// </summary>
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var screens = Screen.AllScreens;
        var result = new List<MonitorInfo>(screens.Length);

        for (var i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            var displayName = screen.Primary
                ? $"モニタ {i + 1}（プライマリ）"
                : $"モニタ {i + 1}";

            result.Add(new MonitorInfo(
                screen.DeviceName,
                displayName,
                screen.Primary,
                screen.Bounds,
                screen.WorkingArea));
        }

        return result;
    }

    /// <summary>
    /// 指定したデバイス名のモニタを取得する。見つからない場合はプライマリモニタを返す。
    /// モニタが1台も取得できない場合（取得失敗・特殊環境等）は、既定のワークエリアを持つ
    /// フォールバック用の <see cref="MonitorInfo"/> を返す。
    /// </summary>
    public static MonitorInfo ResolveMonitor(string? deviceName)
    {
        var monitors = GetMonitors();

        if (monitors.Count == 0)
        {
            return CreateFallbackMonitor();
        }

        if (!string.IsNullOrEmpty(deviceName))
        {
            var match = monitors.FirstOrDefault(m => m.DeviceName == deviceName);
            if (match is not null)
            {
                return match;
            }
        }

        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
    }

    /// <summary>
    /// モニタが1台も取得できない場合に使用する、既定のフォールバック用モニタ情報を生成する。
    /// </summary>
    /// <returns>1920x1080 の仮想領域を持つフォールバック用の <see cref="MonitorInfo"/>。</returns>
    private static MonitorInfo CreateFallbackMonitor()
    {
        var bounds = new System.Drawing.Rectangle(0, 0, 1920, 1080);
        return new MonitorInfo(string.Empty, "既定モニタ", true, bounds, bounds);
    }
}
