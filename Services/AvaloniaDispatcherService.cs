using System;
using Avalonia.Threading;

namespace AvaloniaAppMPV.Services;

public class AvaloniaDispatcherService : IDispatcherService
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public void RunOnce(Action action, TimeSpan delay) =>
        DispatcherTimer.RunOnce(action, delay);
}
