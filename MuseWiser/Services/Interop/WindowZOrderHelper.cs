using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MuseWiser.Models;
using MuseWiser.Models.Enums;

namespace MuseWiser.Services.Interop;

/// <summary>
/// <see cref="DisplaySettings"/> の <see cref="WindowTopmostMode"/> に応じて、
/// ウィンドウの Z オーダー（最前面表示/最背面表示）を切り替える Win32 相互運用ヘルパー。
/// WPF の <see cref="Window.Topmost"/> は最前面表示のみをサポートするため、
/// 最背面表示は Win32 の SetWindowPos（HWND_BOTTOM）を使用して実現する。
/// </summary>
public static class WindowZOrderHelper
{
    /// <summary>
    /// <c>SetWindowPos</c> の <c>hWndInsertAfter</c> に指定する、最背面挿入位置を表す特殊ハンドル値。
    /// </summary>
    private static readonly IntPtr HwndBottom = new(1);

    /// <summary>
    /// ウィンドウの位置を変更しないことを示すフラグ。
    /// </summary>
    private const uint SwpNoMove = 0x0002;

    /// <summary>
    /// ウィンドウのサイズを変更しないことを示すフラグ。
    /// </summary>
    private const uint SwpNoSize = 0x0001;

    /// <summary>
    /// ウィンドウをアクティブ化しないことを示すフラグ。
    /// </summary>
    private const uint SwpNoActivate = 0x0010;

    /// <summary>
    /// 指定したウィンドウの Z オーダー・位置・サイズを変更する Win32 API。
    /// </summary>
    /// <param name="hWnd">対象ウィンドウのハンドル。</param>
    /// <param name="hWndInsertAfter">挿入位置の基準となるウィンドウハンドル（特殊値含む）。</param>
    /// <param name="x">新しい X 座標（<see cref="SwpNoMove"/> 指定時は無視）。</param>
    /// <param name="y">新しい Y 座標（<see cref="SwpNoMove"/> 指定時は無視）。</param>
    /// <param name="cx">新しい幅（<see cref="SwpNoSize"/> 指定時は無視）。</param>
    /// <param name="cy">新しい高さ（<see cref="SwpNoSize"/> 指定時は無視）。</param>
    /// <param name="uFlags">動作を制御するフラグの組み合わせ。</param>
    /// <returns>成功した場合は true。</returns>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    /// <summary>
    /// 指定したウィンドウに対し、設定に応じた Z オーダーを適用する。
    /// </summary>
    /// <param name="window">対象ウィンドウ。</param>
    /// <param name="settings">表示設定。</param>
    public static void Apply(Window window, DisplaySettings settings)
    {
        if (settings.WindowTopmostMode == WindowTopmostMode.Topmost)
        {
            window.Topmost = true;
            return;
        }

        window.Topmost = false;

        var windowHandle = new WindowInteropHelper(window).Handle;
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(windowHandle, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }
}
