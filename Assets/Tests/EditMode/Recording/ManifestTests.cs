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

        // ── 新增：容错损坏 manifest 的 3 个测试 ──────────────────────────────

        [Test]
        public void Recover_EmptyManifest_ReturnsFalse_WithoutThrowing()
        {
            // 写 0 字节的 manifest.json，模拟崩溃时写空文件
            var manifestPath = Path.Combine(_dir, Manifest.FileName);
            File.WriteAllBytes(manifestPath, new byte[0]);

            bool result = false;
            Assert.DoesNotThrow(() => result = Manifest.RecoverIfNeeded(_dir));
            Assert.IsFalse(result);
            Assert.IsFalse(File.Exists(manifestPath), "原始 manifest.json 应已被改名");
            Assert.IsTrue(File.Exists(manifestPath + Manifest.CorruptSuffix), "应存在 manifest.json.corrupt");
        }

        [Test]
        public void Recover_TruncatedJson_ReturnsFalse_WithoutThrowing()
        {
            // 写截断的 JSON，模拟半写崩溃
            var manifestPath = Path.Combine(_dir, Manifest.FileName);
            File.WriteAllText(manifestPath, "{\"SessionId\": \"abc");

            bool result = false;
            Assert.DoesNotThrow(() => result = Manifest.RecoverIfNeeded(_dir));
            Assert.IsFalse(result);
            Assert.IsFalse(File.Exists(manifestPath), "原始 manifest.json 应已被改名");
            Assert.IsTrue(File.Exists(manifestPath + Manifest.CorruptSuffix), "应存在 manifest.json.corrupt");
        }

        [Test]
        public void Recover_MultiChunk_SumsAllChunks()
        {
            // 用 maxChunkBytes=200 强制多段滚段，写 5 帧（每帧 50 字节 payload）
            // 验证 RecoverIfNeeded 跨段正确求和 FrameCount==5
            var m = NewMeta();
            m.Status = SessionStatus.Recording;
            Manifest.Save(_dir, m);

            var streamDir = Path.Combine(_dir, "streams", "body_pose");
            using (var w = new ChunkWriter(streamDir, Guid.Parse(m.SessionId), "body_pose", maxChunkBytes: 200))
            {
                for (int i = 0; i < 5; i++)
                    w.AppendFrame(new byte[50]);
            }

            var recovered = Manifest.RecoverIfNeeded(_dir);

            Assert.IsTrue(recovered);
            var back = Manifest.Load(_dir);
            Assert.AreEqual(SessionStatus.Recovered, back.Status);
            Assert.AreEqual(5, back.Streams[0].FrameCount, "所有段的帧数应累加为 5");
        }
    }
}
