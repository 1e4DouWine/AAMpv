using System;
using Avalonia.Controls;
using Avalonia.Platform;
using AvaloniaAppMPV.Services;

namespace AvaloniaAppMPV.Views;

/// <summary>
/// Pure NativeControlHost — provides the native window handle to MpvPlayerService.
/// No mpv logic lives here anymore.
/// </summary>
public class MpvVideoView : NativeControlHost
{
    private MpvPlayerService? _playerService;

    public void AttachPlayerService(MpvPlayerService playerService)
    {
        _playerService = playerService;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle = base.CreateNativeControlCore(parent);
        _playerService?.Initialize(handle.Handle);
        return handle;
    }
}
