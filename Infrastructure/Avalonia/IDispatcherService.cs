using System;

namespace AvaloniaAppMPV.Infrastructure.Avalonia;

/// <summary>
/// 对 UI 线程调度做一层抽象，避免 ViewModel 直接依赖 Avalonia 静态 API。
/// </summary>
public interface IDispatcherService
{
    void Post(Action action);
    void RunOnce(Action action, TimeSpan delay);
}
