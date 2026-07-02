using System.Collections;
using System.Collections.Concurrent;
using UnityEngine;
using Unity.WebRTC;
using PicoTest.Experiments.WebRTC.Signaling;

namespace PicoTest.Experiments.WebRTC
{
    /// <summary>
    /// 真实视频源 = com.unity.webrtc 接收端。RTCPeerConnection(recvonly video) 收远端双目鱼眼流；
    /// VideoStreamTrack.OnVideoReceived 给出 GPU Texture → 作 <see cref="Frame"/>（直接喂穹顶）。
    /// 信令经 <see cref="ISignaling"/> 交换 offer/answer/candidate。信令回调在后台线程 → 入队，
    /// Tick()（主线程，由 feeder 每帧调）处理（WebRTC 的 SDP 操作须主线程/协程）。
    /// </summary>
    public sealed class UnityWebRtcVideoSource : IWebRtcVideoSource
    {
        public Texture Frame { get; private set; }

        private readonly ISignaling _signaling;
        private readonly string _url;
        private readonly MonoBehaviour _runner;
        private RTCPeerConnection _pc;
        private readonly ConcurrentQueue<SignalingMessage> _incoming = new ConcurrentQueue<SignalingMessage>();

        public UnityWebRtcVideoSource(ISignaling signaling, string url, MonoBehaviour runner)
        {
            _signaling = signaling; _url = url; _runner = runner;
        }

        public IEnumerator GetRenderPump() => Unity.WebRTC.WebRTC.Update();   // 每帧驱动包的编解码/渲染（全限定避免命名空间遮蔽）

        public void Start()
        {
            var config = new RTCConfiguration
            {
                iceServers = new[] { new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } } }
            };
            _pc = new RTCPeerConnection(ref config);
            _pc.AddTransceiver(TrackKind.Video,
                new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.RecvOnly });

            _pc.OnTrack = e =>
            {
                if (e.Track is VideoStreamTrack v)
                    v.OnVideoReceived += tex => Frame = tex;   // com.unity.webrtc 在主线程回调
            };
            _pc.OnIceCandidate = c =>
            {
                try { _signaling.Send(SignalingMessage.Ice(c.Candidate, c.SdpMid, c.SdpMLineIndex ?? 0)); } catch { }
            };

            _signaling.OnMessage += m => _incoming.Enqueue(m);   // 后台线程 → 入队
            _signaling.OnError += err => Debug.LogWarning($"[WebRtc] signaling: {err}");
            _signaling.Connect(_url);
        }

        public void Tick()
        {
            while (_incoming.TryDequeue(out var m))
            {
                switch (m.Type)
                {
                    case SignalingMessage.TypeOffer: _runner.StartCoroutine(HandleOffer(m.Sdp)); break;
                    case SignalingMessage.TypeAnswer: _runner.StartCoroutine(HandleAnswer(m.Sdp)); break;
                    case SignalingMessage.TypeCandidate:
                        _pc?.AddIceCandidate(new RTCIceCandidate(new RTCIceCandidateInit
                        {
                            candidate = m.Candidate, sdpMid = m.SdpMid, sdpMLineIndex = m.SdpMLineIndex
                        }));
                        break;
                }
            }
        }

        private IEnumerator HandleOffer(string sdp)
        {
            var remote = new RTCSessionDescription { type = RTCSdpType.Offer, sdp = sdp };
            yield return _pc.SetRemoteDescription(ref remote);
            var answerOp = _pc.CreateAnswer();
            yield return answerOp;
            if (answerOp.IsError) { Debug.LogWarning("[WebRtc] CreateAnswer error"); yield break; }
            var answer = answerOp.Desc;
            yield return _pc.SetLocalDescription(ref answer);
            _signaling.Send(SignalingMessage.Answer(answer.sdp));
        }

        private IEnumerator HandleAnswer(string sdp)
        {
            var remote = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
            yield return _pc.SetRemoteDescription(ref remote);
        }

        public void Stop()
        {
            try { _signaling?.Close(); } catch { }
            try { _pc?.Close(); } catch { }
            try { _pc?.Dispose(); } catch { }
            _pc = null;
            Frame = null;
        }
    }
}
