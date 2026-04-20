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

    /// <summary>
    /// 读取当前媒体的基础信息。
    /// </summary>
    VideoInfo? GetVideoInfo();
}
