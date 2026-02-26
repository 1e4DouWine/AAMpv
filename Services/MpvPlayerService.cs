using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaAppMPV.Models;
using static AvaloniaAppMPV.Models.MpvInterop;

namespace AvaloniaAppMPV.Services;

/// <summary>
/// Standalone IMpvPlayer implementation decoupled from any View.
/// Receives the native window handle via Initialize() after the View is ready.
/// </summary>
public sealed class MpvPlayerService : IMpvPlayer, IDisposable
{
    private readonly IDispatcherService _dispatcher;
    private MpvContext? _mpv;
    private CancellationTokenSource? _cts;
    private Task? _eventLoopTask;
    private bool _disposed;

    private const ulong ReplyTimePos = 1;
    private const ulong ReplyDuration = 2;
    private const ulong ReplyPause = 3;
    private const ulong ReplyVolume = 4;
    private const ulong ReplyEofReached = 5;
    private const ulong ReplyMute = 6;

    private IntPtr _renderContext;

    public event Action<double>? PositionChanged;
    public event Action<double>? DurationChanged;
    public event Action<bool>? PauseChanged;
    public event Action<double>? VolumeChanged;
    public event Action<bool>? EofReached;
    public event Action<bool>? MuteChanged;
    public event Action<string>? ErrorOccurred;

    public bool IsReady => _mpv?.IsInitialized == true;

    /// <summary>
    /// Raw mpv handle for render context creation.
    /// </summary>
    public IntPtr MpvHandle => _mpv?.Handle ?? IntPtr.Zero;

    public MpvPlayerService(IDispatcherService dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Report an error from the render layer.
    /// </summary>
    public void ReportError(string message)
    {
        _dispatcher.Post(() => ErrorOccurred?.Invoke(message));
    }

    /// <summary>
    /// Store the render context handle for cleanup coordination.
    /// </summary>
    public void SetRenderContext(IntPtr renderContext)
    {
        _renderContext = renderContext;
    }

    /// <summary>
    /// Initialize mpv core without wid. The render context is created
    /// separately by MpvVideoView in OnOpenGlInit.
    /// </summary>
    public void InitializeCore()
    {
        if (_mpv != null) return;

        _mpv = new MpvContext();
        _mpv.SetOption("hwdec", "auto");
        _mpv.SetOption("keep-open", "yes");
        _mpv.SetOption("idle", "yes");
        // vo=libmpv tells mpv to use the render API instead of creating its own window
        _mpv.SetOption("vo", "libmpv");
        _mpv.Initialize();

        _mpv.ObserveProperty("time-pos", MpvFormat.Double, ReplyTimePos);
        _mpv.ObserveProperty("duration", MpvFormat.Double, ReplyDuration);
        _mpv.ObserveProperty("pause", MpvFormat.Flag, ReplyPause);
        _mpv.ObserveProperty("volume", MpvFormat.Double, ReplyVolume);
        _mpv.ObserveProperty("eof-reached", MpvFormat.Flag, ReplyEofReached);
        _mpv.ObserveProperty("mute", MpvFormat.Flag, ReplyMute);

        _cts = new CancellationTokenSource();
        _eventLoopTask = Task.Run(() => EventLoop(_cts.Token));
    }

    private void EventLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _mpv != null)
        {
            var ev = _mpv.WaitEvent(0.5);
            if (ev == null) continue;

            switch (ev.Value.EventId)
            {
                case MpvEventId.Shutdown:
                    return;
                case MpvEventId.PropertyChange:
                    HandlePropertyChange(ev.Value);
                    break;
                case MpvEventId.EndFile:
                    if (ev.Value.Error < 0)
                    {
                        var msg = GetError(ev.Value.Error);
                        _dispatcher.Post(() =>
                            ErrorOccurred?.Invoke($"播放失败: {msg}"));
                    }
                    break;
            }
        }
    }

    private void HandlePropertyChange(MpvEvent ev)
    {
        if (ev.Data == IntPtr.Zero) return;
        var prop = Marshal.PtrToStructure<MpvEventProperty>(ev.Data);

        switch (ev.ReplyUserdata)
        {
            case ReplyTimePos when prop.Format == MpvFormat.Double && prop.Data != IntPtr.Zero:
            {
                var val = Marshal.PtrToStructure<double>(prop.Data);
                _dispatcher.Post(() => PositionChanged?.Invoke(val));
                break;
            }
            case ReplyDuration when prop.Format == MpvFormat.Double && prop.Data != IntPtr.Zero:
            {
                var val = Marshal.PtrToStructure<double>(prop.Data);
                _dispatcher.Post(() => DurationChanged?.Invoke(val));
                break;
            }
            case ReplyPause when prop.Format == MpvFormat.Flag && prop.Data != IntPtr.Zero:
            {
                var val = Marshal.PtrToStructure<int>(prop.Data) != 0;
                _dispatcher.Post(() => PauseChanged?.Invoke(val));
                break;
            }
            case ReplyVolume when prop.Format == MpvFormat.Double && prop.Data != IntPtr.Zero:
            {
                var val = Marshal.PtrToStructure<double>(prop.Data);
                _dispatcher.Post(() => VolumeChanged?.Invoke(val));
                break;
            }
            case ReplyEofReached when prop.Format == MpvFormat.Flag && prop.Data != IntPtr.Zero:
            {
                var val = Marshal.PtrToStructure<int>(prop.Data) != 0;
                _dispatcher.Post(() => EofReached?.Invoke(val));
                break;
            }
            case ReplyMute when prop.Format == MpvFormat.Flag && prop.Data != IntPtr.Zero:
            {
                var val = Marshal.PtrToStructure<int>(prop.Data) != 0;
                _dispatcher.Post(() => MuteChanged?.Invoke(val));
                break;
            }
        }
    }

    // --- IMpvPlayer commands ---

    public void LoadFile(string path) =>
        TryMpv(() => _mpv?.Command("loadfile", path), "加载文件失败");

    public void Play() =>
        TryMpv(() => _mpv?.SetProperty("pause", "no"), "播放失败");

    public void Pause() =>
        TryMpv(() => _mpv?.SetProperty("pause", "yes"), "暂停失败");

    public void TogglePause() =>
        TryMpv(() =>
        {
            if (_mpv == null) return;
            var paused = _mpv.GetPropertyFlag("pause");
            _mpv.SetProperty("pause", paused ? "no" : "yes");
        }, "切换播放/暂停失败");

    public void Seek(double positionSeconds) =>
        TryMpv(() => _mpv?.Command("seek", positionSeconds.ToString("F2"), "absolute", "exact"), "跳转失败");

    public void SeekFast(double positionSeconds) =>
        TryMpv(() => _mpv?.Command("seek", positionSeconds.ToString("F2"), "absolute+keyframes"), "跳转失败");

    public void SeekRelative(double offsetSeconds) =>
        TryMpv(() => _mpv?.Command("seek", offsetSeconds.ToString("F2"), "relative"), "跳转失败");

    public void SetVolume(double volume) =>
        TryMpv(() => _mpv?.SetProperty("volume", Math.Clamp(volume, 0, 100).ToString("F0")), "设置音量失败");

    public void SetMute(bool mute) =>
        TryMpv(() => _mpv?.SetProperty("mute", mute ? "yes" : "no"), "设置静音失败");

    public void ToggleMute() =>
        TryMpv(() =>
        {
            if (_mpv == null) return;
            var muted = _mpv.GetPropertyFlag("mute");
            _mpv.SetProperty("mute", muted ? "no" : "yes");
        }, "切换静音失败");

    private void TryMpv(Action action, string context)
    {
        try { action(); }
        catch (MpvException ex)
        {
            _dispatcher.Post(() => ErrorOccurred?.Invoke($"{context}: {ex.Message}"));
        }
        catch (ObjectDisposedException) { /* shutting down */ }
        catch (Exception ex)
        {
            _dispatcher.Post(() => ErrorOccurred?.Invoke($"{context}: {ex.Message}"));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        try { _eventLoopTask?.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { /* expected on cancellation */ }

        // Render context is already freed by MpvVideoView.CleanupRenderContext()
        // which is called before this method. Clear our reference.
        _renderContext = IntPtr.Zero;

        // Small delay to let any in-flight native callbacks drain
        Thread.Sleep(50);

        _mpv?.Dispose();
        _mpv = null;
        _cts?.Dispose();
        _cts = null;
    }
}
