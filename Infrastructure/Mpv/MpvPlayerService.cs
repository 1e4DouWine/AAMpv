using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaAppMPV.Core.Playback;
using AvaloniaAppMPV.Infrastructure.Avalonia;
using static AvaloniaAppMPV.Infrastructure.Mpv.MpvInterop;

namespace AvaloniaAppMPV.Infrastructure.Mpv;

/// <summary>
/// mpv 的播放服务实现。
/// 它负责：
/// 1. 初始化 mpv core
/// 2. 把 mpv 事件转成适合 UI 使用的托管事件
/// 3. 提供统一的播放控制入口
/// </summary>
public sealed class MpvPlayerService : IMpvPlayer, IDisposable, IAsyncDisposable
{
    private readonly IDispatcherService _dispatcher;
    private readonly object _stateLock = new();
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

    public event Action<string?>? FileLoaded;
    public event Action<double>? PositionChanged;
    public event Action<double>? DurationChanged;
    public event Action<bool>? PauseChanged;
    public event Action<double>? VolumeChanged;
    public event Action<bool>? EofReached;
    public event Action<bool>? MuteChanged;
    public event Action<string>? ErrorOccurred;

    public bool IsReady
    {
        get
        {
            lock (_stateLock)
                return _mpv?.IsInitialized == true;
        }
    }

    /// <summary>
    /// 暴露原生 mpv 句柄，供视频控件创建 render context 时使用。
    /// </summary>
    public IntPtr MpvHandle
    {
        get
        {
            lock (_stateLock)
                return _mpv?.Handle ?? IntPtr.Zero;
        }
    }

    public MpvPlayerService(IDispatcherService dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// 供渲染层主动上报错误，例如 render context 初始化失败。
    /// </summary>
    public void ReportError(string message)
    {
        _dispatcher.Post(() => ErrorOccurred?.Invoke(message));
    }

    /// <summary>
    /// 初始化 mpv core。
    /// 渲染上下文由 MpvVideoView 在 OpenGL 初始化阶段单独创建。
    /// </summary>
    public void InitializeCore()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_stateLock)
        {
            if (_mpv != null)
                return;

            MpvContext? mpv = null;
            CancellationTokenSource? cts = null;

            try
            {
                mpv = new MpvContext();
                mpv.SetOption("hwdec", "auto");
                mpv.SetOption("keep-open", "yes");
                mpv.SetOption("idle", "yes");
                mpv.SetOption("vo", "libmpv");
                mpv.Initialize();

                // 订阅界面需要关注的核心属性变化。
                mpv.ObserveProperty("time-pos", MpvFormat.Double, ReplyTimePos);
                mpv.ObserveProperty("duration", MpvFormat.Double, ReplyDuration);
                mpv.ObserveProperty("pause", MpvFormat.Flag, ReplyPause);
                mpv.ObserveProperty("volume", MpvFormat.Double, ReplyVolume);
                mpv.ObserveProperty("eof-reached", MpvFormat.Flag, ReplyEofReached);
                mpv.ObserveProperty("mute", MpvFormat.Flag, ReplyMute);

                // 使用 wakeup callback 后，事件线程只会在有事件时被唤醒。
                mpv.InstallWakeupCallback();

                cts = new CancellationTokenSource();
                _mpv = mpv;
                _cts = cts;
                _eventLoopTask = Task.Run(() => EventLoop(mpv, cts.Token));
            }
            catch
            {
                _eventLoopTask = null;
                _cts = null;
                _mpv = null;

                cts?.Cancel();
                cts?.Dispose();
                mpv?.Dispose();
                throw;
            }
        }
    }

    private void EventLoop(MpvContext mpv, CancellationToken ct)
    {
        var signal = mpv.WakeupSignal;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                signal.Wait(ct);
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

            // 非阻塞地一次性把当前积压的事件都取完。
            while (!ct.IsCancellationRequested)
            {
                var ev = mpv.WaitEvent(0);
                if (ev == null || ev.Value.EventId == MpvEventId.None)
                    break;

                switch (ev.Value.EventId)
                {
                    case MpvEventId.Shutdown:
                        return;
                    case MpvEventId.FileLoaded:
                    {
                        string? fileName = null;
                        TryMpv(
                            () => fileName = mpv.GetPropertyString("filename"),
                            "读取文件信息失败");
                        _dispatcher.Post(() => FileLoaded?.Invoke(fileName));
                        break;
                    }
                    case MpvEventId.PropertyChange:
                        HandlePropertyChange(ev.Value);
                        break;
                    case MpvEventId.EndFile:
                        if (ev.Value.Error < 0)
                        {
                            var msg = GetError(ev.Value.Error);
                            _dispatcher.Post(() => ErrorOccurred?.Invoke($"播放失败: {msg}"));
                        }
                        break;
                }
            }
        }
    }

    private unsafe void HandlePropertyChange(MpvEvent ev)
    {
        if (ev.Data == IntPtr.Zero)
            return;

        ref readonly var prop = ref *(MpvEventProperty*)ev.Data;

        switch (ev.ReplyUserdata)
        {
            case ReplyTimePos when prop.Format == MpvFormat.Double && prop.Data != IntPtr.Zero:
            {
                var val = *(double*)prop.Data;
                _dispatcher.Post(() => PositionChanged?.Invoke(val));
                break;
            }
            case ReplyDuration when prop.Format == MpvFormat.Double && prop.Data != IntPtr.Zero:
            {
                var val = *(double*)prop.Data;
                _dispatcher.Post(() => DurationChanged?.Invoke(val));
                break;
            }
            case ReplyPause when prop.Format == MpvFormat.Flag && prop.Data != IntPtr.Zero:
            {
                var val = *(int*)prop.Data != 0;
                _dispatcher.Post(() => PauseChanged?.Invoke(val));
                break;
            }
            case ReplyVolume when prop.Format == MpvFormat.Double && prop.Data != IntPtr.Zero:
            {
                var val = *(double*)prop.Data;
                _dispatcher.Post(() => VolumeChanged?.Invoke(val));
                break;
            }
            case ReplyEofReached when prop.Format == MpvFormat.Flag && prop.Data != IntPtr.Zero:
            {
                var val = *(int*)prop.Data != 0;
                _dispatcher.Post(() => EofReached?.Invoke(val));
                break;
            }
            case ReplyMute when prop.Format == MpvFormat.Flag && prop.Data != IntPtr.Zero:
            {
                var val = *(int*)prop.Data != 0;
                _dispatcher.Post(() => MuteChanged?.Invoke(val));
                break;
            }
        }
    }

    public void LoadFile(string path) =>
        TryMpv(() => RequireMpv().Command("loadfile", path), "加载文件失败");

    public void Play() =>
        TryMpv(() => RequireMpv().SetProperty("pause", "no"), "播放失败");

    public void Pause() =>
        TryMpv(() => RequireMpv().SetProperty("pause", "yes"), "暂停失败");

    public void TogglePause() =>
        TryMpv(() =>
        {
            var mpv = RequireMpv();
            var paused = mpv.GetPropertyFlag("pause");
            mpv.SetProperty("pause", paused ? "no" : "yes");
        }, "切换播放/暂停失败");

    public void Seek(double positionSeconds) =>
        TryMpv(() => RequireMpv().Command("seek", positionSeconds.ToString("F2", CultureInfo.InvariantCulture), "absolute", "exact"), "跳转失败");

    public void SeekFast(double positionSeconds) =>
        TryMpv(() => RequireMpv().Command("seek", positionSeconds.ToString("F2", CultureInfo.InvariantCulture), "absolute+keyframes"), "跳转失败");

    public void SeekRelative(double offsetSeconds) =>
        TryMpv(() => RequireMpv().Command("seek", offsetSeconds.ToString("F2", CultureInfo.InvariantCulture), "relative"), "跳转失败");

    public void SetVolume(double volume) =>
        TryMpv(() => RequireMpv().SetProperty("volume", Math.Clamp(volume, 0, 100).ToString("F0", CultureInfo.InvariantCulture)), "设置音量失败");

    public void SetMute(bool mute) =>
        TryMpv(() => RequireMpv().SetProperty("mute", mute ? "yes" : "no"), "设置静音失败");

    public void ToggleMute() =>
        TryMpv(() =>
        {
            var mpv = RequireMpv();
            var muted = mpv.GetPropertyFlag("mute");
            mpv.SetProperty("mute", muted ? "no" : "yes");
        }, "切换静音失败");

    public VideoInfo? GetVideoInfo()
    {
        MpvContext? mpv;
        lock (_stateLock)
            mpv = _mpv;

        if (mpv == null || !mpv.IsInitialized)
            return null;

        try
        {
            return new VideoInfo(
                FileName: mpv.GetPropertyString("filename"),
                FileFormat: mpv.GetPropertyString("file-format"),
                FileSize: TryGetLong(mpv, "file-size"),
                VideoWidth: TryGetLong(mpv, "video-params/dw") ?? TryGetLong(mpv, "width"),
                VideoHeight: TryGetLong(mpv, "video-params/dh") ?? TryGetLong(mpv, "height"),
                VideoCodec: mpv.GetPropertyString("video-codec"),
                HwDecCurrent: mpv.GetPropertyString("hwdec-current"),
                VideoFps: TryGetDouble(mpv, "estimated-vf-fps") ?? TryGetDouble(mpv, "container-fps"),
                VideoBitrate: TryGetDouble(mpv, "video-bitrate"),
                PixelFormat: mpv.GetPropertyString("video-params/pixelformat"),
                AudioCodec: mpv.GetPropertyString("audio-codec-name"),
                AudioSampleRate: TryGetLong(mpv, "audio-params/samplerate"),
                AudioChannels: TryGetLong(mpv, "audio-params/channel-count"),
                AudioBitrate: TryGetDouble(mpv, "audio-bitrate")
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetVideoInfo failed: {ex.Message}");
            return null;
        }
    }

    private static long? TryGetLong(MpvContext mpv, string name)
    {
        return mpv.TryGetPropertyLong(name, out long val) ? val : null;
    }

    private static double? TryGetDouble(MpvContext mpv, string name)
    {
        return mpv.TryGetPropertyDouble(name, out double val) ? val : null;
    }

    private void TryMpv(Action action, string context)
    {
        try
        {
            action();
        }
        catch (MpvException ex)
        {
            _dispatcher.Post(() => ErrorOccurred?.Invoke($"{context}: {ex.Message}"));
        }
        catch (ObjectDisposedException)
        {
            // 关闭过程中出现的调用直接忽略，避免多余报错。
        }
        catch (Exception ex)
        {
            _dispatcher.Post(() => ErrorOccurred?.Invoke($"{context}: {ex.Message}"));
        }
    }

    private MpvContext RequireMpv()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_stateLock)
            return _mpv ?? throw new InvalidOperationException("播放器尚未初始化。");
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? cts;
        Task? eventLoopTask;
        MpvContext? mpv;

        lock (_stateLock)
        {
            if (_disposed)
                return;

            _disposed = true;

            cts = _cts;
            eventLoopTask = _eventLoopTask;
            mpv = _mpv;

            _cts = null;
            _eventLoopTask = null;
            _mpv = null;
        }

        cts?.Cancel();
        mpv?.WakeupSignal.Set();

        try
        {
            if (eventLoopTask != null)
                await eventLoopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 取消事件线程时的异常属于预期行为。
        }
        catch (ObjectDisposedException)
        {
            // 关闭过程中等待句柄被释放也属于可接受路径。
        }

        mpv?.Dispose();
        cts?.Dispose();
    }
}
