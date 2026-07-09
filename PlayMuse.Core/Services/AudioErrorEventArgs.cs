namespace PlayMuse.Core.Services;

public sealed class AudioErrorEventArgs(string message, Exception? exception = null) : EventArgs
{
    public string Message { get; } = message;

    public Exception? Exception { get; } = exception;
}
