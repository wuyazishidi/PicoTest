// Assets/Tests/EditMode/Recording/ChunkTests.cs
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PicoTest.Core.Recording;

namespace PicoTest.Tests.EditMode.Recording
{
    public class ChunkTests
    {
        private string _dir;

        [SetUp] public void SetUp() { _dir = Path.Combine(Path.GetTempPath(), "ptc_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_dir); }
        [TearDown] public void TearDown() { Directory.Delete(_dir, true); }

        [Test]
        public void WriteThenRead_RoundTrips()
        {
            var sid = Guid.NewGuid();
            using (var w = new ChunkWriter(_dir, sid, "body_pose", maxChunkBytes: 1024 * 1024))
            {
                w.AppendFrame(new byte[] { 1, 2, 3 });
                w.AppendFrame(new byte[] { 4, 5 });
            }
            var frames = ChunkReader.ReadAllFrames(Path.Combine(_dir, "chunk_0001.ptc")).ToList();
            Assert.AreEqual(2, frames.Count);
            CollectionAssert.AreEqual(new byte[] { 4, 5 }, frames[1]);
        }

        [Test]
        public void RollsToNewChunk_WhenMaxBytesExceeded()
        {
            using (var w = new ChunkWriter(_dir, Guid.NewGuid(), "s", maxChunkBytes: 200))
            {
                for (int i = 0; i < 10; i++) w.AppendFrame(new byte[50]);
            }
            Assert.Greater(Directory.GetFiles(_dir, "chunk_*.ptc").Length, 1);
        }

        [Test]
        public void TruncatedTail_IsToleratedOnRead()
        {
            var path = Path.Combine(_dir, "chunk_0001.ptc");
            using (var w = new ChunkWriter(_dir, Guid.NewGuid(), "s", 1024 * 1024))
            {
                w.AppendFrame(new byte[100]);
                w.AppendFrame(new byte[100]);
            }
            // 模拟崩溃：截掉最后 30 字节
            using (var fs = new FileStream(path, FileMode.Open)) fs.SetLength(fs.Length - 30);
            var frames = ChunkReader.ReadAllFrames(path).ToList();
            Assert.AreEqual(1, frames.Count); // 完整的第一帧保留，残帧丢弃
        }
    }
}
