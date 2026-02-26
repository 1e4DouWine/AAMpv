using System;

namespace AvaloniaAppMPV.Models;

/// <summary>
/// Abstraction over mpv playback operations.
/// ViewModel depends on this interface, not on the concrete View.
/// </summary>
public interface IMpvPlayer
{
    // --- Events (fired on UI thread) ---
    event Action<double>? PositionChanged;
    event Action<double>? DurationChanged;
    event Action<bool>? PauseChanged;
    event Action<double>? VolumeChanged;
    event Action<bool>? MuteChanged;
    event Action<bool>? EofReached;
    event Action<string>? ErrorOccurred;

    // --- Commands ---
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
}
