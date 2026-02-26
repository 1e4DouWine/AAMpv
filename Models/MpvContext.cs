using System;
using System.Runtime.InteropServices;
using System.Threading;
using static AvaloniaAppMPV.Models.MpvInterop;

namespace AvaloniaAppMPV.Models;

/// <summary>
/// Thread-safe managed wrapper around a native mpv handle.
/// All public methods are guarded by a lock and a disposed check.
/// </summary>
public sealed class MpvContext : IDisposable
{
    private IntPtr _mpvHandle;
    private readonly object _lock = new();
    private volatile bool _disposed;

    // Wakeup callback support: signal the event loop when mpv has events
    private readonly ManualResetEventSlim _wakeupSignal = new(false);
    private MpvWakeupCallbackFn? _wakeupCallbackDelegate; // prevent GC collection

    public bool IsInitialized => _mpvHandle != IntPtr.Zero;

    /// <summary>
    /// Raw mpv handle for use with mpv_render_context_create.
    /// Caller must ensure thread safety.
    /// </summary>
    public IntPtr Handle => _mpvHandle;

    /// <summary>
    /// Signal used by the event loop to wait for mpv wakeup notifications.
    /// </summary>
    public ManualResetEventSlim WakeupSignal => _wakeupSignal;

    public MpvContext()
    {
        _mpvHandle = mpv_create();
        if (_mpvHandle == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create mpv instance");
    }

    public void Initialize()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            Check(mpv_initialize(_mpvHandle));
        }
    }

    public void SetOption(string name, string value)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            Check(mpv_set_option_string(_mpvHandle, name, value));
        }
    }

    public void SetProperty(string name, string value)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            Check(mpv_set_property_string(_mpvHandle, name, value));
        }
    }

    public bool GetPropertyFlag(string name)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            mpv_get_property(_mpvHandle, name, MpvFormat.Flag, out int val);
            return val != 0;
        }
    }

    public double GetPropertyDouble(string name)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            mpv_get_property(_mpvHandle, name, MpvFormat.Double, out double val);
            return val;
        }
    }

    public bool TryGetPropertyDouble(string name, out double value)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            int err = mpv_get_property(_mpvHandle, name, MpvFormat.Double, out value);
            return err >= 0;
        }
    }

    public long GetPropertyLong(string name)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            mpv_get_property(_mpvHandle, name, MpvFormat.Int64, out long val);
            return val;
        }
    }

    public bool TryGetPropertyLong(string name, out long value)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            int err = mpv_get_property(_mpvHandle, name, MpvFormat.Int64, out value);
            return err >= 0;
        }
    }

    public string? GetPropertyString(string name)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            int err = mpv_get_property(_mpvHandle, name, MpvFormat.String, out IntPtr ptr);
            if (err < 0 || ptr == IntPtr.Zero) return null;
            try
            {
                return Marshal.PtrToStringUTF8(ptr);
            }
            finally
            {
                mpv_free(ptr);
            }
        }
    }

    public unsafe void Command(params string[] args)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            // stackalloc avoids heap allocation for the pointer array
            const int StackLimit = 16;
            int count = args.Length + 1;
            Span<IntPtr> span = count <= StackLimit
                ? stackalloc IntPtr[count]
                : new IntPtr[count];
            try
            {
                for (int i = 0; i < args.Length; i++)
                    span[i] = AllocUtf8(args[i]);
                span[args.Length] = IntPtr.Zero;
                fixed (IntPtr* ptr = span)
                {
                    Check(mpv_command_ptr(_mpvHandle, ptr));
                }
            }
            finally
            {
                for (int i = 0; i < args.Length; i++)
                    if (span[i] != IntPtr.Zero)
                        FreeUtf8(span[i]);
            }
        }
    }

    public void CommandString(string cmd)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            Check(mpv_command_string(_mpvHandle, cmd));
        }
    }

    public void ObserveProperty(string name, MpvFormat format, ulong userData = 0)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            Check(mpv_observe_property(_mpvHandle, userData, name, format));
        }
    }

    /// <summary>
    /// Install the mpv wakeup callback so the event loop can be signal-driven
    /// instead of polling. Must be called after Initialize().
    /// </summary>
    public void InstallWakeupCallback()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            _wakeupCallbackDelegate = OnWakeup;
            var fnPtr = Marshal.GetFunctionPointerForDelegate(_wakeupCallbackDelegate);
            mpv_set_wakeup_callback(_mpvHandle, fnPtr, IntPtr.Zero);
            // Set signal so the event loop immediately drains any events
            // that were queued before the callback was installed
            _wakeupSignal.Set();
        }
    }

    private void OnWakeup(IntPtr d)
    {
        _wakeupSignal.Set();
    }

    /// <summary>
    /// Waits for the next mpv event using unsafe pointer dereference instead of
    /// Marshal.PtrToStructure. This call is NOT locked because mpv_wait_event
    /// is thread-safe per the mpv documentation.
    /// </summary>
    public unsafe MpvEvent? WaitEvent(double timeout = 0)
    {
        if (_disposed) return null;
        var ptr = mpv_wait_event(_mpvHandle, timeout);
        if (ptr == IntPtr.Zero) return null;
        return *(MpvEvent*)ptr;
    }

    private void Check(int errorCode)
    {
        if (errorCode < 0)
            throw new MpvException(errorCode, GetError(errorCode));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;

        ManualResetEventSlim? signalToDispose = null;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            // Signal the event loop to exit if it's waiting
            _wakeupSignal.Set();

            if (_mpvHandle != IntPtr.Zero)
            {
                mpv_terminate_destroy(_mpvHandle);
                _mpvHandle = IntPtr.Zero;
            }

            signalToDispose = _wakeupSignal;
        }
        // Dispose outside the lock to avoid potential race with event loop Wait()
        signalToDispose?.Dispose();
    }
}

public class MpvException : Exception
{
    public int ErrorCode { get; }

    public MpvException(int errorCode, string message)
        : base($"mpv error {errorCode}: {message}")
    {
        ErrorCode = errorCode;
    }
}
