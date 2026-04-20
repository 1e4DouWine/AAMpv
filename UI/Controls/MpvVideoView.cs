using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using AvaloniaAppMPV.Infrastructure.Mpv;
using static AvaloniaAppMPV.Infrastructure.Mpv.MpvInterop;

namespace AvaloniaAppMPV.UI.Controls;

/// <summary>
/// 基于 OpenGL 的视频控件。
/// mpv 不自己创建窗口，而是直接把画面渲染到 Avalonia 提供的 FBO 中。
/// </summary>
public class MpvVideoView : OpenGlControlBase
{
    private MpvPlayerService? _playerService;
    private IntPtr _renderContext;
    private MpvGetProcAddressFn? _getProcAddressDelegate;
    private MpvRenderUpdateFn? _updateCallbackDelegate;
    private volatile bool _initialized;

    // 这些原生缓冲区会在每帧复用，避免反复分配。
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
        if (_playerService == null || _initialized)
            return;

        IntPtr glInitParamsPtr = IntPtr.Zero;
        IntPtr apiTypePtr = IntPtr.Zero;
        IntPtr advancedPtr = IntPtr.Zero;
        IntPtr paramsArray = IntPtr.Zero;

        try
        {
            // 先初始化 mpv core，再创建与 OpenGL 绑定的 render context。
            _playerService.InitializeCore();

            // 保存委托引用，避免被 GC 回收后 mpv 回调到无效地址。
            _getProcAddressDelegate = GetProcAddress;
            var getProcFnPtr = Marshal.GetFunctionPointerForDelegate(_getProcAddressDelegate);

            var glInitParams = new MpvOpenGlInitParams
            {
                GetProcAddress = getProcFnPtr,
                GetProcAddressCtx = IntPtr.Zero,
            };
            glInitParamsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<MpvOpenGlInitParams>());
            Marshal.StructureToPtr(glInitParams, glInitParamsPtr, false);

            apiTypePtr = Marshal.StringToCoTaskMemUTF8("opengl");

            advancedPtr = Marshal.AllocHGlobal(sizeof(int));
            Marshal.WriteInt32(advancedPtr, 1);

            // 参数数组格式为：[api_type, gl_init, advanced_control, terminator]
            var paramSize = Marshal.SizeOf<MpvRenderParam>();
            paramsArray = Marshal.AllocHGlobal(paramSize * 4);

            WriteParam(paramsArray, 0, paramSize, MpvRenderParamType.ApiType, apiTypePtr);
            WriteParam(paramsArray, 1, paramSize, MpvRenderParamType.OpenGlInitParams, glInitParamsPtr);
            WriteParam(paramsArray, 2, paramSize, MpvRenderParamType.AdvancedControl, advancedPtr);
            WriteParam(paramsArray, 3, paramSize, MpvRenderParamType.Invalid, IntPtr.Zero);

            var mpvHandle = _playerService.MpvHandle;
            var err = mpv_render_context_create(out _renderContext, mpvHandle, paramsArray);

            if (err < 0)
            {
                CleanupRenderContext();
                _playerService.ReportError($"创建 mpv 渲染上下文失败: {GetError(err)}");
                return;
            }

            // mpv 有新帧需要绘制时，会通过这个回调通知 Avalonia 请求下一帧。
            _updateCallbackDelegate = OnMpvUpdateCallback;
            var updateFnPtr = Marshal.GetFunctionPointerForDelegate(_updateCallbackDelegate);
            mpv_render_context_set_update_callback(_renderContext, updateFnPtr, IntPtr.Zero);

            _fboPtr = Marshal.AllocHGlobal(Marshal.SizeOf<MpvOpenGlFbo>());
            _flipPtr = Marshal.AllocHGlobal(sizeof(int));
            Marshal.WriteInt32(_flipPtr, 1);
            _blockPtr = Marshal.AllocHGlobal(sizeof(int));
            Marshal.WriteInt32(_blockPtr, 1);
            _renderParamsPtr = Marshal.AllocHGlobal(paramSize * 4);

            // 除了 FBO 句柄和尺寸每帧都会变，其他参数都可以预填充。
            WriteParam(_renderParamsPtr, 1, paramSize, MpvRenderParamType.FlipY, _flipPtr);
            WriteParam(_renderParamsPtr, 2, paramSize, MpvRenderParamType.BlockForTargetTime, _blockPtr);
            WriteParam(_renderParamsPtr, 3, paramSize, MpvRenderParamType.Invalid, IntPtr.Zero);

            _initialized = true;
        }
        catch (Exception ex)
        {
            CleanupRenderContext();
            _playerService.ReportError($"初始化 mpv 渲染失败: {ex.Message}");
        }
        finally
        {
            if (apiTypePtr != IntPtr.Zero)
                Marshal.FreeCoTaskMem(apiTypePtr);
            FreeIfAllocated(ref glInitParamsPtr);
            FreeIfAllocated(ref advancedPtr);
            FreeIfAllocated(ref paramsArray);
        }

        IntPtr GetProcAddress(IntPtr ctx, IntPtr namePtr)
        {
            var name = Marshal.PtrToStringUTF8(namePtr);
            if (name == null)
                return IntPtr.Zero;

            return gl.GetProcAddress(name);
        }
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (!_initialized || _renderContext == IntPtr.Zero)
            return;

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var width = (int)(Bounds.Width * scaling);
        var height = (int)(Bounds.Height * scaling);
        if (width <= 0 || height <= 0)
            return;

        var fbo = new MpvOpenGlFbo { Fbo = fb, W = width, H = height, InternalFormat = 0 };
        Marshal.StructureToPtr(fbo, _fboPtr, false);

        var paramSize = Marshal.SizeOf<MpvRenderParam>();
        WriteParam(_renderParamsPtr, 0, paramSize, MpvRenderParamType.OpenGlFbo, _fboPtr);

        mpv_render_context_render(_renderContext, _renderParamsPtr);
        mpv_render_context_report_swap(_renderContext);
    }

    /// <summary>
    /// 显式释放 render context。
    /// 必须先释放它，再销毁 mpv core，避免原生层 use-after-free。
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

        FreeIfAllocated(ref _fboPtr);
        FreeIfAllocated(ref _flipPtr);
        FreeIfAllocated(ref _blockPtr);
        FreeIfAllocated(ref _renderParamsPtr);
        _updateCallbackDelegate = null;
        _getProcAddressDelegate = null;
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        // 如果窗口关闭前已经手动清理过，这里再次调用也是安全的。
        CleanupRenderContext();
    }

    private void OnMpvUpdateCallback(IntPtr ctx)
    {
        // _initialized 是 volatile，用来挡住已经释放后的回调。
        if (!_initialized)
            return;

        var rc = _renderContext;
        if (rc == IntPtr.Zero)
            return;

        var flags = mpv_render_context_update(rc);
        if ((flags & MpvRenderUpdateFrame) != 0)
            Dispatcher.UIThread.Post(RequestNextFrameRendering, DispatcherPriority.Render);
    }

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
