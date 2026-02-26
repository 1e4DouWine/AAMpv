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

    public bool IsInitialized => _mpvHandle != IntPtr.Zero;

    /// <summary>
    /// Raw mpv handle for use with mpv_render_context_create.
    /// Caller must ensure thread safety.
    /// </summary>
    public IntPtr Handle => _mpvHandle;

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

    public long GetPropertyLong(string name)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            mpv_get_property(_mpvHandle, name, MpvFormat.Int64, out long val);
            return val;
        }
    }

    public void Command(params string[] args)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            var ptrs = new IntPtr[args.Length + 1];
            try
            {
                for (int i = 0; i < args.Length; i++)
                    ptrs[i] = AllocUtf8(args[i]);
                ptrs[args.Length] = IntPtr.Zero;
                Check(mpv_command(_mpvHandle, ptrs));
            }
            finally
            {
                for (int i = 0; i < args.Length; i++)
                    if (ptrs[i] != IntPtr.Zero)
                        FreeUtf8(ptrs[i]);
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
    /// Waits for the next mpv event. This call is NOT locked because it is
    /// designed to block on the event thread; mpv_wait_event is thread-safe
    /// with respect to other mpv API calls per the mpv documentation.
    /// </summary>
    public MpvEvent? WaitEvent(double timeout = 0)
    {
        if (_disposed) return null;
        var ptr = mpv_wait_event(_mpvHandle, timeout);
        if (ptr == IntPtr.Zero) return null;
        return Marshal.PtrToStructure<MpvEvent>(ptr);
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

        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            if (_mpvHandle != IntPtr.Zero)
            {
                mpv_terminate_destroy(_mpvHandle);
                _mpvHandle = IntPtr.Zero;
            }
        }
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
