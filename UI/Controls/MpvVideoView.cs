using System;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using AvaloniaAppMPV.Infrastructure.Mpv;

namespace AvaloniaAppMPV.UI.Controls;

/// <summary>
/// Avalonia 视频承载控件。具体的 MPV 渲染实现由后端对象负责。
/// </summary>
public class MpvVideoView : OpenGlControlBase
{
    private IMpvRenderHost? _renderHost;
    private IMpvRenderBackend? _renderBackend;

    public void AttachRenderHost(IMpvRenderHost renderHost) => _renderHost = renderHost;

    protected override void OnOpenGlInit(GlInterface gl)
    {
        if (_renderHost == null || _renderBackend?.IsInitialized == true)
            return;

        try
        {
            _renderHost.InitializeCore();
            // 当前承载控件只实现 OpenGL；后续增加其他后端时再引入真正的工厂。
            _renderBackend = new OpenGlMpvRenderBackend();
            _renderBackend.Initialize(
                gl,
                _renderHost.MpvHandle,
                RequestNextFrameRendering,
                _renderHost.ReportError);
        }
        catch (Exception ex)
        {
            _renderHost?.ReportError($"初始化视频渲染失败: {ex.Message}");
            CleanupRenderContext();
        }
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_renderBackend?.IsInitialized != true)
            return;

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var width = (int)(Bounds.Width * scaling);
        var height = (int)(Bounds.Height * scaling);
        _renderBackend.Render(fb, width, height);
    }

    public void CleanupRenderContext()
    {
        _renderBackend?.Cleanup();
        _renderBackend?.Dispose();
        _renderBackend = null;
    }

    protected override void OnOpenGlDeinit(GlInterface gl) => CleanupRenderContext();

}
