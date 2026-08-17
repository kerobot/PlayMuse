namespace PlayMuse.Core.Models;

public sealed class AppSettings
{
    public AudioShareMode ShareMode { get; set; } = AudioShareMode.Shared;

    public string? OutputDeviceId { get; set; }

    public float Volume { get; set; } = 1.0f;

    public bool IsLoopEnabled { get; set; }

    /// <summary>
    /// 直近に保存/読込したプレイリストファイルの絶対パス。次回起動時の自動読込に使用する。
    /// </summary>
    public string? LastPlaylistFilePath { get; set; }

    /// <summary>
    /// TRACK INFOMATION表示欄の開閉状態。
    /// </summary>
    public bool IsTrackInfoVisible { get; set; } = true;

    /// <summary>
    /// SPECTRUM ANALYZER表示欄の開閉状態。
    /// </summary>
    public bool IsSpectrumVisible { get; set; } = true;

    /// <summary>
    /// ウィンドウの左端位置（画面座標）。未保存時（初回起動）はnull。
    /// </summary>
    public double? WindowLeft { get; set; }

    /// <summary>
    /// ウィンドウの上端位置（画面座標）。未保存時（初回起動）はnull。
    /// </summary>
    public double? WindowTop { get; set; }

    /// <summary>
    /// ウィンドウの幅。未保存時（初回起動）はnull。
    /// </summary>
    public double? WindowWidth { get; set; }

    /// <summary>
    /// ウィンドウの高さ。未保存時（初回起動）はnull。
    /// </summary>
    public double? WindowHeight { get; set; }

    /// <summary>
    /// ウィンドウの表示状態（"Normal" / "Maximized"）。未保存時（初回起動）はnull。
    /// PlayMuse.CoreはUIフレームワーク非依存のため、System.Windows.WindowStateではなく文字列で保持する。
    /// </summary>
    public string? WindowState { get; set; }
}
