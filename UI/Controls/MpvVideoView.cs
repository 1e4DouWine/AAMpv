using System;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using AvaloniaAppMPV.Core.Playback;
using AvaloniaAppMPV.Infrastructure.Mpv;

namespace AvaloniaAppMPV.UI.Controls;

/// <summary>
/// Avalonia 视频承载控件。具体的 MPV 渲染实现由后端对象负责。
/// </summary>
public class MpvVideoView : OpenGlControlBase
{
    private MpvPlayerService? _playerService;
    private IMpvRenderBackend? _renderBackend;

    public void AttachPlayerService(MpvPlayerService playerService) => _playerService = playerService;

    protected override void OnOpenGlInit(GlInterface gl)
    {
        if (_playerService == null || _renderBackend?.IsInitialized == true)
            return;

        try
        {
            _playerService.InitializeCore();
            if (_playerService.RenderBackend is RenderBackendKind.Direct3D11 or RenderBackendKind.Vulkan)
                _playerService.ReportError($"渲染后端 {_playerService.RenderBackend} 尚未实现，已回退到 OpenGL。");
            _renderBackend = CreateBackend(_playerService.RenderBackend);
            _renderBackend.Initialize(
                gl,
                _playerService.MpvHandle,
                RequestNextFrameRendering,
                _playerService.ReportError);
        }
        catch (Exception ex)
        {
            _playerService.ReportError($"初始化视频渲染失败: {ex.Message}");
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

    private static IMpvRenderBackend CreateBackend(RenderBackendKind kind)
    {
        // D3D11/Vulkan 后端在当前阶段只有抽象，实际 Avalonia 承载尚未实现。
        return new OpenGlMpvRenderBackend();
    }
}
