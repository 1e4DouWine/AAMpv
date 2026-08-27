namespace AvaloniaAppMPV.Core.Playback;

/// <summary>
/// 从 mpv 属性中读取出来的视频/音频信息快照。
/// </summary>
public record VideoInfo(
    string? FileName,
    string? FileFormat,
    long? FileSize,
    long? VideoWidth,
    long? VideoHeight,
    string? VideoCodec,
    string? HwDecCurrent,
    double? VideoFps,
    double? VideoBitrate,
    string? PixelFormat,
    string? AudioCodec,
    long? AudioSampleRate,
    long? AudioChannels,
    double? AudioBitrate,
    RenderBackendKind RenderBackend
);
