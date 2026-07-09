using System.Windows.Threading;
using PlayMuse.Core.Services;

namespace PlayMuse.Services;

/// <summary>
/// WPFの<see cref="Dispatcher"/>を用いて<see cref="IDispatcherService"/>を実装する。
/// </summary>
public sealed class WpfDispatcherService : IDispatcherService
{
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;

    public void Invoke(Action action)
    {
        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }
}
