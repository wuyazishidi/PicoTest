using NUnit.Framework;
using UnityEngine;
using PicoTest.Experiments.WebRTC;

namespace PicoTest.Experiments.WebRTC.Tests
{
    /// <summary>假帧源单测（Texture 化，与 com.unity.webrtc 的 Texture 交付一致）。</summary>
    public class FakeStereoVideoSourceTests
    {
        [Test]
        public void ProducesTexture_WithExpectedDimsAndNonBlack()
        {
            var s = new FakeStereoVideoSource(2560, 720);
            try
            {
                s.Start();
                s.Tick();
                var t = s.Frame as Texture2D;
                Assert.IsNotNull(t, "无纹理");
                Assert.AreEqual(2560, t.width);
                Assert.AreEqual(720, t.height);
                var c = t.GetPixel(t.width / 2, t.height / 2);
                Assert.Greater(c.r + c.g + c.b, 0.01f, "中心像素为黑");
            }
            finally { s.Stop(); }
        }

        [Test]
        public void GetRenderPump_IsNull()
        {
            Assert.IsNull(new FakeStereoVideoSource().GetRenderPump());
        }

        [Test]
        public void StopIsIdempotent_AndSafeWithoutStart()
        {
            var s = new FakeStereoVideoSource(64, 64);
            Assert.DoesNotThrow(() => s.Stop());
            s.Start();
            Assert.DoesNotThrow(() => { s.Stop(); s.Stop(); });
        }
    }
}
