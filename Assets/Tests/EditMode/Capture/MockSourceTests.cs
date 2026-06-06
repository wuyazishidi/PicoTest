// Assets/Tests/EditMode/Capture/MockSourceTests.cs
using System.Collections.Generic;
using NUnit.Framework;
using PicoTest.Core.Capture;
using PicoTest.Core.Schema;

namespace PicoTest.Tests.EditMode.Capture
{
    public class MockSourceTests
    {
        [Test]
        public void BodyPoseSource_Produces72FramesPerSecond()
        {
            var src = new MockBodyPoseSource();
            var frames = new List<byte[]>();
            src.FrameProduced += (ts, bytes) => frames.Add(bytes);
            src.Start();
            // 模拟 1 秒：每 10ms tick 一次
            for (long now = 0; now <= 1_000_000_000L; now += 10_000_000L) src.Tick(now);
            src.Stop();
            Assert.AreEqual(73, frames.Count); // t=0 一帧 + 72 帧
            Assert.AreEqual(BodyPoseFrame.ByteSize, frames[0].Length);
        }

        [Test]
        public void BodyPoseSource_TimestampsMonotonic_AndDeterministic()
        {
            var a = Run(); var b = Run();
            CollectionAssert.AreEqual(a, b); // 同种子同输出
            for (int i = 1; i < a.Count; i++)
            {
                var prev = BodyPoseFrame.FromBytes(a[i - 1]).TimestampNs;
                var cur = BodyPoseFrame.FromBytes(a[i]).TimestampNs;
                Assert.Greater(cur, prev);
            }

            List<byte[]> Run()
            {
                var s = new MockBodyPoseSource(seed: 42);
                var list = new List<byte[]>();
                s.FrameProduced += (ts, bytes) => list.Add(bytes);
                s.Start();
                for (long now = 0; now <= 200_000_000L; now += 5_000_000L) s.Tick(now);
                s.Stop();
                return list;
            }
        }

        [Test]
        public void VideoSource_Produces30FramesPerSecond_WithValidCrc()
        {
            var src = new MockVideoSource();
            var frames = new List<byte[]>();
            src.FrameProduced += (ts, bytes) => frames.Add(bytes);
            src.Start();
            for (long now = 0; now <= 1_000_000_000L; now += 10_000_000L) src.Tick(now);
            src.Stop();
            Assert.AreEqual(31, frames.Count);
            var f = VideoFrameMeta.FromBytes(frames[0]);
            Assert.AreEqual(Crc32.Compute(f.Payload), f.Crc32);
        }
    }
}
