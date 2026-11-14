namespace MuseWiser.Services.Audio;

/// <summary>
/// 既定の再生デバイスの音量レベル・スペクトラム取得を抽象化するサービス。
/// 共有再生時は WASAPI ループバックキャプチャ、将来的には PlayMuse との
/// プロセス間通信（排他再生時の波形連携）による実装への差し替えを想定する。
/// </summary>
public interface IAudioCaptureService : IDisposable
{
    /// <summary>
    /// 音声データが更新されたときに発生するイベント。
    /// </summary>
    event EventHandler<AudioCaptureDataEventArgs>? DataAvailable;

    /// <summary>
    /// 使用中のオーディオデバイス情報（名前・サンプルレート・チャンネル数）が変化したときに発生するイベント。
    /// </summary>
    event EventHandler<AudioCaptureDeviceInfo>? DeviceChanged;

    /// <summary>
    /// キャプチャの接続状態（データ取得元への接続・切断）が変化したときに発生するイベント。
    /// WASAPI ループバックの場合は <see cref="Start"/> 呼び出しで Connected、<see cref="Stop"/> で Disconnected。
    /// PlayMuse Named Pipe 連携の場合は、パイプ接続の確立・切断に連動する。
    /// </summary>
    event EventHandler<AudioCaptureConnectionState>? ConnectionStateChanged;

    /// <summary>
    /// キャプチャを開始する。
    /// </summary>
    void Start();

    /// <summary>
    /// キャプチャを停止する。
    /// </summary>
    void Stop();
}

/// <summary>
/// 音声キャプチャで得られたレベル・スペクトラム情報。
/// </summary>
/// <param name="LeftLevel">左チャンネルのレベル（0.0〜1.0）。</param>
/// <param name="RightLevel">右チャンネルのレベル（0.0〜1.0）。</param>
/// <param name="Spectrum">周波数バンドごとの振幅（0.0〜1.0）。</param>
public sealed record AudioCaptureDataEventArgs(float LeftLevel, float RightLevel, float[] Spectrum);

/// <summary>
/// 使用中のオーディオデバイスの情報。
/// </summary>
/// <param name="DeviceName">デバイスのフレンドリ名。デバイスが見つからない場合は null。</param>
/// <param name="SampleRate">サンプルレート（Hz）。デバイスが見つからない場合は 0。</param>
/// <param name="Channels">チャンネル数。デバイスが見つからない場合は 0。</param>
public sealed record AudioCaptureDeviceInfo(string? DeviceName, int SampleRate, int Channels);

/// <summary>
/// <see cref="IAudioCaptureService.ConnectionStateChanged"/> が通知する接続状態。
/// </summary>
public enum AudioCaptureConnectionState
{
    /// <summary>キャプチャ対象（デバイスまたはパイプ）へ接続済み。</summary>
    Connected,

    /// <summary>キャプチャ対象から切断中（未接続・再接続待ち等）。</summary>
    Disconnected,
}
