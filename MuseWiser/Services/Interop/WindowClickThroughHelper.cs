using System.Runtime.InteropServices;

namespace MuseWiser.Services.Interop;

/// <summary>
/// ウィンドウをクリックスルー（他ウィンドウの操作を妨げない）にするための Win32 相互運用ヘルパー。
/// </summary>
public static class WindowClickThroughHelper
{
    /// <summary>
    /// <c>GetWindowLong</c>/<c>SetWindowLong</c> で指定する拡張ウィンドウスタイルを取得するインデックス。
    /// </summary>
    private const int GWL_EXSTYLE = -20;

    /// <summary>
    /// マウスメッセージを透過させる（クリックスルー）ウィンドウ拡張スタイル。
    /// </summary>
    private const int WS_EX_TRANSPARENT = 0x00000020;

    /// <summary>
    /// レイヤードウィンドウとして扱うための拡張スタイル（WS_EX_TRANSPARENT と並用する必要がある）。
    /// </summary>
    private const int WS_EX_LAYERED = 0x00080000;

    /// <summary>
    /// Alt + Tab の切り替え候補から除外するためのツールウィンドウ拡張スタイル。
    /// </summary>
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>
    /// 32bit プロセス用の <c>GetWindowLong</c> 呼び出し。
    /// </summary>
    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    /// <summary>
    /// 64bit プロセス用の <c>GetWindowLongPtr</c> 呼び出し。
    /// </summary>
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    /// <summary>
    /// 32bit プロセス用の <c>SetWindowLong</c> 呼び出し。
    /// </summary>
    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>
    /// 64bit プロセス用の <c>SetWindowLongPtr</c> 呼び出し。
    /// </summary>
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    /// <summary>
    /// プロセスのビット数（32/64bit）に応じて適切な API を呼び分け、指定ウィンドウの現在の拡張スタイル値を取得する。
    /// </summary>
    /// <param name="hWnd">対象ウィンドウのハンドル。</param>
    /// <param name="nIndex">取得するウィンドウ属性のインデックス（例: <see cref="GWL_EXSTYLE"/>）。</param>
    /// <returns>現在のウィンドウ属性値。</returns>
    private static long GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return Environment.Is64BitProcess
            ? GetWindowLongPtr64(hWnd, nIndex).ToInt64()
            : GetWindowLong32(hWnd, nIndex);
    }

    /// <summary>
    /// プロセスのビット数（32/64bit）に応じて適切な API を呼び分け、指定ウィンドウの拡張スタイル値を設定する。
    /// </summary>
    /// <param name="hWnd">対象ウィンドウのハンドル。</param>
    /// <param name="nIndex">設定するウィンドウ属性のインデックス（例: <see cref="GWL_EXSTYLE"/>）。</param>
    /// <param name="dwNewLong">設定する新しい値。</param>
    private static void SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (Environment.Is64BitProcess)
        {
            SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
        }
        else
        {
            SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32());
        }
    }

    /// <summary>
    /// 指定したウィンドウハンドルに対し、クリック・マウス操作を透過させる拡張スタイルを設定する。
    /// 設定後、このウィンドウはマウス操作を受け付けず、背面のウィンドウを直接操作できるようになる。
    /// </summary>
    public static void MakeClickThrough(IntPtr windowHandle)
    {
        var extendedStyle = GetWindowLongPtr(windowHandle, GWL_EXSTYLE);
        var newStyle = extendedStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT;
        SetWindowLongPtr(windowHandle, GWL_EXSTYLE, new IntPtr(newStyle));
    }

    /// <summary>
    /// 指定したウィンドウハンドルを Alt + Tab の切り替え候補から除外する拡張スタイルを設定する。
    /// </summary>
    public static void ExcludeFromAltTab(IntPtr windowHandle)
    {
        var extendedStyle = GetWindowLongPtr(windowHandle, GWL_EXSTYLE);
        var newStyle = extendedStyle | WS_EX_TOOLWINDOW;
        SetWindowLongPtr(windowHandle, GWL_EXSTYLE, new IntPtr(newStyle));
    }
}
