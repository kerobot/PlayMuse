namespace PlayMuse.Core.Services;

/// <summary>
/// UIスレッドへの処理のマーシャリングを抽象化する。
/// NAudioのコールバック等、UIスレッド以外から発火するイベントを安全にViewModelへ反映するために使用する。
/// </summary>
public interface IDispatcherService
{
    void Invoke(Action action);
}
