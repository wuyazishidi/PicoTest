// Assets/Experiments/Exp-RobotStreamLeftPreview/Scripts/OfferPayload.cs
using Newtonsoft.Json;

namespace PicoTest.Experiments.RobotStreamLeftPreview
{
    /// <summary>
    /// HTTP offer/answer body（{"sdp":..,"type":..}），与 Tools/run_stereo_left_viewer.py 的
    /// aiohttp `/offer` 端点一致（无 ICE candidate 字段 —— 该端点不做 trickle）。
    /// 纯数据 + JSON 编解码，可单测（不依赖 Unity/网络），与 Experiment.WebRTC 的
    /// SignalingMessage 同款风格。
    /// </summary>
    public sealed class OfferPayload
    {
        [JsonProperty("sdp")] public string Sdp;
        [JsonProperty("type")] public string Type;

        public string ToJson() => JsonConvert.SerializeObject(this);

        public static OfferPayload Parse(string json) => JsonConvert.DeserializeObject<OfferPayload>(json);

        public static OfferPayload Offer(string sdp) => new OfferPayload { Sdp = sdp, Type = "offer" };
    }
}
