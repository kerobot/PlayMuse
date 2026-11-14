namespace MuseWiser.Models.Enums;

/// <summary>
/// 音量メーター・スペクトラム表示に使用する音声データの取得元を表す。
/// </summary>
public enum AudioSourceMode
{
    /// <summary>
    /// WASAPI 共有ループバックキャプチャ（既定）。既定の再生デバイスの出力音を取得する。
    /// </summary>
    WasapiLoopback,

    /// <summary>
    /// PlayMuse（別プロセスの音楽プレーヤー）との名前付きパイプ連携。
    /// PlayMuse が排他モードで再生している場合でも、PlayMuse 側から直接
    /// レベル・スペクトラム値を受け取ることを想定した拡張ポイント。
    /// 現時点ではスタブ実装のみを提供する。
    /// </summary>
    PlayMusePipe,

    /// <summary>
    /// PlayMuse パイプ優先・WASAPI ループバックへの自動フォールバック。
    /// PlayMuse が排他モードで再生しパイプに接続できている間はそのデータを採用し、
    /// 未接続（PlayMuse 未起動・共有モード等）の間は WASAPI ループバックへ自動的に切り替える。
    /// </summary>
    Auto,
}
