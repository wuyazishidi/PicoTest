// Assets/Experiments/Exp-RobotStreamLeftPreview/Tests/OfferPayloadTests.cs
using NUnit.Framework;

namespace PicoTest.Experiments.RobotStreamLeftPreview.Tests
{
    /// <summary>
    /// offer/answer JSON 编解码单测（不依赖网络/WebRTC 原生库）。
    /// 与 run_stereo_left_viewer.py 的 aiohttp `/offer` 端点约定一致：
    /// {"sdp": "...", "type": "offer"|"answer"}。
    /// </summary>
    public class OfferPayloadTests
    {
        [Test]
        public void Offer_RoundTrips()
        {
            var p = OfferPayload.Offer("v=0\r\no=- 1 2 IN IP4 127.0.0.1\r\n");
            var back = OfferPayload.Parse(p.ToJson());
            Assert.AreEqual("offer", back.Type);
            Assert.AreEqual(p.Sdp, back.Sdp);
        }

        [Test]
        public void Parse_KnownAnswerJson()
        {
            var back = OfferPayload.Parse("{\"sdp\":\"answer-sdp\",\"type\":\"answer\"}");
            Assert.AreEqual("answer", back.Type);
            Assert.AreEqual("answer-sdp", back.Sdp);
        }

        [Test]
        public void ToJson_UsesLowercaseKeys_MatchingAiohttpEndpoint()
        {
            string json = OfferPayload.Offer("x").ToJson();
            StringAssert.Contains("\"sdp\"", json);
            StringAssert.Contains("\"type\"", json);
        }
    }
}
