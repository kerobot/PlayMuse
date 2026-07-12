namespace PlayMuse.Core.Models;

public sealed class AppSettings
{
    public AudioShareMode ShareMode { get; set; } = AudioShareMode.Shared;

    public string? OutputDeviceId { get; set; }

    public float Volume { get; set; } = 1.0f;

    public bool IsLoopEnabled { get; set; }
}
