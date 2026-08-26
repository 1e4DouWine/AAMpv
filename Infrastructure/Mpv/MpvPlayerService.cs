using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaAppMPV.Core.Playback;
using AvaloniaAppMPV.Infrastructure.Avalonia;
using static AvaloniaAppMPV.Infrastructure.Mpv.MpvInterop;

namespace AvaloniaAppMPV.Infrastructure.Mpv;

public sealed class MpvPlayerService : IMpvPlayer, IDisposable, IAsyncDisposable
{
    private readonly IDispatcherService _dispatcher;
    private readonly object _stateLock = new();
    private MpvContext? _mpv;
    private CancellationTokenSource? _cts;
    private Task? _eventLoopTask;
    private MpvPlayerSettings _settings = new();
    private bool _disposed;
    private PlaybackState _playbackState = PlaybackState.Unloaded;
    private string? _currentFilePath;
    private string? _currentHardwareDecode;
    private double _speed = 1.0;
    private RenderBackendKind _effectiveRenderBackend = RenderBackendKind.OpenGL;

    private const ulong ReplyTimePos = 1;
    private const ulong ReplyDuration = 2;
    private const ulong ReplyPause = 3;
    private const ulong ReplyVolume = 4;
    private const ulong ReplyEofReached = 5;
    private const ulong ReplyMute = 6;
    private const ulong ReplySpeed = 7;
    private const ulong ReplyHwDec = 8;

    public event Action<string?>? FileLoaded;
    public event Action<double>? PositionChanged;
    public event Action<double>? DurationChanged;
    public event Action<bool>? PauseChanged;
    public event Action<double>? VolumeChanged;
    public event Action<bool>? EofReached;
    public event Action<bool>? MuteChanged;
    public event Action<string>? ErrorOccurred;
    public event Action<PlaybackState>? PlaybackStateChanged;
    public event Action<double>? SpeedChanged;
    public event Action<string?>? HardwareDecodeChanged;
    public event Action<RenderBackendKind>? RenderBackendChanged;
    public event Action<string>? LogMessage;
    public event Action<string>? WarningOccurred;

    public bool IsReady
    {
        get { lock (_stateLock) return _mpv?.IsInitialized == true; }
    }

    public PlaybackState PlaybackState
    {
        get { lock (_stateLock) return _playbackState; }
    }

    public string? CurrentFilePath
    {
        get { lock (_stateLock) return _currentFilePath; }
    }

    public string? CurrentHardwareDecode
    {
        get { lock (_stateLock) return _currentHardwareDecode; }
    }

    public RenderBackendKind RenderBackend
    {
        get { lock (_stateLock) return _effectiveRenderBackend; }
    }

    public IntPtr MpvHandle
    {
        get { lock (_stateLock) return _mpv?.Handle ?? IntPtr.Zero; }
    }

    public MpvPlayerService(IDispatcherService dispatcher) => _dispatcher = dispatcher;

    public void Configure(MpvPlayerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_stateLock)
        {
            if (_mpv != null)
                throw new InvalidOperationException("播放器已经初始化，设置需要在初始化前修改。");
            _settings = settings.Clone();
            _settings.DefaultSpeed = NormalizeSpeed(_settings.DefaultSpeed);
            _settings.Volume = Math.Clamp(_settings.Volume, 0, 100);
            _effectiveRenderBackend = RenderBackendKind.OpenGL;
        }

        PublishRenderBackend(RenderBackend);
    }

    public void ReportError(string message) => Publish(() => ErrorOccurred?.Invoke(message));

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
                var settings = _settings.Clone();
                if (settings.RenderBackend is RenderBackendKind.Direct3D11 or RenderBackendKind.Vulkan)
                    PublishWarning($"渲染后端 {settings.RenderBackend} 尚未实现，当前使用 OpenGL。");
                mpv = new MpvContext();
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

                mpv.ObserveProperty("time-pos", MpvFormat.Double, ReplyTimePos);
                mpv.ObserveProperty("duration", MpvFormat.Double, ReplyDuration);
                mpv.ObserveProperty("pause", MpvFormat.Flag, ReplyPause);
                mpv.ObserveProperty("volume", MpvFormat.Double, ReplyVolume);
                mpv.ObserveProperty("eof-reached", MpvFormat.Flag, ReplyEofReached);
                mpv.ObserveProperty("mute", MpvFormat.Flag, ReplyMute);
                mpv.ObserveProperty("speed", MpvFormat.Double, ReplySpeed);
                mpv.ObserveProperty("hwdec-current", MpvFormat.String, ReplyHwDec);
                mpv.RequestLogMessages("warn");
                mpv.InstallWakeupCallback();

                cts = new CancellationTokenSource();
                _mpv = mpv;
                _cts = cts;
                _speed = settings.DefaultSpeed;
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

        PublishState(PlaybackState.Unloaded);
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

    private void EventLoop(MpvContext mpv, CancellationToken ct)
    {
        var signal = mpv.WakeupSignal;
        while (!ct.IsCancellationRequested)
        {
            try { signal.Wait(ct); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            signal.Reset();

            while (!ct.IsCancellationRequested)
            {
                var ev = mpv.WaitEvent(0);
                if (ev == null || ev.Value.EventId == MpvEventId.None)
                    break;

                switch (ev.Value.EventId)
                {
                    case MpvEventId.Shutdown:
                        return;
                    case MpvEventId.StartFile:
                        PublishState(PlaybackState.Loading);
                        break;
                    case MpvEventId.FileLoaded:
                        HandleFileLoaded(mpv);
                        break;
                    case MpvEventId.PropertyChange:
                        HandlePropertyChange(ev.Value);
                        break;
                    case MpvEventId.LogMessage:
                        HandleLogMessage(ev.Value);
                        break;
                    case MpvEventId.EndFile:
                        HandleEndFile(ev.Value);
                        break;
                }
            }
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

        lock (_stateLock)
            _currentFilePath = fileName;
        PublishState(PlaybackState.Paused);
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
                Publish(() => PositionChanged?.Invoke(position)); break;
            case ReplyDuration when prop.Format == MpvFormat.Double && prop.Data != IntPtr.Zero:
                var duration = *(double*)prop.Data;
                Publish(() => DurationChanged?.Invoke(duration)); break;
            case ReplyPause when prop.Format == MpvFormat.Flag && prop.Data != IntPtr.Zero:
                HandlePause(*(int*)prop.Data != 0); break;
            case ReplyVolume when prop.Format == MpvFormat.Double && prop.Data != IntPtr.Zero:
                var volume = *(double*)prop.Data;
                Publish(() => VolumeChanged?.Invoke(volume)); break;
            case ReplyEofReached when prop.Format == MpvFormat.Flag && prop.Data != IntPtr.Zero:
                var eof = *(int*)prop.Data != 0;
                Publish(() => EofReached?.Invoke(eof)); break;
            case ReplyMute when prop.Format == MpvFormat.Flag && prop.Data != IntPtr.Zero:
                var mute = *(int*)prop.Data != 0;
                Publish(() => MuteChanged?.Invoke(mute)); break;
            case ReplySpeed when prop.Format == MpvFormat.Double && prop.Data != IntPtr.Zero:
                UpdateSpeed(*(double*)prop.Data); break;
            case ReplyHwDec when prop.Format == MpvFormat.String && prop.Data != IntPtr.Zero:
                UpdateHardwareDecode(Marshal.PtrToStringUTF8(*(IntPtr*)prop.Data)); break;
        }
    }

    private void HandlePause(bool paused)
    {
        Publish(() => PauseChanged?.Invoke(paused));
        if (PlaybackState is PlaybackState.Loading or PlaybackState.Ended or PlaybackState.Unloaded)
            return;
        PublishState(paused ? PlaybackState.Paused : PlaybackState.Playing);
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
                PublishState(PlaybackState.Ended);
                Publish(() => EofReached?.Invoke(true));
                return;
            }
        }

        if (error < 0)
        {
            PublishState(PlaybackState.Error);
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
            PublishState(PlaybackState.Error);
            Publish(() => ErrorOccurred?.Invoke($"文件不存在: {fullPath}"));
            return;
        }

        lock (_stateLock) _currentFilePath = fullPath;
        PublishState(PlaybackState.Loading);
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

    public void SetVolume(double volume) =>
        TryMpv(() => RequireMpv().SetProperty("volume", Math.Clamp(volume, 0, 130).ToString("F0", CultureInfo.InvariantCulture)), "设置音量失败");

    public void SetMute(bool mute) =>
        TryMpv(() => RequireMpv().SetProperty("mute", mute ? "yes" : "no"), "设置静音失败");

    public void ToggleMute() => TryMpv(() =>
    {
        var mpv = RequireMpv();
        mpv.SetProperty("mute", mpv.GetPropertyFlag("mute") ? "no" : "yes");
    }, "切换静音失败");

    public void SetSpeed(double speed) =>
        TryMpv(() => RequireMpv().SetProperty("speed", NormalizeSpeed(speed).ToString(CultureInfo.InvariantCulture)), "设置播放速度失败");

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
        lock (_stateLock) mpv = _mpv;
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
        lock (_stateLock) _speed = speed;
        Publish(() => SpeedChanged?.Invoke(speed));
    }

    private void UpdateHardwareDecode(string? value)
    {
        lock (_stateLock) _currentHardwareDecode = value;
        Publish(() => HardwareDecodeChanged?.Invoke(value));
    }

    private void PublishState(PlaybackState state)
    {
        lock (_stateLock) _playbackState = state;
        Publish(() => PlaybackStateChanged?.Invoke(state));
    }

    private void PublishRenderBackend(RenderBackendKind backend) => Publish(() => RenderBackendChanged?.Invoke(backend));
    private void PublishWarning(string message) => Publish(() => WarningOccurred?.Invoke(message));
    private void Publish(Action action) => _dispatcher.Post(action);

    private void TryMpv(Action action, string context)
    {
        try { action(); }
        catch (MpvException ex)
        {
            PublishState(PlaybackState.Error);
            Publish(() => ErrorOccurred?.Invoke($"{context}: {ex.Message}"));
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            PublishState(PlaybackState.Error);
            Publish(() => ErrorOccurred?.Invoke($"{context}: {ex.Message}"));
        }
    }

    private MpvContext RequireMpv()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_stateLock) return _mpv ?? throw new InvalidOperationException("播放器尚未初始化。");
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
        CancellationTokenSource? cts;
        Task? eventLoopTask;
        MpvContext? mpv;
        lock (_stateLock)
        {
            if (_disposed) return;
            _disposed = true;
            cts = _cts; eventLoopTask = _eventLoopTask; mpv = _mpv;
            _cts = null; _eventLoopTask = null; _mpv = null;
        }

        cts?.Cancel();
        mpv?.WakeupSignal.Set();
        try { if (eventLoopTask != null) await eventLoopTask.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        mpv?.Dispose();
        cts?.Dispose();
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
