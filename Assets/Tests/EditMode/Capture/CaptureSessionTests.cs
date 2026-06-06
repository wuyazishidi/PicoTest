// Assets/Tests/EditMode/Capture/CaptureSessionTests.cs
using System;
using System.IO;
using NUnit.Framework;
using PicoTest.Core.Capture;
using PicoTest.Core.Recording;

namespace PicoTest.Tests.EditMode.Capture
{
    public class CaptureSessionTests
    {
        private string _root;
        [SetUp] public void SetUp() { _root = Path.Combine(Path.GetTempPath(), "pts_" + Guid.NewGuid().ToString("N")); }
        [TearDown] public void TearDown() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

        [Test]
        public void FullSession_TwoSources_RecordsAndCompletes()
        {
            var session = new CaptureSession(_root, "test-device",
                new ICaptureSource[] { new MockBodyPoseSource(), new MockVideoSource() });

            session.Start();
            for (long now = 0; now <= 500_000_000L; now += 5_000_000L) session.Tick(now);
            var dir = session.SessionDir;
            session.Stop();

            var meta = Manifest.Load(dir);
            Assert.AreEqual(SessionStatus.Completed, meta.Status);
            Assert.AreEqual(2, meta.Streams.Count);
            // 0.5s: body 72Hz → 37 帧（含 t=0），video 30Hz → 16 帧
            Assert.AreEqual(37, meta.Streams.Find(s => s.Id == "body_pose").FrameCount);
            Assert.AreEqual(16, meta.Streams.Find(s => s.Id == "video").FrameCount);
        }

        [Test]
        public void Snapshot_ReflectsRecordingState()
        {
            var session = new CaptureSession(_root, "d", new ICaptureSource[] { new MockBodyPoseSource() });
            Assert.IsFalse(session.GetSnapshot().IsRecording);
            session.Start();
            session.Tick(100_000_000L);
            var snap = session.GetSnapshot();
            Assert.IsTrue(snap.IsRecording);
            Assert.Greater(snap.Streams[0].FrameCount, 0);
            session.Stop();
            Assert.IsFalse(session.GetSnapshot().IsRecording);
        }

        [Test]
        public void AddTag_AppearsInFinalManifest()
        {
            var session = new CaptureSession(_root, "d", new ICaptureSource[] { new MockBodyPoseSource() });
            session.Start();
            session.AddTag("squat");
            var dir = session.SessionDir;
            session.Stop();
            CollectionAssert.Contains(Manifest.Load(dir).Tags, "squat");
        }

        [Test]
        public void Restart_CreatesNewSession_WithFreshRecorder()
        {
            var session = new CaptureSession(_root, "d", new ICaptureSource[] { new MockBodyPoseSource() });
            session.Start();
            session.Tick(50_000_000L);
            var dir1 = session.SessionDir;
            session.Stop();

            session.Start();
            session.Tick(50_000_000L);
            var dir2 = session.SessionDir;
            session.Stop();

            Assert.AreNotEqual(dir1, dir2);
            Assert.AreEqual(SessionStatus.Completed, Manifest.Load(dir1).Status);
            Assert.AreEqual(SessionStatus.Completed, Manifest.Load(dir2).Status);
        }
    }
}
