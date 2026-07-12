using NAudio.Wave;
using PlayMuse.Core.Models;
using PlaybackState = PlayMuse.Core.Models.PlaybackState;

namespace PlayMuse.Core.Services;

/// <summary>
/// 単一トラックの再生（Play/Pause/Stop/Seek/Volume）と出力デバイス・共有モードの切替を担うサービス。
/// </summary>
public interface IAudioPlaybackService : IDisposable
{
    PlaybackState State { get; }

    TimeSpan Position { get; set; }

    TimeSpan Duration { get; }

    float Volume { get; set; }

    AudioDeviceInfo? OutputDevice { get; }

    /// <summary>
    /// ユーザー設定として要求されている共有モード（<see cref="SetShareMode"/>で指定した値）。
    /// </summary>
    AudioShareMode ShareMode { get; }

    /// <summary>
    /// 現在の出力に実際に適用されている共有モード。
    /// 排他モードが要求されていても、対象トラックのフォーマットがデバイスの排他モードに非対応、
    /// または他アプリの排他使用中等で初期化に失敗した場合は<see cref="AudioShareMode.Shared"/>にフォールバックされる。
    /// </summary>
    AudioShareMode ActualShareMode { get; }

    /// <summary>
    /// ソースファイルのオーディオフォーマット（デコード後）。
    /// </summary>
    WaveFormat? SourceFormat { get; }

    /// <summary>
    /// デバイスに出力されるオーディオフォーマット。
    /// リサンプリングが行われている場合は変換後のフォーマット、行われていない場合はSourceFormatと同じ。
    /// </summary>
    WaveFormat? OutputFormat { get; }

    /// <summary>
    /// リサンプリングが実行されているかどうか。
    /// </summary>
    bool IsResampling { get; }

    event EventHandler<PlaybackState>? StateChanged;

    event EventHandler? PlaybackCompleted;

    event EventHandler<AudioErrorEventArgs>? ErrorOccurred;

    void Load(Track track);

    void Play();

    void Pause();

    void Stop();

    void SetOutputDevice(AudioDeviceInfo device);

    void SetShareMode(AudioShareMode shareMode);
}
