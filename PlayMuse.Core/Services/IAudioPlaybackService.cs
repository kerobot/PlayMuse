using PlayMuse.Core.Models;

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

    AudioShareMode ShareMode { get; }

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
