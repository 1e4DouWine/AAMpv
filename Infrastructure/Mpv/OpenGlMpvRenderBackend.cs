using System;
using System.Runtime.InteropServices;
using Avalonia.OpenGL;
using Avalonia.Threading;
using AvaloniaAppMPV.Core.Playback;
using static AvaloniaAppMPV.Infrastructure.Mpv.MpvInterop;

namespace AvaloniaAppMPV.Infrastructure.Mpv;

/// <summary>
/// libmpv OpenGL render API 后端。未来 D3D11/Vulkan 后端只需实现同一接口。
/// </summary>
public sealed class OpenGlMpvRenderBackend : IMpvRenderBackend
{
    private readonly object _lifecycleLock = new();
    private IntPtr _renderContext;
    private IntPtr _fboPtr;
    private IntPtr _flipPtr;
    private IntPtr _blockPtr;
    private IntPtr _renderParamsPtr;
    private MpvGetProcAddressFn? _getProcAddressDelegate;
    private MpvRenderUpdateFn? _updateCallbackDelegate;
    private Action? _requestNextFrame;
    private Action<string>? _reportError;

    public RenderBackendKind Kind => RenderBackendKind.OpenGL;
    private bool _isInitialized;

    public bool IsInitialized
    {
        get
        {
            lock (_lifecycleLock)
                return _isInitialized;
        }
    }

    public void Initialize(GlInterface gl, IntPtr mpvHandle, Action requestNextFrame, Action<string> reportError)
    {
        lock (_lifecycleLock)
        {
            if (_isInitialized)
                return;

            IntPtr glInitParamsPtr = IntPtr.Zero;
            IntPtr apiTypePtr = IntPtr.Zero;
            IntPtr advancedPtr = IntPtr.Zero;
            IntPtr paramsArray = IntPtr.Zero;

            try
            {
                _requestNextFrame = requestNextFrame;
                _reportError = reportError;
                _getProcAddressDelegate = (ctx, namePtr) =>
                {
                    var name = Marshal.PtrToStringUTF8(namePtr);
                    return name == null ? IntPtr.Zero : gl.GetProcAddress(name);
                };

                var glInitParams = new MpvOpenGlInitParams
                {
                    GetProcAddress = Marshal.GetFunctionPointerForDelegate(_getProcAddressDelegate),
                    GetProcAddressCtx = IntPtr.Zero,
                };
                glInitParamsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<MpvOpenGlInitParams>());
                Marshal.StructureToPtr(glInitParams, glInitParamsPtr, false);
                apiTypePtr = Marshal.StringToCoTaskMemUTF8("opengl");
                advancedPtr = Marshal.AllocHGlobal(sizeof(int));
                Marshal.WriteInt32(advancedPtr, 1);

                var paramSize = Marshal.SizeOf<MpvRenderParam>();
                paramsArray = Marshal.AllocHGlobal(paramSize * 4);
                WriteParam(paramsArray, 0, paramSize, MpvRenderParamType.ApiType, apiTypePtr);
                WriteParam(paramsArray, 1, paramSize, MpvRenderParamType.OpenGlInitParams, glInitParamsPtr);
                WriteParam(paramsArray, 2, paramSize, MpvRenderParamType.AdvancedControl, advancedPtr);
                WriteParam(paramsArray, 3, paramSize, MpvRenderParamType.Invalid, IntPtr.Zero);

                var err = mpv_render_context_create(out _renderContext, mpvHandle, paramsArray);
                if (err < 0)
                {
                    CleanupCore();
                    reportError($"创建 OpenGL mpv 渲染上下文失败: {GetError(err)}");
                    return;
                }

                _updateCallbackDelegate = OnMpvUpdateCallback;
                mpv_render_context_set_update_callback(
                    _renderContext,
                    Marshal.GetFunctionPointerForDelegate(_updateCallbackDelegate),
                    IntPtr.Zero);

                _fboPtr = Marshal.AllocHGlobal(Marshal.SizeOf<MpvOpenGlFbo>());
                _flipPtr = Marshal.AllocHGlobal(sizeof(int));
                Marshal.WriteInt32(_flipPtr, 1);
                _blockPtr = Marshal.AllocHGlobal(sizeof(int));
                // Avalonia controls the render cadence. Waiting for mpv's target
                // presentation time here can block the shared render path for a
                // full video frame interval (about 33 ms for 30 fps), making
                // other Avalonia windows stutter while they are being dragged.
                Marshal.WriteInt32(_blockPtr, 0);
                _renderParamsPtr = Marshal.AllocHGlobal(paramSize * 4);
                WriteParam(_renderParamsPtr, 1, paramSize, MpvRenderParamType.FlipY, _flipPtr);
                WriteParam(_renderParamsPtr, 2, paramSize, MpvRenderParamType.BlockForTargetTime, _blockPtr);
                WriteParam(_renderParamsPtr, 3, paramSize, MpvRenderParamType.Invalid, IntPtr.Zero);
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                CleanupCore();
                reportError($"初始化 OpenGL mpv 渲染失败: {ex.Message}");
            }
            finally
            {
                if (apiTypePtr != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(apiTypePtr);
                FreeIfAllocated(ref glInitParamsPtr);
                FreeIfAllocated(ref advancedPtr);
                FreeIfAllocated(ref paramsArray);
            }
        }
    }

    public void Render(int framebuffer, int width, int height)
    {
        string? error = null;
        lock (_lifecycleLock)
        {
            if (!_isInitialized || _renderContext == IntPtr.Zero || width <= 0 || height <= 0)
                return;

            var fbo = new MpvOpenGlFbo { Fbo = framebuffer, W = width, H = height, InternalFormat = 0 };
            Marshal.StructureToPtr(fbo, _fboPtr, false);
            var paramSize = Marshal.SizeOf<MpvRenderParam>();
            WriteParam(_renderParamsPtr, 0, paramSize, MpvRenderParamType.OpenGlFbo, _fboPtr);

            var err = mpv_render_context_render(_renderContext, _renderParamsPtr);
            if (err < 0)
                error = $"OpenGL 渲染失败: {GetError(err)}";
            mpv_render_context_report_swap(_renderContext);
        }

        if (error != null)
            _reportError?.Invoke(error);
    }

    private void OnMpvUpdateCallback(IntPtr ctx)
    {
        Action? requestNextFrame = null;
        ulong flags;
        lock (_lifecycleLock)
        {
            if (!_isInitialized || _renderContext == IntPtr.Zero)
                return;

            flags = mpv_render_context_update(_renderContext);
            if ((flags & MpvRenderUpdateFrame) != 0)
                requestNextFrame = _requestNextFrame;
        }

        if (requestNextFrame != null)
            Dispatcher.UIThread.Post(requestNextFrame, DispatcherPriority.Render);
    }

    public void Cleanup()
    {
        lock (_lifecycleLock)
            CleanupCore();
    }

    private void CleanupCore()
    {
        _isInitialized = false;
        if (_renderContext != IntPtr.Zero)
        {
            mpv_render_context_set_update_callback(_renderContext, IntPtr.Zero, IntPtr.Zero);
            mpv_render_context_free(_renderContext);
            _renderContext = IntPtr.Zero;
        }

        FreeIfAllocated(ref _fboPtr);
        FreeIfAllocated(ref _flipPtr);
        FreeIfAllocated(ref _blockPtr);
        FreeIfAllocated(ref _renderParamsPtr);
        _updateCallbackDelegate = null;
        _getProcAddressDelegate = null;
        _requestNextFrame = null;
        _reportError = null;
    }

    public void Dispose() => Cleanup();

    private static void WriteParam(IntPtr array, int index, int paramSize, MpvRenderParamType type, IntPtr data)
    {
        var offset = array + index * paramSize;
        Marshal.WriteInt32(offset, (int)type);
        Marshal.WriteIntPtr(offset + IntPtr.Size, data);
    }

    private static void FreeIfAllocated(ref IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
            return;

        Marshal.FreeHGlobal(ptr);
        ptr = IntPtr.Zero;
    }
}
