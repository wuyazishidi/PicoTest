using NUnit.Framework;
using PicoTest.Experiments.WebRTC.Signaling;

namespace PicoTest.Experiments.WebRTC.Tests
{
    /// <summary>信令 JSON 编解码单测（M2 可验证部分，不依赖网络/原生库）。</summary>
    public class SignalingMessageTests
    {
        [Test]
        public void Offer_RoundTrips()
        {
            var m = SignalingMessage.Offer("v=0\r\no=- 1 2 IN IP4 127.0.0.1\r\n");
            var back = SignalingMessage.Parse(m.ToJson());
            Assert.AreEqual(SignalingMessage.TypeOffer, back.Type);
            Assert.AreEqual(m.Sdp, back.Sdp);
        }

        [Test]
        public void Answer_RoundTrips()
        {
            var back = SignalingMessage.Parse(SignalingMessage.Answer("answer-sdp").ToJson());
            Assert.AreEqual(SignalingMessage.TypeAnswer, back.Type);
            Assert.AreEqual("answer-sdp", back.Sdp);
        }

        [Test]
        public void Candidate_RoundTrips()
        {
            var back = SignalingMessage.Parse(SignalingMessage.Ice("candidate:1 1 udp 2 1.2.3.4 5 typ host", "0", 3).ToJson());
            Assert.AreEqual(SignalingMessage.TypeCandidate, back.Type);
            Assert.AreEqual("candidate:1 1 udp 2 1.2.3.4 5 typ host", back.Candidate);
            Assert.AreEqual("0", back.SdpMid);
            Assert.AreEqual(3, back.SdpMLineIndex);
        }

        [Test]
        public void Parse_KnownJson()
        {
            var m = SignalingMessage.Parse("{\"type\":\"answer\",\"sdp\":\"x\"}");
            Assert.AreEqual(SignalingMessage.TypeAnswer, m.Type);
            Assert.AreEqual("x", m.Sdp);
        }
    }
}
