// Assets/Experiments/Exp-RobotStreamLeftPreview/Scripts/HttpOfferVideoSource.cs
using System.Collections;
using System.Text;
using PicoTest.Experiments.WebRTC;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.Networking;

namespace PicoTest.Experiments.RobotStreamLeftPreview
{
    /// <summary>
    /// 视频源 = 直连 Tools/run_stereo_left_viewer.py（或姊妹脚本 run_stereo_viewer.py）的 aiortc
    /// HTTP offer/answer 端点。逐字复刻该脚本内嵌浏览器 JS 客户端的握手（无信令中继、无 ICE
    /// trickle）：recvonly video transceiver → CreateOffer/SetLocalDescription → 等本端
    /// GatheringState==Complete（一次性打包全部 host candidate）→ POST {baseUrl}/offer
    /// （body {"sdp":..,"type":"offer"}）→ 解析 answer JSON → SetRemoteDescription。
    /// 不复用 ISignaling（协议形状不同：那是双向 WS 消息流，这是单次 HTTP 往返，无 candidate 消息）。
    /// </summary>
    public sealed class HttpOfferVideoSource : IWebRtcVideoSource
    {
        public Texture Frame { get; private set; }

        private readonly string _baseUrl;
        private readonly MonoBehaviour _runner;
        private RTCPeerConnection _pc;

        public HttpOfferVideoSource(string baseUrl, MonoBehaviour runner)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _runner = runner;
        }

        public IEnumerator GetRenderPump() => Unity.WebRTC.WebRTC.Update();

        public void Start()
        {
            // 无 STUN/TURN：只走同网段 host candidate 直连（与浏览器参考客户端的
            // `new RTCPeerConnection()`——零配置——一致）。
            var config = new RTCConfiguration { iceServers = new RTCIceServer[0] };
            _pc = new RTCPeerConnection(ref config);
            _pc.AddTransceiver(TrackKind.Video,
                new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.RecvOnly });

            _pc.OnTrack = e =>
            {
                if (e.Track is VideoStreamTrack v)
                    v.OnVideoReceived += tex => Frame = tex;   // com.unity.webrtc 在主线程回调
            };

            _runner.StartCoroutine(Negotiate());
        }

        private IEnumerator Negotiate()
        {
            var offerOp = _pc.CreateOffer();
            yield return offerOp;
            if (offerOp.IsError) { Debug.LogWarning("[HttpOfferWebRTC] CreateOffer 失败"); yield break; }

            var offer = offerOp.Desc;
            var setLocalOp = _pc.SetLocalDescription(ref offer);
            yield return setLocalOp;
            if (setLocalOp.IsError) { Debug.LogWarning("[HttpOfferWebRTC] SetLocalDescription 失败"); yield break; }

            // 无 trickle：等本端 ICE 收集完成再发一次性 offer（同浏览器参考客户端）。
            while (_pc.GatheringState != RTCIceGatheringState.Complete) yield return null;

            string body = OfferPayload.Offer(_pc.LocalDescription.sdp).ToJson();
            using (var req = new UnityWebRequest($"{_baseUrl}/offer", "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[HttpOfferWebRTC] POST {_baseUrl}/offer 失败: {req.error}");
                    yield break;
                }

                var answer = OfferPayload.Parse(req.downloadHandler.text);
                var remote = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = answer.Sdp };
                var setRemoteOp = _pc.SetRemoteDescription(ref remote);
                yield return setRemoteOp;
                if (setRemoteOp.IsError) Debug.LogWarning("[HttpOfferWebRTC] SetRemoteDescription 失败");
            }
        }

        public void Tick() { }   // 一次性 HTTP 握手已在 Start() 协程内完成，无需每帧轮询

        public void Stop()
        {
            try { _pc?.Close(); } catch { }
            try { _pc?.Dispose(); } catch { }
            _pc = null;
            Frame = null;
        }
    }
}
