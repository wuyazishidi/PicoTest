// Assets/Tests/EditMode/Recording/SessionRecorderTests.cs
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PicoTest.Core.Recording;
using PicoTest.Core.Schema;

namespace PicoTest.Tests.EditMode.Recording
{
    public class SessionRecorderTests
    {
        private string _root;
        [SetUp] public void SetUp() { _root = Path.Combine(Path.GetTempPath(), "ptr_" + Guid.NewGuid().ToString("N")); }
        [TearDown] public void TearDown() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

        [Test]
        public void RecordsFrames_AndFinalizesManifest()
        {
            var meta = SessionMeta.CreateNew("test");
            meta.Streams.Add(new StreamInfo { Id = "body_pose", Type = StreamType.BodyPose, NominalHz = 72 });

            using (var rec = new SessionRecorder(_root, meta))
            {
                rec.Start();
                for (int i = 0; i < 100; i++)
                    rec.Enqueue("body_pose", new byte[680]);
                rec.Stop();
            }

            var dir = Path.Combine(_root, meta.SessionId);
            var back = Manifest.Load(dir);
            Assert.AreEqual(SessionStatus.Completed, back.Status);
            Assert.AreEqual(100, back.Streams[0].FrameCount);
            Assert.AreEqual(0, back.Streams[0].DroppedFrames);

            var total = Directory.GetFiles(Path.Combine(dir, "streams", "body_pose"), "chunk_*.ptc")
                .Sum(f => ChunkReader.ReadAllFrames(f).Count());
            Assert.AreEqual(100, total);
        }

        [Test]
        public void Enqueue_AfterStop_IsRejected()
        {
            var meta = SessionMeta.CreateNew("test");
            meta.Streams.Add(new StreamInfo { Id = "s", Type = StreamType.BodyPose, NominalHz = 72 });
            var rec = new SessionRecorder(_root, meta);
            rec.Start();
            rec.Stop();
            Assert.IsFalse(rec.Enqueue("s", new byte[1]));
            rec.Dispose();
        }

        [Test]
        public void QueueOverflow_CountsDroppedFrames()
        {
            var meta = SessionMeta.CreateNew("test");
            meta.Streams.Add(new StreamInfo { Id = "s", Type = StreamType.BodyPose, NominalHz = 72 });
            // queueCapacity=2 且不启动写线程消费 → 必然溢出
            using (var rec = new SessionRecorder(_root, meta, queueCapacity: 2, startWriters: false))
            {
                rec.Start();
                for (int i = 0; i < 10; i++) rec.Enqueue("s", new byte[1]);
                Assert.GreaterOrEqual(rec.GetDroppedFrames("s"), 8);
            }
        }
    }
}
