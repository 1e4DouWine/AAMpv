using System;
using AvaloniaAppMPV.Core.Playback;

namespace AvaloniaAppMPV.Infrastructure.Mpv;

/// <summary>
/// 视频承载控件需要的最小 MPV 渲染宿主契约。
/// UI 不需要知道具体的播放服务类型，只获取 render context 所需的信息。
/// </summary>
public interface IMpvRenderHost
{
    IntPtr MpvHandle { get; }
    RenderBackendKind RenderBackend { get; }

    void InitializeCore();
    void ReportError(string message);
}
