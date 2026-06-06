// Assets/Tests/EditMode/Schema/FramesTests.cs
using NUnit.Framework;
using PicoTest.Core.Schema;

namespace PicoTest.Tests.EditMode.Schema
{
    public class FramesTests
    {
        [Test]
        public void BodyPoseFrame_RoundTrip()
        {
            var f = new BodyPoseFrame(123456789L);
            for (int i = 0; i < BodyPoseFrame.JointCount; i++)
                f.SetJoint(i, new Vec3f(i, i * 2, i * 3), new Quatf(0, 0, 0, 1));

            var bytes = f.ToBytes();
            Assert.AreEqual(BodyPoseFrame.ByteSize, bytes.Length);

            var back = BodyPoseFrame.FromBytes(bytes);
            Assert.AreEqual(123456789L, back.TimestampNs);
            Assert.AreEqual(new Vec3f(5, 10, 15), back.GetJointPosition(5));
        }

        [Test]
        public void BodyPoseFrame_ByteSize_Is680()
        {
            // 8(ts) + 24 * (12 + 16)
            Assert.AreEqual(680, BodyPoseFrame.ByteSize);
        }

        [Test]
        public void VideoFrameMeta_RoundTrip_WithPayload()
        {
            var payload = new byte[] { 1, 2, 3, 4 };
            var f = new VideoFrameMeta(42L, 7, 640, 480, payload);
            var bytes = f.ToBytes();
            var back = VideoFrameMeta.FromBytes(bytes);
            Assert.AreEqual(42L, back.TimestampNs);
            Assert.AreEqual(7, back.FrameIndex);
            Assert.AreEqual(640, back.Width);
            Assert.AreEqual(4, back.Payload.Length);
            Assert.AreEqual(f.Crc32, back.Crc32);
        }
    }
}
