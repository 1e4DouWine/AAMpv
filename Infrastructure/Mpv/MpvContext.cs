using System;
using System.Runtime.InteropServices;
using System.Threading;
using static AvaloniaAppMPV.Infrastructure.Mpv.MpvInterop;

namespace AvaloniaAppMPV.Infrastructure.Mpv;

/// <summary>
/// 对 mpv 原生句柄的线程安全托管封装。
/// 对外公开的方法都带有锁和释放检查，避免 UI 线程和事件线程同时踩到原生资源。
/// </summary>
public sealed class MpvContext : IDisposable
{
    private IntPtr _mpvHandle;
    private readonly object _lock = new();
    private volatile bool _disposed;
    private bool _initialized;

    // mpv 有新事件时通过 wakeup callback 唤醒事件循环，而不是持续轮询。
    private readonly ManualResetEventSlim _wakeupSignal = new(false);
    private MpvWakeupCallbackFn? _wakeupCallbackDelegate;

    public bool IsInitialized => _initialized;

    /// <summary>
    /// 暴露原生 mpv 句柄，供 render context 创建时使用。
    /// 调用方需要自行保证线程安全。
    /// </summary>
    public IntPtr Handle => _mpvHandle;

    /// <summary>
    /// 事件循环等待的信号量。
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
            if (_initialized)
                return;

            Check(mpv_initialize(_mpvHandle));
            _initialized = true;
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

    public void LoadConfigFile(string path)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            Check(mpv_load_config_file(_mpvHandle, path));
        }
    }

    public void RequestLogMessages(string level = "warn")
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            Check(mpv_request_log_messages(_mpvHandle, level));
        }
    }

    public bool GetPropertyFlag(string name)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            Check(mpv_get_property(_mpvHandle, name, MpvFormat.Flag, out int val));
            return val != 0;
        }
    }

    public double GetPropertyDouble(string name)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            Check(mpv_get_property(_mpvHandle, name, MpvFormat.Double, out double val));
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
            Check(mpv_get_property(_mpvHandle, name, MpvFormat.Int64, out long val));
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
            if (err < 0 || ptr == IntPtr.Zero)
                return null;

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
                {
                    if (span[i] != IntPtr.Zero)
                        FreeUtf8(span[i]);
                }
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
    /// 安装 mpv 的 wakeup callback，让事件循环在有事件时再被唤醒。
    /// </summary>
    public void InstallWakeupCallback()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            _wakeupCallbackDelegate = OnWakeup;
            var fnPtr = Marshal.GetFunctionPointerForDelegate(_wakeupCallbackDelegate);
            mpv_set_wakeup_callback(_mpvHandle, fnPtr, IntPtr.Zero);

            // 安装回调前如果已经有事件排队，主动 set 一次，避免首批事件被漏掉。
            _wakeupSignal.Set();
        }
    }

    private void OnWakeup(IntPtr d)
    {
        _wakeupSignal.Set();
    }

    /// <summary>
    /// 读取下一条 mpv 事件。
    /// 这个调用本身是线程安全的，因此不再额外加锁。
    /// </summary>
    public unsafe MpvEvent? WaitEvent(double timeout = 0)
    {
        if (_disposed || !_initialized || _mpvHandle == IntPtr.Zero)
            return null;

        var ptr = mpv_wait_event(_mpvHandle, timeout);
        if (ptr == IntPtr.Zero)
            return null;

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
        if (_disposed)
            return;

        ManualResetEventSlim? signalToDispose = null;
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            _initialized = false;

            // 如果事件线程正在等待，先唤醒它，让它有机会退出。
            _wakeupSignal.Set();

            if (_mpvHandle != IntPtr.Zero)
            {
                mpv_terminate_destroy(_mpvHandle);
                _mpvHandle = IntPtr.Zero;
            }

            signalToDispose = _wakeupSignal;
        }

        // 在锁外释放等待句柄，避免和事件线程的 Wait 形成竞争。
        signalToDispose?.Dispose();
    }
}

/// <summary>
/// 对 mpv 错误码做一层异常封装，便于上层统一处理。
/// </summary>
public class MpvException : Exception
{
    public int ErrorCode { get; }

    public MpvException(int errorCode, string message)
        : base($"mpv error {errorCode}: {message}")
    {
        ErrorCode = errorCode;
    }
}
