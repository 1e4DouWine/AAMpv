using System;

namespace AvaloniaAppMPV.Core.Playback;

/// <summary>
/// 播放能力抽象。
/// ViewModel 只依赖这个接口，不直接依赖具体的 mpv 实现。
/// </summary>
public interface IMpvPlayer
{
    // 这些事件统一在 UI 线程触发，方便界面层直接绑定。
    event Action<string?>? FileLoaded;
    event Action<double>? PositionChanged;
    event Action<double>? DurationChanged;
    event Action<bool>? PauseChanged;
    event Action<double>? VolumeChanged;
    event Action<bool>? MuteChanged;
    event Action<bool>? EofReached;
    event Action<string>? ErrorOccurred;
    event Action<PlaybackState>? PlaybackStateChanged;
    event Action<double>? SpeedChanged;
    event Action<string?>? HardwareDecodeChanged;
    event Action<RenderBackendKind>? RenderBackendChanged;
    event Action<string>? LogMessage;
    event Action<string>? WarningOccurred;

    PlaybackState PlaybackState { get; }
    string? CurrentFilePath { get; }
    string? CurrentHardwareDecode { get; }
    RenderBackendKind RenderBackend { get; }

    void Configure(MpvPlayerSettings settings);

    void LoadFile(string path);
    void Play();
    void Pause();
    void TogglePause();
    void Seek(double positionSeconds);
    void SeekFast(double positionSeconds);
    void SeekRelative(double offsetSeconds);
    void SetVolume(double volume);
    void SetMute(bool mute);
    void ToggleMute();
    void SetSpeed(double speed);
    void ResetSpeed();
    void Screenshot(string? path = null);

    /// <summary>
    /// 读取当前媒体的基础信息。
    /// </summary>
    VideoInfo? GetVideoInfo();
}
