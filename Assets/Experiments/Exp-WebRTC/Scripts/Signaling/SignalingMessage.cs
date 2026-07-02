using Newtonsoft.Json;

namespace PicoTest.Experiments.WebRTC.Signaling
{
    /// <summary>
    /// 信令消息（自定义 JSON over WebSocket，首版）。类型：offer/answer/candidate/bye。
    /// 纯数据 + JSON 编解码，可单测（不依赖 Unity/网络）。真实协议确定后（Ayame/自定义/HTTP）可换实现。
    /// </summary>
    public sealed class SignalingMessage
    {
        [JsonProperty("type")] public string Type;          // "offer" | "answer" | "candidate" | "bye"
        [JsonProperty("sdp")] public string Sdp;            // offer/answer 的 SDP
        [JsonProperty("candidate")] public string Candidate; // ICE candidate 串
        [JsonProperty("sdpMid")] public string SdpMid;
        [JsonProperty("sdpMLineIndex")] public int SdpMLineIndex;

        public const string TypeOffer = "offer";
        public const string TypeAnswer = "answer";
        public const string TypeCandidate = "candidate";
        public const string TypeBye = "bye";

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Include,
        };

        public string ToJson() => JsonConvert.SerializeObject(this, Settings);

        public static SignalingMessage Parse(string json)
            => JsonConvert.DeserializeObject<SignalingMessage>(json);

        public static SignalingMessage Offer(string sdp) => new SignalingMessage { Type = TypeOffer, Sdp = sdp };
        public static SignalingMessage Answer(string sdp) => new SignalingMessage { Type = TypeAnswer, Sdp = sdp };
        public static SignalingMessage Ice(string cand, string mid, int mline)
            => new SignalingMessage { Type = TypeCandidate, Candidate = cand, SdpMid = mid, SdpMLineIndex = mline };
    }
}
