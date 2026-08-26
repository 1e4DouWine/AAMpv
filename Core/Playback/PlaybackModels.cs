using System;
using System.Collections.Generic;
using System.IO;

namespace AvaloniaAppMPV.Core.Playback;

public enum PlaybackState
{
    Unloaded,
    Loading,
    Playing,
    Paused,
    Ended,
    Error,
}

public enum RenderBackendKind
{
    Auto,
    OpenGL,
    Direct3D11,
    Vulkan,
}

public enum HardwareDecodeMode
{
    Auto,
    Disabled,
    D3D11va,
    D3D11vaCopy,
    Nvdec,
    NvdecCopy,
    Vulkan,
    VulkanCopy,
    Dxva2,
    Dxva2Copy,
}

public sealed record PlaybackSnapshot(
    PlaybackState State,
    string? FilePath,
    double Position,
    double Duration,
    bool IsPaused,
    double Volume,
    bool IsMuted,
    double Speed,
    string? HardwareDecode,
    RenderBackendKind RenderBackend)
{
    public static PlaybackSnapshot Empty { get; } = new(
        PlaybackState.Unloaded,
        null,
        0,
        0,
        true,
        100,
        false,
        1.0,
        null,
        RenderBackendKind.OpenGL);
}

public sealed class MpvPlayerSettings
{
    public HardwareDecodeMode HardwareDecode { get; set; } = HardwareDecodeMode.Auto;
    public RenderBackendKind RenderBackend { get; set; } = RenderBackendKind.Auto;
    public bool UseCustomMpvConfig { get; set; }
    public string? MpvConfigPath { get; set; }
    public string ScreenshotDirectory { get; set; } = string.Empty;
    public double DefaultSpeed { get; set; } = 1.0;
    public bool RememberPlaybackPosition { get; set; } = true;
    public double Volume { get; set; } = 100;
    public bool IsMuted { get; set; }

    public MpvPlayerSettings Clone() => new()
    {
        HardwareDecode = HardwareDecode,
        RenderBackend = RenderBackend,
        UseCustomMpvConfig = UseCustomMpvConfig,
        MpvConfigPath = MpvConfigPath,
        ScreenshotDirectory = ScreenshotDirectory,
        DefaultSpeed = DefaultSpeed,
        RememberPlaybackPosition = RememberPlaybackPosition,
        Volume = Volume,
        IsMuted = IsMuted,
    };
}

public sealed class RecentMediaEntry
{
    public string Path { get; set; } = string.Empty;
    public DateTime LastOpenedUtc { get; set; }
    public double Position { get; set; }

    public bool Exists => File.Exists(Path);
}

public sealed class PlayerSettingsDocument
{
    public int Version { get; set; } = 1;
    public MpvPlayerSettings Player { get; set; } = new();
    public List<RecentMediaEntry> RecentFiles { get; set; } = [];
}
