using System;
using UnityEngine;
using PicoTest.Experiments.WebRTC.Signaling;
#if WEBRTC_NATIVE
using System.Runtime.InteropServices;
using PicoTest.Experiments.WebRTC.Native;
#endif

namespace PicoTest.Experiments.WebRTC
{
    /// <summary>
    /// 真实 WebRTC 视频源：经 C wrapper(libpicowebrtc) + libwebrtc 建立 PeerConnection，
    /// 收远端机器人双目鱼眼流，wrapper 内 libyuv 转 RGBA(SBS)，帧回调(原生线程)转发到 OnFrame。
    /// 信令由注入的 <see cref="ISignaling"/> 交换 offer/answer/candidate。
    /// 需定义 WEBRTC_NATIVE 且提供预编译 .so —— 否则 Start() 仅告警（M0 用 FakeStereoVideoSource 兜底）。
    /// </summary>
    public sealed class WebRtcVideoSource : IWebRtcVideoSource
    {
        public event Action<IntPtr, int, int, int> OnFrame;

        private readonly ISignaling _signaling;
        private readonly string _signalUrl;

        public WebRtcVideoSource(ISignaling signaling, string signalUrl)
        {
            _signaling = signaling;
            _signalUrl = signalUrl;
        }

#if WEBRTC_NATIVE
        private IntPtr _handle;
        private WrtcFrameCallback _frameCb;   // 保持引用防 GC
        private WrtcSignalCallback _signalCb;

        public void Start()
        {
            _frameCb = OnNativeFrame;
            _signalCb = OnNativeSignal;
            _handle = WebRtcInterop.wrtc_create(_frameCb, _signalCb, IntPtr.Zero);

            _signaling.OnMessage += OnSignalingMessage;
            _signaling.OnError += e => Debug.LogWarning($"[WebRtc] signaling: {e}");
            _signaling.OnConnected += () => WebRtcInterop.wrtc_start(_handle, null); // 接收端：等待远端 offer
            _signaling.Connect(_signalUrl);
        }

        private void OnSignalingMessage(SignalingMessage m)
        {
            if (_handle == IntPtr.Zero) return;
            switch (m.Type)
            {
                case SignalingMessage.TypeOffer:  WebRtcInterop.wrtc_set_remote_sdp(_handle, 0, m.Sdp); break;
                case SignalingMessage.TypeAnswer: WebRtcInterop.wrtc_set_remote_sdp(_handle, 1, m.Sdp); break;
                case SignalingMessage.TypeCandidate: WebRtcInterop.wrtc_add_ice(_handle, m.Candidate, m.SdpMid, m.SdpMLineIndex); break;
            }
        }

        [AOT.MonoPInvokeCallback(typeof(WrtcFrameCallback))]
        private void OnNativeFrame(IntPtr data, int size, int w, int h, long tsNs, IntPtr user)
        {
            try { OnFrame?.Invoke(data, size, w, h); } catch { /* 原生线程禁日志 */ }
        }

        [AOT.MonoPInvokeCallback(typeof(WrtcSignalCallback))]
        private void OnNativeSignal(int kind, IntPtr text, IntPtr user)
        {
            // 本端产生的 SDP/candidate → 经信令发给对端（转字符串在原生线程安全；发送走信令后台）
            string s = Marshal.PtrToStringAnsi(text);
            SignalingMessage msg = kind == 0 ? SignalingMessage.Offer(s)
                                 : kind == 1 ? SignalingMessage.Answer(s)
                                 : SignalingMessage.Ice(s, null, 0);
            try { _signaling.Send(msg); } catch { }
        }

        public void Stop()
        {
            if (_signaling != null) { _signaling.OnMessage -= OnSignalingMessage; _signaling.Close(); }
            if (_handle != IntPtr.Zero) { WebRtcInterop.wrtc_close(_handle); _handle = IntPtr.Zero; }
        }
#else
        public void Start()
        {
            Debug.LogWarning("[WebRtc] 原生 WebRTC 未编入（需定义 WEBRTC_NATIVE + 提供 libpicowebrtc/libwebrtc）。" +
                             "M0 请用 FakeStereoVideoSource。");
        }

        public void Stop() { _signaling?.Close(); }
#endif
    }
}
