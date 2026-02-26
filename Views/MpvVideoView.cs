using System;
using System.Runtime.InteropServices;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using AvaloniaAppMPV.Models;
using AvaloniaAppMPV.Services;
using static AvaloniaAppMPV.Models.MpvInterop;

namespace AvaloniaAppMPV.Views;

/// <summary>
/// OpenGL-based video control. mpv renders into Avalonia's FBO via mpv_render_context.
/// Replaces the old NativeControlHost + wid approach.
/// </summary>
public class MpvVideoView : OpenGlControlBase
{
    private MpvPlayerService? _playerService;
    private IntPtr _renderContext;
    private MpvGetProcAddressFn? _getProcAddressDelegate;
    private MpvRenderUpdateFn? _updateCallbackDelegate;
    private volatile bool _initialized;

    // Pre-allocated native buffers for OnOpenGlRender (reused every frame)
    private IntPtr _fboPtr;
    private IntPtr _flipPtr;
    private IntPtr _blockPtr;
    private IntPtr _renderParamsPtr;

    public void AttachPlayerService(MpvPlayerService playerService)
    {
        _playerService = playerService;
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        if (_playerService == null) return;

        // Initialize mpv core (without wid)
        _playerService.InitializeCore();

        // Keep delegate alive to prevent GC
        _getProcAddressDelegate = GetProcAddress;
        var getProcFnPtr = Marshal.GetFunctionPointerForDelegate(_getProcAddressDelegate);

        // Build mpv_opengl_init_params
        var glInitParams = new MpvOpenGlInitParams
        {
            GetProcAddress = getProcFnPtr,
            GetProcAddressCtx = IntPtr.Zero,
        };
        var glInitParamsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<MpvOpenGlInitParams>());
        Marshal.StructureToPtr(glInitParams, glInitParamsPtr, false);

        // Build api type string
        var apiTypePtr = Marshal.StringToCoTaskMemUTF8("opengl");

        // Build advanced control flag
        var advancedPtr = Marshal.AllocHGlobal(sizeof(int));
        Marshal.WriteInt32(advancedPtr, 1);

        // Build params array: [api_type, gl_init, advanced_control, terminator]
        var paramSize = Marshal.SizeOf<MpvRenderParam>();
        var paramsArray = Marshal.AllocHGlobal(paramSize * 4);

        WriteParam(paramsArray, 0, paramSize, MpvRenderParamType.ApiType, apiTypePtr);
        WriteParam(paramsArray, 1, paramSize, MpvRenderParamType.OpenGlInitParams, glInitParamsPtr);
        WriteParam(paramsArray, 2, paramSize, MpvRenderParamType.AdvancedControl, advancedPtr);
        WriteParam(paramsArray, 3, paramSize, MpvRenderParamType.Invalid, IntPtr.Zero);

        // Create render context
        var mpvHandle = _playerService.MpvHandle;
        var err = mpv_render_context_create(out _renderContext, mpvHandle, paramsArray);

        // Free temp allocations
        Marshal.FreeCoTaskMem(apiTypePtr);
        Marshal.FreeHGlobal(glInitParamsPtr);
        Marshal.FreeHGlobal(advancedPtr);
        Marshal.FreeHGlobal(paramsArray);

        if (err < 0)
        {
            _playerService.ReportError($"mpv_render_context_create failed: {GetError(err)}");
            return;
        }

        // Set update callback — mpv calls this when a new frame is ready
        _updateCallbackDelegate = OnMpvUpdateCallback;
        var updateFnPtr = Marshal.GetFunctionPointerForDelegate(_updateCallbackDelegate);
        mpv_render_context_set_update_callback(_renderContext, updateFnPtr, IntPtr.Zero);

        // Pre-allocate native buffers for per-frame render params
        _fboPtr = Marshal.AllocHGlobal(Marshal.SizeOf<MpvOpenGlFbo>());
        _flipPtr = Marshal.AllocHGlobal(sizeof(int));
        Marshal.WriteInt32(_flipPtr, 1);
        _blockPtr = Marshal.AllocHGlobal(sizeof(int));
        Marshal.WriteInt32(_blockPtr, 1);
        _renderParamsPtr = Marshal.AllocHGlobal(paramSize * 4);

        // Pre-fill static params (FlipY, BlockForTargetTime, terminator)
        WriteParam(_renderParamsPtr, 1, paramSize, MpvRenderParamType.FlipY, _flipPtr);
        WriteParam(_renderParamsPtr, 2, paramSize, MpvRenderParamType.BlockForTargetTime, _blockPtr);
        WriteParam(_renderParamsPtr, 3, paramSize, MpvRenderParamType.Invalid, IntPtr.Zero);

        _initialized = true;

        // Store render context in service for cleanup coordination
        _playerService.SetRenderContext(_renderContext);

        return;

        // Local: resolve GL function by name for mpv
        IntPtr GetProcAddress(IntPtr ctx, IntPtr namePtr)
        {
            var name = Marshal.PtrToStringUTF8(namePtr);
            if (name == null) return IntPtr.Zero;
            return gl.GetProcAddress(name);
        }
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (!_initialized || _renderContext == IntPtr.Zero) return;

        var scaling = VisualRoot?.RenderScaling ?? 1.0;
        var width = (int)(Bounds.Width * scaling);
        var height = (int)(Bounds.Height * scaling);
        if (width <= 0 || height <= 0) return;

        // Update FBO struct in pre-allocated buffer (Fbo/size may change each frame)
        var fbo = new MpvOpenGlFbo { Fbo = fb, W = width, H = height, InternalFormat = 0 };
        Marshal.StructureToPtr(fbo, _fboPtr, false);

        // Update slot 0 (FBO changes per frame; slots 1-3 are pre-filled and static)
        var paramSize = Marshal.SizeOf<MpvRenderParam>();
        WriteParam(_renderParamsPtr, 0, paramSize, MpvRenderParamType.OpenGlFbo, _fboPtr);

        mpv_render_context_render(_renderContext, _renderParamsPtr);
        mpv_render_context_report_swap(_renderContext);
    }

    /// <summary>
    /// Explicitly free the render context. Must be called BEFORE mpv_terminate_destroy
    /// to avoid use-after-free crashes. Safe to call from the UI thread.
    /// OnOpenGlDeinit will skip the free if already done here.
    /// </summary>
    public void CleanupRenderContext()
    {
        _initialized = false;

        if (_renderContext != IntPtr.Zero)
        {
            mpv_render_context_set_update_callback(_renderContext, IntPtr.Zero, IntPtr.Zero);
            mpv_render_context_free(_renderContext);
            _renderContext = IntPtr.Zero;
        }

        // Free pre-allocated render buffers
        FreeIfAllocated(ref _fboPtr);
        FreeIfAllocated(ref _flipPtr);
        FreeIfAllocated(ref _blockPtr);
        FreeIfAllocated(ref _renderParamsPtr);
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        // CleanupRenderContext may have already freed everything; this is safe to call again.
        CleanupRenderContext();
    }

    private void OnMpvUpdateCallback(IntPtr ctx)
    {
        // Guard: _initialized is volatile, prevents calling into a freed render context
        if (!_initialized) return;

        var rc = _renderContext;
        if (rc == IntPtr.Zero) return;

        var flags = mpv_render_context_update(rc);
        if ((flags & MpvRenderUpdateFrame) != 0)
            Dispatcher.UIThread.Post(RequestNextFrameRendering, DispatcherPriority.Render);
    }

    private static void WriteParam(IntPtr array, int index, int paramSize,
        MpvRenderParamType type, IntPtr data)
    {
        var offset = array + index * paramSize;
        Marshal.WriteInt32(offset, (int)type);
        Marshal.WriteIntPtr(offset + IntPtr.Size, data);
    }

    private static void FreeIfAllocated(ref IntPtr ptr)
    {
        if (ptr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(ptr);
            ptr = IntPtr.Zero;
        }
    }
}
