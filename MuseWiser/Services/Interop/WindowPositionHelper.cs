using System.Windows;
using MuseWiser.Models;
using MuseWiser.Models.Enums;

namespace MuseWiser.Services.Interop;

/// <summary>
/// <see cref="DisplaySettings"/> からウィンドウの配置位置・サイズを計算するヘルパー。
/// </summary>
public static class WindowPositionHelper
{
    /// <summary>
    /// ウィンドウに設定可能な幅・高さの最小値（ピクセル）。
    /// 設定ファイル破損等で 0 以下の値が渡された場合の防御に使用する。
    /// </summary>
    private const double MinWindowSize = 100;

    /// <summary>
    /// デザイン基準サイズ（ピクセル）。'MainWindow.xaml' の内部 Grid の基準幅・高さと一致させる。
    /// </summary>
    private const double BaseDesignSize = 540;

    /// <summary>
    /// 天気情報欄が占有するデザイン上の高さ（ピクセル）の推定固定値。
    /// 実機での見た目に応じてチューニングが必要。
    /// </summary>
    private const double WeatherSectionHeight = 184;

    /// <summary>
    /// 指定した設定をウィンドウへ適用する（Left/Top/Width/Height を設定）。
    /// </summary>
    /// <param name="window">対象ウィンドウ。</param>
    /// <param name="settings">表示設定。</param>
    /// <param name="weatherVisible">天気情報欄を表示するかどうか。</param>
    public static void Apply(Window window, DisplaySettings settings, bool weatherVisible)
    {
        var monitor = MonitorHelper.ResolveMonitor(settings.MonitorDeviceName);
        var scale = GetDpiScale(window);

        // System.Drawing の座標はデバイスピクセルのため、WPF の論理ピクセルへ変換する。
        var workingArea = new Rect(
            monitor.WorkingArea.X / scale.X,
            monitor.WorkingArea.Y / scale.Y,
            monitor.WorkingArea.Width / scale.X,
            monitor.WorkingArea.Height / scale.Y);

        var designHeight = BaseDesignSize - (weatherVisible ? 0 : WeatherSectionHeight);
        var width = Math.Max(MinWindowSize, BaseDesignSize * settings.Scale);
        var height = Math.Max(MinWindowSize, designHeight * settings.Scale);

        var (anchorX, anchorY) = GetAnchorOrigin(settings.Anchor, workingArea, width, height);

        window.Width = width;
        window.Height = height;
        window.Left = anchorX + settings.OffsetX;
        window.Top = anchorY + settings.OffsetY;
    }

    /// <summary>
    /// 指定されたアンカー位置とウィンドウサイズに基づいて、ワークエリア内の原点座標（論理ピクセル）を算出する。
    /// </summary>
    /// <param name="anchor">ウィンドウを配置する基準位置（9分割のアンカー）。</param>
    /// <param name="workingArea">配置対象モニタのワークエリア。</param>
    /// <param name="width">ウィンドウの幅。</param>
    /// <param name="height">ウィンドウの高さ。</param>
    /// <returns>ウィンドウの左上角の X・Y 座標。</returns>
    private static (double X, double Y) GetAnchorOrigin(ScreenAnchor anchor, Rect workingArea, double width, double height)
    {
        double x = anchor switch
        {
            ScreenAnchor.TopLeft or ScreenAnchor.MiddleLeft or ScreenAnchor.BottomLeft => workingArea.Left,
            ScreenAnchor.TopCenter or ScreenAnchor.MiddleCenter or ScreenAnchor.BottomCenter => workingArea.Left + ((workingArea.Width - width) / 2),
            ScreenAnchor.TopRight or ScreenAnchor.MiddleRight or ScreenAnchor.BottomRight => workingArea.Right - width,
            _ => workingArea.Left,
        };

        double y = anchor switch
        {
            ScreenAnchor.TopLeft or ScreenAnchor.TopCenter or ScreenAnchor.TopRight => workingArea.Top,
            ScreenAnchor.MiddleLeft or ScreenAnchor.MiddleCenter or ScreenAnchor.MiddleRight => workingArea.Top + ((workingArea.Height - height) / 2),
            ScreenAnchor.BottomLeft or ScreenAnchor.BottomCenter or ScreenAnchor.BottomRight => workingArea.Bottom - height,
            _ => workingArea.Top,
        };

        return (x, y);
    }

    /// <summary>
    /// 指定されたウィンドウが表示されているディスプレイの DPI スケールを取得する。
    /// コンポジションターゲットが未確定の場合は等働値（1.0）を返す。
    /// </summary>
    /// <param name="window">対象ウィンドウ。</param>
    /// <returns>X・Y 方向の DPI スケール係数。</returns>
    private static System.Windows.Point GetDpiScale(Window window)
    {
        var source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget is not null)
        {
            var matrix = source.CompositionTarget.TransformToDevice;
            return new System.Windows.Point(matrix.M11, matrix.M22);
        }

        return new System.Windows.Point(1.0, 1.0);
    }
}
