using PlayMuse.Core.Models;

namespace PlayMuse.Core.Services;

/// <summary>
/// PlayMuse が WASAPI 排他モードで再生している間、音量レベル・スペクトラムアナライザ・
/// 出力デバイス情報を Named Pipe 経由で MuseWiser へブロードキャストするサービス。
/// </summary>
public interface ILevelMeterBroadcastService : IDisposable
{
    /// <summary>
    /// <see cref="IAudioPlaybackService.ActualShareModeChanged"/> の監視を開始する。
    /// 既に排他モードであれば直ちにパイプサーバーを起動する。
    /// </summary>
    void Start();
}
