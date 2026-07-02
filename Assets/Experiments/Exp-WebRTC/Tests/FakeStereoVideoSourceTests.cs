using System;
using System.Threading;
using NUnit.Framework;
using PicoTest.Experiments.WebRTC;

namespace PicoTest.Experiments.WebRTC.Tests
{
    /// <summary>假帧源纯 C# 单测：后台线程按约定尺寸投帧（模拟原生解码线程）。</summary>
    public class FakeStereoVideoSourceTests
    {
        [Test]
        public void DeliversFrame_WithExpectedSizeAndDims()
        {
            const int W = 2560, H = 720;
            var src = new FakeStereoVideoSource(W, H, 60);
            int gotSize = 0, gotW = 0, gotH = 0;
            bool ptrNonNull = false;
            var evt = new ManualResetEventSlim(false);

            Action<IntPtr, int, int, int> h = (data, size, w, hh) =>
            {
                gotSize = size; gotW = w; gotH = hh; ptrNonNull = data != IntPtr.Zero;
                evt.Set();
            };
            src.OnFrame += h;
            try
            {
                src.Start();
                Assert.IsTrue(evt.Wait(2000), "2s 内未收到帧");
            }
            finally
            {
                src.OnFrame -= h;
                src.Stop();
            }

            Assert.IsTrue(ptrNonNull, "帧指针为空");
            Assert.AreEqual(W, gotW, "宽");
            Assert.AreEqual(H, gotH, "高");
            Assert.AreEqual(W * H * 4, gotSize, "RGBA32 字节数");
        }

        [Test]
        public void StopIsIdempotent_AndSafeWithoutStart()
        {
            var src = new FakeStereoVideoSource(64, 64, 30);
            Assert.DoesNotThrow(() => src.Stop());          // 未 Start 直接 Stop
            src.Start();
            Assert.DoesNotThrow(() => { src.Stop(); src.Stop(); }); // 重复 Stop
        }
    }
}
