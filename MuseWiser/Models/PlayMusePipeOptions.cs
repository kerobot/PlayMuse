using MuseWiser.Models.Enums;
using PlayMuse.LevelMeterIpc;

namespace MuseWiser.Models;

/// <summary>
/// PlayMuse との名前付きパイプ連携に関する設定。
/// 将来的に <see cref="AudioSourceMode.PlayMusePipe"/> を実装する際に使用する拡張ポイント。
/// </summary>
public sealed class PlayMusePipeOptions
{
    /// <summary>
    /// 使用する音声データの取得元。既定は WASAPI ループバックキャプチャ。
    /// </summary>
    public AudioSourceMode Mode { get; set; } = AudioSourceMode.WasapiLoopback;

    /// <summary>
    /// PlayMuse 側が公開する名前付きパイプの名前。
    /// 実際のプロトコル・既定値は PlayMuse 側の実装に合わせて別途確定する。
    /// </summary>
    public string PipeName { get; set; } = LevelMeterIpcDefaults.PipeName;

    /// <summary>
    /// パイプへの接続を試みる間隔（ミリ秒）。PlayMuse が未起動の場合の再接続に使用する。
    /// </summary>
    public int ReconnectIntervalMilliseconds { get; set; } = 3000;
}
