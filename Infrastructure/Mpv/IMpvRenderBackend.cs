using System;
using Avalonia.OpenGL;
using AvaloniaAppMPV.Core.Playback;

namespace AvaloniaAppMPV.Infrastructure.Mpv;

public interface IMpvRenderBackend : IDisposable
{
    RenderBackendKind Kind { get; }
    bool IsInitialized { get; }

    void Initialize(GlInterface gl, IntPtr mpvHandle, Action requestNextFrame, Action<string> reportError);
    void Render(int framebuffer, int width, int height);
    void Cleanup();
}
