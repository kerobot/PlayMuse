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
}
