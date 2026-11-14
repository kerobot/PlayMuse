namespace MuseWiser.Models;

/// <summary>
/// 設定画面でのモニタ選択用に、接続されているディスプレイの情報を表す。
/// </summary>
/// <param name="DeviceName">モニタのデバイス名（<see cref="System.Windows.Forms.Screen.DeviceName"/> 相当）。</param>
/// <param name="DisplayName">UIに表示する名称。</param>
/// <param name="IsPrimary">プライマリモニタかどうか。</param>
/// <param name="Bounds">モニタ全体の範囲（デバイスピクセル）。</param>
/// <param name="WorkingArea">タスクバー等を除いた作業領域（デバイスピクセル）。</param>
public sealed record MonitorInfo(
    string DeviceName,
    string DisplayName,
    bool IsPrimary,
    System.Drawing.Rectangle Bounds,
    System.Drawing.Rectangle WorkingArea);
