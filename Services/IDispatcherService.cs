using System;

namespace AvaloniaAppMPV.Services;

/// <summary>
/// Abstracts UI thread dispatching away from the ViewModel.
/// </summary>
public interface IDispatcherService
{
    void Post(Action action);
    void RunOnce(Action action, TimeSpan delay);
}
