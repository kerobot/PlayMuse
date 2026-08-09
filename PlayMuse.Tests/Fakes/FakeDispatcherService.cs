using PlayMuse.Core.Services;

namespace PlayMuse.Tests.Fakes;

/// <summary>
/// <see cref="IDispatcherService"/> のテスト用フェイク。テストはシングルスレッドで実行されるため、即時実行する。
/// </summary>
public sealed class FakeDispatcherService : IDispatcherService
{
    public void Invoke(Action action) => action();
}
