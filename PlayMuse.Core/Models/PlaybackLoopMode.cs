namespace PlayMuse.Core.Models;

/// <summary>
/// プレイリストの再生終了時の挙動を表す。
/// </summary>
public enum PlaybackLoopMode
{
    /// <summary>プレイリストの最後まで再生したら停止する。</summary>
    Off,

    /// <summary>プレイリスト全曲をループ再生する。</summary>
    RepeatAll,

    /// <summary>現在の1曲のみをループ再生する。</summary>
    RepeatOne,
}
