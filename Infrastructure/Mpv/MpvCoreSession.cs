using System;
using System.Threading;
using System.Threading.Tasks;
using static AvaloniaAppMPV.Infrastructure.Mpv.MpvInterop;

namespace AvaloniaAppMPV.Infrastructure.Mpv;

/// <summary>
/// libmpv core 的生命周期和事件泵。
/// 播放业务只处理事件，不再直接拥有 MPV 事件线程和取消资源。
/// </summary>
public sealed class MpvCoreSession : IDisposable, IAsyncDisposable
{
    private readonly object _lifecycleLock = new();
    private readonly MpvContext _context;
    private CancellationTokenSource? _cts;
    private Task? _eventLoopTask;
    private bool _disposed;

    public MpvContext Context => _context;
    public IntPtr Handle => _context.Handle;

    public MpvCoreSession()
    {
        _context = new MpvContext();
    }

    public void StartEventLoop(Action<MpvEvent> eventHandler)
    {
        ArgumentNullException.ThrowIfNull(eventHandler);

        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_eventLoopTask != null)
                return;

            _context.InstallWakeupCallback();
            _cts = new CancellationTokenSource();
            var cancellationToken = _cts.Token;
            _eventLoopTask = Task.Run(() => EventLoop(eventHandler, cancellationToken), cancellationToken);
        }
    }

    private void EventLoop(Action<MpvEvent> eventHandler, CancellationToken cancellationToken)
    {
        var signal = _context.WakeupSignal;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                signal.Wait(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            signal.Reset();

            while (!cancellationToken.IsCancellationRequested)
            {
                var ev = _context.WaitEvent(0);
                if (ev == null || ev.Value.EventId == MpvEventId.None)
                    break;

                if (ev.Value.EventId == MpvEventId.Shutdown)
                    return;

                eventHandler(ev.Value);
            }
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? cts;
        Task? eventLoopTask;

        lock (_lifecycleLock)
        {
            if (_disposed)
                return;

            _disposed = true;
            cts = _cts;
            eventLoopTask = _eventLoopTask;
            _cts = null;
            _eventLoopTask = null;
        }

        cts?.Cancel();
        _context.WakeupSignal.Set();

        try
        {
            if (eventLoopTask != null)
                await eventLoopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        _context.Dispose();
        cts?.Dispose();
    }
}
