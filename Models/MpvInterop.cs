using System;
using System.Runtime.InteropServices;

namespace AvaloniaAppMPV.Models;

public static partial class MpvInterop
{
    private const string LibMpv = "libmpv-2";

    // --- client.h ---

    [LibraryImport(LibMpv, EntryPoint = "mpv_create")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial IntPtr mpv_create();

    [LibraryImport(LibMpv, EntryPoint = "mpv_initialize")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int mpv_initialize(IntPtr ctx);

    [LibraryImport(LibMpv, EntryPoint = "mpv_terminate_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void mpv_terminate_destroy(IntPtr ctx);

    [LibraryImport(LibMpv, EntryPoint = "mpv_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void mpv_destroy(IntPtr ctx);

    [LibraryImport(LibMpv, EntryPoint = "mpv_command")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int mpv_command(IntPtr ctx, IntPtr[] args);

    [LibraryImport(LibMpv, EntryPoint = "mpv_command")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static unsafe partial int mpv_command_ptr(IntPtr ctx, IntPtr* args);

    [LibraryImport(LibMpv, EntryPoint = "mpv_command_string", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int mpv_command_string(IntPtr ctx, string args);

    [LibraryImport(LibMpv, EntryPoint = "mpv_set_option_string", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int mpv_set_option_string(IntPtr ctx, string name, string data);

    [LibraryImport(LibMpv, EntryPoint = "mpv_set_property_string", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int mpv_set_property_string(IntPtr ctx, string name, string data);

    [LibraryImport(LibMpv, EntryPoint = "mpv_get_property", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int mpv_get_property(IntPtr ctx, string name, MpvFormat format, out int data);

    [LibraryImport(LibMpv, EntryPoint = "mpv_get_property", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int mpv_get_property(IntPtr ctx, string name, MpvFormat format, out long data);

    [LibraryImport(LibMpv, EntryPoint = "mpv_get_property", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int mpv_get_property(IntPtr ctx, string name, MpvFormat format, out double data);

    [LibraryImport(LibMpv, EntryPoint = "mpv_get_property", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int mpv_get_property(IntPtr ctx, string name, MpvFormat format, out IntPtr data);

    [LibraryImport(LibMpv, EntryPoint = "mpv_observe_property", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int mpv_observe_property(IntPtr mpv, ulong reply_userdata, string name, MpvFormat format);

    [LibraryImport(LibMpv, EntryPoint = "mpv_wait_event")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial IntPtr mpv_wait_event(IntPtr ctx, double timeout);

    [LibraryImport(LibMpv, EntryPoint = "mpv_set_wakeup_callback")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void mpv_set_wakeup_callback(IntPtr ctx, IntPtr cb, IntPtr d);

    [LibraryImport(LibMpv, EntryPoint = "mpv_free")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void mpv_free(IntPtr data);

    [LibraryImport(LibMpv, EntryPoint = "mpv_error_string")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial IntPtr mpv_error_string(int error);

    // --- render.h ---

    [LibraryImport(LibMpv, EntryPoint = "mpv_render_context_create")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int mpv_render_context_create(out IntPtr res, IntPtr mpv, IntPtr @params);

    [LibraryImport(LibMpv, EntryPoint = "mpv_render_context_render")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int mpv_render_context_render(IntPtr ctx, IntPtr @params);

    [LibraryImport(LibMpv, EntryPoint = "mpv_render_context_set_update_callback")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void mpv_render_context_set_update_callback(IntPtr ctx, IntPtr callback, IntPtr callback_ctx);

    [LibraryImport(LibMpv, EntryPoint = "mpv_render_context_update")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial ulong mpv_render_context_update(IntPtr ctx);

    [LibraryImport(LibMpv, EntryPoint = "mpv_render_context_free")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void mpv_render_context_free(IntPtr ctx);

    [LibraryImport(LibMpv, EntryPoint = "mpv_render_context_report_swap")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void mpv_render_context_report_swap(IntPtr ctx);

    // --- Enums ---

    public enum MpvFormat
    {
        None = 0,
        String = 1,
        OsdString = 2,
        Flag = 3,
        Int64 = 4,
        Double = 5,
        Node = 6,
    }

    public enum MpvEventId
    {
        None = 0,
        Shutdown = 1,
        LogMessage = 2,
        GetPropertyReply = 3,
        SetPropertyReply = 4,
        CommandReply = 5,
        StartFile = 6,
        EndFile = 7,
        FileLoaded = 8,
        VideoReconfig = 17,
        AudioReconfig = 18,
        Seek = 20,
        PlaybackRestart = 21,
        PropertyChange = 22,
        QueueOverflow = 24,
    }

    // --- Structs ---

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEvent
    {
        public MpvEventId EventId;
        public int Error;
        public ulong ReplyUserdata;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEventProperty
    {
        public IntPtr Name;
        public MpvFormat Format;
        public IntPtr Data;
    }

    // --- Render API enums & structs ---

    public enum MpvRenderParamType
    {
        Invalid = 0,
        ApiType = 1,
        OpenGlInitParams = 2,
        OpenGlFbo = 3,
        FlipY = 4,
        Depth = 5,
        IccProfile = 6,
        AmbientLight = 7,
        X11Display = 8,
        WlDisplay = 9,
        AdvancedControl = 10,
        NextFrameInfo = 11,
        BlockForTargetTime = 12,
        SkipRendering = 13,
    }

    public const ulong MpvRenderUpdateFrame = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvOpenGlInitParams
    {
        public IntPtr GetProcAddress; // delegate* unmanaged<IntPtr, IntPtr, IntPtr>
        public IntPtr GetProcAddressCtx;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvOpenGlFbo
    {
        public int Fbo;
        public int W;
        public int H;
        public int InternalFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvRenderParam
    {
        public MpvRenderParamType Type;
        public IntPtr Data;
    }

    /// <summary>
    /// Delegate matching mpv_set_wakeup_callback signature: void (*cb)(void *d)
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void MpvWakeupCallbackFn(IntPtr d);

    /// <summary>
    /// Delegate matching mpv_opengl_init_params.get_proc_address signature.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr MpvGetProcAddressFn(IntPtr ctx, IntPtr name);

    /// <summary>
    /// Delegate matching mpv_render_update_fn signature.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void MpvRenderUpdateFn(IntPtr ctx);

    // --- Helpers ---

    public static IntPtr AllocUtf8(string s) => Marshal.StringToCoTaskMemUTF8(s);

    public static void FreeUtf8(IntPtr p) => Marshal.FreeCoTaskMem(p);

    public static string GetError(int code)
    {
        var ptr = mpv_error_string(code);
        return Marshal.PtrToStringUTF8(ptr) ?? $"Unknown error {code}";
    }
}
