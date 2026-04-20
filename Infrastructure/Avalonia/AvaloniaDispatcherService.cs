using System;
using Avalonia.Threading;

namespace AvaloniaAppMPV.Infrastructure.Avalonia;

/// <summary>
/// Avalonia 对应的 UI 调度实现。
/// </summary>
public class AvaloniaDispatcherService : IDispatcherService
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public void RunOnce(Action action, TimeSpan delay) =>
        DispatcherTimer.RunOnce(action, delay);
}
