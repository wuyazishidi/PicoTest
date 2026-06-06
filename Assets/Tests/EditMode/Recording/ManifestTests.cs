// Assets/Tests/EditMode/Recording/ManifestTests.cs
using System;
using System.IO;
using NUnit.Framework;
using PicoTest.Core;
using PicoTest.Core.Recording;
using PicoTest.Core.Schema;

namespace PicoTest.Tests.EditMode.Recording
{
    public class ManifestTests
    {
        private string _dir;
        [SetUp] public void SetUp() { _dir = Path.Combine(Path.GetTempPath(), "ptm_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_dir); }
        [TearDown] public void TearDown() { Directory.Delete(_dir, true); }

        private static SessionMeta NewMeta()
        {
            var m = SessionMeta.CreateNew("test-device");
            m.Streams.Add(new StreamInfo { Id = "body_pose", Type = StreamType.BodyPose, NominalHz = 72 });
            return m;
        }

        [Test]
        public void SaveLoad_RoundTrips()
        {
            var m = NewMeta();
            m.Tags.Add("walk");
            Manifest.Save(_dir, m);
            var back = Manifest.Load(_dir);
            Assert.AreEqual(m.SessionId, back.SessionId);
            Assert.AreEqual(CoreInfo.SchemaVersion, back.SchemaVersion);
            Assert.AreEqual("walk", back.Tags[0]);
            Assert.AreEqual(StreamType.BodyPose, back.Streams[0].Type);
        }

        [Test]
        public void Recover_RebuildsFromChunks_AndMarksRecovered()
        {
            var m = NewMeta();
            m.Status = SessionStatus.Recording;   // 崩溃 = 停在 Recording 没终化
            Manifest.Save(_dir, m);
            var streamDir = Path.Combine(_dir, "streams", "body_pose");
            using (var w = new ChunkWriter(streamDir, Guid.Parse(m.SessionId), "body_pose", 1 << 20))
            {
                w.AppendFrame(new byte[10]);
                w.AppendFrame(new byte[10]);
            }

            var recovered = Manifest.RecoverIfNeeded(_dir);

            Assert.IsTrue(recovered);
            var back = Manifest.Load(_dir);
            Assert.AreEqual(SessionStatus.Recovered, back.Status);
            Assert.AreEqual(2, back.Streams[0].FrameCount);
        }

        [Test]
        public void Recover_NoOp_WhenCompleted()
        {
            var m = NewMeta();
            m.Status = SessionStatus.Completed;
            Manifest.Save(_dir, m);
            Assert.IsFalse(Manifest.RecoverIfNeeded(_dir));
        }
    }
}
