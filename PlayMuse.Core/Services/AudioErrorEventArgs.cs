namespace PlayMuse.Core.Services;

/// <summary>
/// 再生エラーの種別。ViewModel側での回復処理（自動スキップ/デバイス切替）の判断に使用する。
/// </summary>
public enum AudioErrorKind
{
    /// <summary>
    /// 上記以外の一般的な再生エラー。
    /// </summary>
    Playback,

    /// <summary>
    /// 再生対象のファイルが見つからない、または読み込みに失敗した（削除・移動・破損など）。
    /// </summary>
    TrackUnavailable,

    /// <summary>
    /// 出力デバイスが切断された、または利用できなくなった。
    /// </summary>
    DeviceDisconnected,
}

public sealed class AudioErrorEventArgs(string message, Exception? exception = null, AudioErrorKind kind = AudioErrorKind.Playback) : EventArgs
{
    public string Message { get; } = message;

    public Exception? Exception { get; } = exception;

    public AudioErrorKind Kind { get; } = kind;
}
