// WebRtcInterop —— C API wrapper（libpicowebrtc.so，extern "C"）的 P/Invoke 绑定。
// 需在 Player Settings / asmdef defineConstraints 定义 WEBRTC_NATIVE 且提供预编译 .so 后才编入调用。
// C wrapper 契约见 Exp-WebRTC/native/picowebrtc.h。首个原生插件（见计划/decisions）。
using System;
using System.Runtime.InteropServices;

namespace PicoTest.Experiments.WebRTC.Native
{
#if WEBRTC_NATIVE
    /// <summary>解码帧回调（原生解码线程）：RGBA(SBS) data + 字节数 + 宽高 + 时间戳(ns)。禁在此做 JNI/Unity 调用。</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void WrtcFrameCallback(IntPtr data, int size, int width, int height, long tsNs, IntPtr user);

    /// <summary>SDP/candidate 出站回调（本端产生，需经信令发给对端）。</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void WrtcSignalCallback(int kind, IntPtr text, IntPtr user); // kind: 0=offer 1=answer 2=candidate

    public static class WebRtcInterop
    {
        private const string Lib = "picowebrtc";

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr wrtc_create(WrtcFrameCallback onFrame, WrtcSignalCallback onSignal, IntPtr user);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int wrtc_start(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string offerSdpOrNull);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void wrtc_set_remote_sdp(IntPtr handle, int kind, [MarshalAs(UnmanagedType.LPStr)] string sdp);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void wrtc_add_ice(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string cand,
            [MarshalAs(UnmanagedType.LPStr)] string mid, int mlineIndex);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void wrtc_close(IntPtr handle);
    }
#endif
}
