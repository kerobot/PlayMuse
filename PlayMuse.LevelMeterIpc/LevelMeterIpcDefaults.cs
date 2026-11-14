namespace PlayMuse.LevelMeterIpc;

/// <summary>
/// PlayMuse と MuseWiser 間のレベルメーター／スペクトラムアナライザ連携（Named Pipe IPC）で
/// 共有される既定値・定数。
/// </summary>
public static class LevelMeterIpcDefaults
{
    /// <summary>
    /// PlayMuse が排他モード再生時に公開する既定の名前付きパイプ名。
    /// </summary>
    public const string PipeName = "PlayMuse.LevelMeter";

    /// <summary>
    /// スペクトラムアナライザのバンド数（PlayMuse / MuseWiser 双方の16バンドLED表示に合わせる）。
    /// </summary>
    public const int SpectrumBandCount = 16;
}
