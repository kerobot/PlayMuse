using NAudio.Wave;
using PlayMuse.Core.Models;
using PlayMuse.Core.Services;
using PlaybackState = PlayMuse.Core.Models.PlaybackState;

namespace PlayMuse.Tests.Fakes;

/// <summary>
/// <see cref="IAudioPlaybackService"/> のテスト用フェイク。実際の再生は行わず、呼び出し回数と状態遷移のみを記録する。
/// </summary>
public sealed class FakeAudioPlaybackService : IAudioPlaybackService
{
    public PlaybackState State { get; private set; } = PlaybackState.Stopped;

    public TimeSpan Position { get; set; }

    public TimeSpan Duration { get; set; }

    public float Volume { get; set; } = 1.0f;

    public AudioDeviceInfo? OutputDevice { get; private set; }

    public AudioShareMode ShareMode { get; private set; }

    public AudioShareMode ActualShareMode { get; private set; }

    public WaveFormat? SourceFormat => null;

    public int? SourceDisplayBitsPerSample => null;

    public WaveFormat? OutputFormat => null;

    public string? OutputFormatLabel => null;

    public bool IsResampling => false;

    public bool IsBitPerfect => false;

    public int PlayCallCount { get; private set; }

    public int PauseCallCount { get; private set; }

    public int StopCallCount { get; private set; }

    public int LoadCallCount { get; private set; }

    public Track? LoadedTrack { get; private set; }

    public event EventHandler<PlaybackState>? StateChanged;

    public event EventHandler? PlaybackCompleted;

    public event EventHandler<AudioErrorEventArgs>? ErrorOccurred;

    public void Load(Track track)
    {
        LoadedTrack = track;
        LoadCallCount++;
    }

    public void Play()
    {
        PlayCallCount++;
        SetState(PlaybackState.Playing);
    }

    public void Pause()
    {
        PauseCallCount++;
        SetState(PlaybackState.Paused);
    }

    public void Stop()
    {
        StopCallCount++;
        SetState(PlaybackState.Stopped);
    }

    public void SetOutputDevice(AudioDeviceInfo device) => OutputDevice = device;

    public void SetShareMode(AudioShareMode shareMode)
    {
        ShareMode = shareMode;
        ActualShareMode = shareMode;
    }

    public void RaiseErrorOccurred(AudioErrorEventArgs args) => ErrorOccurred?.Invoke(this, args);

    public void RaisePlaybackCompleted() => PlaybackCompleted?.Invoke(this, EventArgs.Empty);

    private void SetState(PlaybackState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
    }
}
