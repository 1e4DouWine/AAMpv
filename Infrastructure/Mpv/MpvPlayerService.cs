using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaAppMPV.Core.Playback;
using AvaloniaAppMPV.Infrastructure.Avalonia;
using static AvaloniaAppMPV.Infrastructure.Mpv.MpvInterop;

namespace AvaloniaAppMPV.Infrastructure.Mpv;

public sealed class MpvPlayerService : IMpvPlayer, IMpvRenderHost, IDisposable, IAsyncDisposable
{
    private readonly IDispatcherService _dispatcher;
    private readonly object _stateLock = new();
    private MpvCoreSession? _coreSession;
    private MpvPlayerSettings _settings = new();
    private bool _disposed;
    private PlaybackSnapshot _snapshot = PlaybackSnapshot.Empty;

    private const ulong ReplyTimePos = 1;
    private const ulong ReplyDuration = 2;
    private const ulong ReplyPause = 3;
    private const ulong ReplyVolume = 4;
    private const ulong ReplyEofReached = 5;
    private const ulong ReplyMute = 6;
    private const ulong ReplySpeed = 7;
    private const ulong ReplyHwDec = 8;

    public event Action<string?>? FileLoaded;
    public event Action<PlaybackSnapshot>? SnapshotChanged;
    public event Action<string>? ErrorOccurred;
    public event Action<string>? LogMessage;
    public event Action<string>? WarningOccurred;

    public bool IsReady
    {
        get { lock (_stateLock) return _coreSession?.Context.IsInitialized == true; }
    }

    public PlaybackSnapshot Snapshot
    {
        get { lock (_stateLock) return _snapshot; }
    }

    public PlaybackState PlaybackState
    {
        get { lock (_stateLock) return _snapshot.State; }
    }

    public string? CurrentFilePath
    {
        get { lock (_stateLock) return _snapshot.FilePath; }
    }

    public string? CurrentHardwareDecode
    {
        get { lock (_stateLock) return _snapshot.HardwareDecode; }
    }

    public RenderBackendKind RenderBackend
    {
        get { lock (_stateLock) return _snapshot.RenderBackend; }
    }

    public IntPtr MpvHandle
    {
        get { lock (_stateLock) return _coreSession?.Handle ?? IntPtr.Zero; }
    }

    public MpvPlayerService(IDispatcherService dispatcher) => _dispatcher = dispatcher;

    public void Configure(MpvPlayerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_stateLock)
        {
            if (_coreSession != null)
                throw new InvalidOperationException("播放器已经初始化，设置需要在初始化前修改。");
            _settings = settings.Clone();
            _settings.DefaultSpeed = NormalizeSpeed(_settings.DefaultSpeed);
            _settings.Volume = Math.Clamp(_settings.Volume, 0, 100);
            _snapshot = _snapshot with
            {
                Speed = _settings.DefaultSpeed,
                Volume = _settings.Volume,
                IsMuted = _settings.IsMuted,
                RenderBackend = RenderBackendKind.OpenGL,
            };
        }
    }

    public void ReportError(string message) => Publish(() => ErrorOccurred?.Invoke(message));

    public void InitializeCore()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Exception? initializationError = null;
        MpvCoreSession? failedSession = null;

        lock (_stateLock)
        {
            if (_coreSession != null)
                return;

            MpvCoreSession? session = null;
            try
            {
                var settings = _settings.Clone();
                if (settings.RenderBackend is RenderBackendKind.Direct3D11 or RenderBackendKind.Vulkan)
                    PublishWarning($"渲染后端 {settings.RenderBackend} 尚未实现，当前使用 OpenGL。");
                session = new MpvCoreSession();
                var mpv = session.Context;
                mpv.SetOption("config", "no");
                mpv.SetOption("hwdec", ToMpvHardwareDecode(settings.HardwareDecode));
                mpv.SetOption("keep-open", "yes");
                mpv.SetOption("idle", "yes");
                mpv.SetOption("vo", "libmpv");
                mpv.SetOption("speed", settings.DefaultSpeed.ToString(CultureInfo.InvariantCulture));
                mpv.Initialize();

                LoadCustomConfig(mpv, settings);
                // These are mandatory for the embedded OpenGL render context.
                mpv.SetProperty("hwdec", ToMpvHardwareDecode(settings.HardwareDecode));
                mpv.SetProperty("volume", settings.Volume.ToString("F0", CultureInfo.InvariantCulture));
                mpv.SetProperty("mute", settings.IsMuted ? "yes" : "no");

                mpv.ObserveProperty("time-pos", MpvFormat.Double, ReplyTimePos);
                mpv.ObserveProperty("duration", MpvFormat.Double, ReplyDuration);
                mpv.ObserveProperty("pause", MpvFormat.Flag, ReplyPause);
                mpv.ObserveProperty("volume", MpvFormat.Double, ReplyVolume);
                mpv.ObserveProperty("eof-reached", MpvFormat.Flag, ReplyEofReached);
                mpv.ObserveProperty("mute", MpvFormat.Flag, ReplyMute);
                mpv.ObserveProperty("speed", MpvFormat.Double, ReplySpeed);
                mpv.ObserveProperty("hwdec-current", MpvFormat.String, ReplyHwDec);
                mpv.RequestLogMessages("warn");
                session.StartEventLoop(ev => HandleEvent(mpv, ev));
                _coreSession = session;
            }
            catch (Exception ex)
            {
                failedSession = session;
                initializationError = ex;
            }
        }

        if (initializationError != null)
        {
            failedSession?.Dispose();
            ExceptionDispatchInfo.Capture(initializationError).Throw();
        }

        UpdateSnapshot(snapshot => snapshot with { State = PlaybackState.Unloaded });
    }

    private void LoadCustomConfig(MpvContext mpv, MpvPlayerSettings settings)
    {
        if (!settings.UseCustomMpvConfig || string.IsNullOrWhiteSpace(settings.MpvConfigPath))
            return;

        var path = Path.GetFullPath(settings.MpvConfigPath);
        if (!File.Exists(path))
        {
            PublishWarning($"MPV 配置文件不存在，已跳过: {path}");
            return;
        }

        var forbidden = MpvConfigValidator.FindForbiddenOptions(path);
        if (forbidden.Count > 0)
        {
            PublishWarning($"MPV 配置包含嵌入渲染不允许的选项，已跳过: {string.Join(", ", forbidden)}");
            return;
        }

        try
        {
            mpv.LoadConfigFile(path);
        }
        catch (MpvException ex)
        {
            PublishWarning($"MPV 配置加载失败，已跳过: {ex.Message}");
        }
    }

    private void HandleEvent(MpvContext mpv, MpvEvent ev)
    {
        switch (ev.EventId)
        {
            case MpvEventId.StartFile:
                UpdateSnapshot(snapshot => snapshot with
                {
                    State = PlaybackState.Loading,
                    IsPaused = true,
                });
                break;
            case MpvEventId.FileLoaded:
                HandleFileLoaded(mpv);
                break;
            case MpvEventId.PropertyChange:
                HandlePropertyChange(ev);
                break;
            case MpvEventId.LogMessage:
                HandleLogMessage(ev);
                break;
            case MpvEventId.EndFile:
                HandleEndFile(ev);
                break;
        }
    }

    private void HandleFileLoaded(MpvContext mpv)
    {
        string? fileName = null;
        TryMpv(() =>
        {
            fileName = mpv.GetPropertyString("filename");
            UpdateHardwareDecode(mpv.GetPropertyString("hwdec-current"));
        }, "读取文件信息失败");

        UpdateSnapshot(snapshot => snapshot with
        {
            FilePath = fileName,
            State = PlaybackState.Paused,
            IsPaused = true,
            Position = 0,
            Duration = 0,
        });
        Publish(() => FileLoaded?.Invoke(fileName));
    }

    private unsafe void HandlePropertyChange(MpvEvent ev)
    {
        if (ev.Data == IntPtr.Zero)
            return;
        ref readonly var prop = ref *(MpvEventProperty*)ev.Data;

        switch (ev.ReplyUserdata)
        {
            case ReplyTimePos when prop.Format == MpvFormat.Double && prop.Data != IntPtr.Zero:
                var position = *(double*)prop.Data;
                UpdateSnapshot(snapshot => snapshot with { Position = position }); break;
            case ReplyDuration when prop.Format == MpvFormat.Double && prop.Data != IntPtr.Zero:
                var duration = *(double*)prop.Data;
                UpdateSnapshot(snapshot => snapshot with { Duration = duration }); break;
            case ReplyPause when prop.Format == MpvFormat.Flag && prop.Data != IntPtr.Zero:
                HandlePause(*(int*)prop.Data != 0); break;
            case ReplyVolume when prop.Format == MpvFormat.Double && prop.Data != IntPtr.Zero:
                var volume = *(double*)prop.Data;
                UpdateSnapshot(snapshot => snapshot with { Volume = volume }); break;
            case ReplyEofReached when prop.Format == MpvFormat.Flag && prop.Data != IntPtr.Zero:
                var eof = *(int*)prop.Data != 0;
                if (eof)
                    UpdateSnapshot(snapshot => snapshot with { State = PlaybackState.Ended, IsPaused = true });
                break;
            case ReplyMute when prop.Format == MpvFormat.Flag && prop.Data != IntPtr.Zero:
                var mute = *(int*)prop.Data != 0;
                UpdateSnapshot(snapshot => snapshot with { IsMuted = mute }); break;
            case ReplySpeed when prop.Format == MpvFormat.Double && prop.Data != IntPtr.Zero:
                UpdateSpeed(*(double*)prop.Data); break;
            case ReplyHwDec when prop.Format == MpvFormat.String && prop.Data != IntPtr.Zero:
                UpdateHardwareDecode(Marshal.PtrToStringUTF8(*(IntPtr*)prop.Data)); break;
        }
    }

    private void HandlePause(bool paused)
    {
        UpdateSnapshot(snapshot => snapshot with
        {
            IsPaused = paused,
            State = snapshot.State is PlaybackState.Loading or PlaybackState.Ended or PlaybackState.Unloaded
                ? snapshot.State
                : paused ? PlaybackState.Paused : PlaybackState.Playing,
        });
    }

    private void HandleLogMessage(MpvEvent ev)
    {
        if (ev.Data == IntPtr.Zero)
            return;
        var log = Marshal.PtrToStructure<MpvEventLogMessage>(ev.Data);
        var prefix = Marshal.PtrToStringUTF8(log.Prefix) ?? "mpv";
        var level = Marshal.PtrToStringUTF8(log.Level) ?? "warn";
        var text = Marshal.PtrToStringUTF8(log.Text)?.TrimEnd() ?? string.Empty;
        if (text.Length > 0)
            Publish(() => LogMessage?.Invoke($"[{prefix}] {level}: {text}"));
    }

    private void HandleEndFile(MpvEvent ev)
    {
        var error = ev.Error;
        if (ev.Data != IntPtr.Zero)
        {
            var end = Marshal.PtrToStructure<MpvEventEndFile>(ev.Data);
            error = end.Error;
            if (end.Reason == MpvEndFileReason.Eof)
            {
                UpdateSnapshot(snapshot => snapshot with
                {
                    State = PlaybackState.Ended,
                    IsPaused = true,
                });
                return;
            }
        }

        if (error < 0)
        {
            UpdateSnapshot(snapshot => snapshot with
            {
                State = PlaybackState.Error,
                IsPaused = true,
            });
            Publish(() => ErrorOccurred?.Invoke($"播放失败: {GetError(error)}"));
        }
    }

    public void LoadFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            UpdateSnapshot(snapshot => snapshot with
            {
                State = PlaybackState.Error,
                IsPaused = true,
            });
            Publish(() => ErrorOccurred?.Invoke($"文件不存在: {fullPath}"));
            return;
        }

        UpdateSnapshot(snapshot => snapshot with
        {
            FilePath = fullPath,
            State = PlaybackState.Loading,
            Position = 0,
            Duration = 0,
            IsPaused = true,
        });
        TryMpv(() => RequireMpv().Command("loadfile", fullPath, "replace"), "加载文件失败");
    }

    public void Play() => TryMpv(() => RequireMpv().SetProperty("pause", "no"), "播放失败");
    public void Pause() => TryMpv(() => RequireMpv().SetProperty("pause", "yes"), "暂停失败");

    public void TogglePause() => TryMpv(() =>
    {
        var mpv = RequireMpv();
        mpv.SetProperty("pause", mpv.GetPropertyFlag("pause") ? "no" : "yes");
    }, "切换播放/暂停失败");

    public void Seek(double positionSeconds) =>
        TryMpv(() => RequireMpv().Command("seek", FormatNumber(positionSeconds), "absolute", "exact"), "跳转失败");

    public void SeekFast(double positionSeconds) =>
        TryMpv(() => RequireMpv().Command("seek", FormatNumber(positionSeconds), "absolute+keyframes"), "跳转失败");

    public void SeekRelative(double offsetSeconds) =>
        TryMpv(() => RequireMpv().Command("seek", FormatNumber(offsetSeconds), "relative"), "跳转失败");

    public void SetVolume(double volume)
    {
        var normalized = Math.Clamp(volume, 0, 130);
        TryMpv(() =>
        {
            RequireMpv().SetProperty("volume", normalized.ToString("F0", CultureInfo.InvariantCulture));
            UpdateSnapshot(snapshot => snapshot with { Volume = normalized });
        }, "设置音量失败");
    }

    public void SetMute(bool mute) => TryMpv(() =>
    {
        RequireMpv().SetProperty("mute", mute ? "yes" : "no");
        UpdateSnapshot(snapshot => snapshot with { IsMuted = mute });
    }, "设置静音失败");

    public void ToggleMute() => TryMpv(() =>
    {
        var mpv = RequireMpv();
        var mute = !mpv.GetPropertyFlag("mute");
        mpv.SetProperty("mute", mute ? "yes" : "no");
        UpdateSnapshot(snapshot => snapshot with { IsMuted = mute });
    }, "切换静音失败");

    public void SetSpeed(double speed)
    {
        var normalized = NormalizeSpeed(speed);
        TryMpv(() =>
        {
            RequireMpv().SetProperty("speed", normalized.ToString(CultureInfo.InvariantCulture));
            UpdateSnapshot(snapshot => snapshot with { Speed = normalized });
        }, "设置播放速度失败");
    }

    public void ResetSpeed() => SetSpeed(1.0);

    public void Screenshot(string? path = null)
    {
        TryMpv(() =>
        {
            var mpv = RequireMpv();
            if (string.IsNullOrWhiteSpace(path))
            {
                mpv.Command("screenshot", "subtitles");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            mpv.Command("screenshot-to-file", Path.GetFullPath(path), "subtitles");
        }, "截图失败");
    }

    public VideoInfo? GetVideoInfo()
    {
        MpvContext? mpv;
        lock (_stateLock) mpv = _coreSession?.Context;
        if (mpv == null || !mpv.IsInitialized)
            return null;

        try
        {
            return new VideoInfo(
                mpv.GetPropertyString("filename"), mpv.GetPropertyString("file-format"), TryGetLong(mpv, "file-size"),
                TryGetLong(mpv, "video-params/dw") ?? TryGetLong(mpv, "width"), TryGetLong(mpv, "video-params/dh") ?? TryGetLong(mpv, "height"),
                mpv.GetPropertyString("video-codec"), mpv.GetPropertyString("hwdec-current"),
                TryGetDouble(mpv, "estimated-vf-fps") ?? TryGetDouble(mpv, "container-fps"), TryGetDouble(mpv, "video-bitrate"),
                mpv.GetPropertyString("video-params/pixelformat"), mpv.GetPropertyString("audio-codec-name"),
                TryGetLong(mpv, "audio-params/samplerate"), TryGetLong(mpv, "audio-params/channel-count"), TryGetDouble(mpv, "audio-bitrate"),
                RenderBackend);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetVideoInfo failed: {ex.Message}");
            return null;
        }
    }

    private void UpdateSpeed(double speed)
    {
        UpdateSnapshot(snapshot => snapshot with { Speed = speed });
    }

    private void UpdateHardwareDecode(string? value)
    {
        UpdateSnapshot(snapshot => snapshot with { HardwareDecode = value });
    }

    private void UpdateSnapshot(Func<PlaybackSnapshot, PlaybackSnapshot> update)
    {
        PlaybackSnapshot snapshot;
        lock (_stateLock)
        {
            var nextSnapshot = update(_snapshot);
            _snapshot = nextSnapshot with { Revision = _snapshot.Revision + 1 };
            snapshot = _snapshot;
        }

        Publish(() => SnapshotChanged?.Invoke(snapshot));
    }

    private void PublishWarning(string message) => Publish(() => WarningOccurred?.Invoke(message));
    private void Publish(Action action) => _dispatcher.Post(action);

    private void TryMpv(Action action, string context)
    {
        try { action(); }
        catch (MpvException ex)
        {
            UpdateSnapshot(snapshot => snapshot with
            {
                State = PlaybackState.Error,
                IsPaused = true,
            });
            Publish(() => ErrorOccurred?.Invoke($"{context}: {ex.Message}"));
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            UpdateSnapshot(snapshot => snapshot with
            {
                State = PlaybackState.Error,
                IsPaused = true,
            });
            Publish(() => ErrorOccurred?.Invoke($"{context}: {ex.Message}"));
        }
    }

    private MpvContext RequireMpv()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_stateLock)
            return _coreSession?.Context ?? throw new InvalidOperationException("播放器尚未初始化。");
    }

    private static string ToMpvHardwareDecode(HardwareDecodeMode mode) => mode switch
    {
        HardwareDecodeMode.Disabled => "no",
        HardwareDecodeMode.D3D11va => "d3d11va",
        HardwareDecodeMode.D3D11vaCopy => "d3d11va-copy",
        HardwareDecodeMode.Nvdec => "nvdec",
        HardwareDecodeMode.NvdecCopy => "nvdec-copy",
        HardwareDecodeMode.Vulkan => "vulkan",
        HardwareDecodeMode.VulkanCopy => "vulkan-copy",
        HardwareDecodeMode.Dxva2 => "dxva2",
        HardwareDecodeMode.Dxva2Copy => "dxva2-copy",
        _ => "auto",
    };

    private static double NormalizeSpeed(double value) => double.IsFinite(value) ? Math.Clamp(value, 0.1, 4.0) : 1.0;
    private static string FormatNumber(double value) => value.ToString("F2", CultureInfo.InvariantCulture);
    private static long? TryGetLong(MpvContext mpv, string name) => mpv.TryGetPropertyLong(name, out var value) ? value : null;
    private static double? TryGetDouble(MpvContext mpv, string name) => mpv.TryGetPropertyDouble(name, out var value) ? value : null;

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        MpvCoreSession? session;
        lock (_stateLock)
        {
            if (_disposed) return;
            _disposed = true;
            session = _coreSession;
            _coreSession = null;
        }

        if (session != null)
            await session.DisposeAsync().ConfigureAwait(false);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MpvEventEndFile
    {
        public MpvEndFileReason Reason;
        public int Error;
        public long PlaylistEntryId;
        public long PlaylistInsertId;
        public int PlaylistInsertNumEntries;
    }
}
