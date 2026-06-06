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

        [Test]
        public void Roll_FramesReadBackFromSecondChunk_NoLossOrCrossContamination()
        {
            // maxChunkBytes 设小一点，确保在第二帧写入时触发滚动到 chunk_0002
            // 帧头部 = HeaderLength = 4+4+16+4+1=29；每帧 payload=100 + 4(len prefix) = 104
            // 设 maxChunkBytes=200：header(29)+frame1(104)=133 < 200；
            // 加 frame2 时 position=133，133+104=237 > 200 → 滚动
            const int payloadSize = 100;
            var sid = Guid.NewGuid();
            using (var w = new ChunkWriter(_dir, sid, "s", maxChunkBytes: 200))
            {
                // 每帧首字节为递增标记，便于区分
                for (int i = 0; i < 5; i++)
                {
                    var frame = new byte[payloadSize];
                    frame[0] = (byte)(i + 1); // 首字节 1,2,3,4,5
                    w.AppendFrame(frame);
                }
            }

            // 必须产生多个 chunk 文件
            var files = Directory.GetFiles(_dir, "chunk_*.ptc");
            Assert.Greater(files.Length, 1, "期望至少产生 2 个 chunk 文件");

            // 读回 chunk_0002.ptc，验证内容不丢失也不串帧
            var chunk2Path = Path.Combine(_dir, "chunk_0002.ptc");
            Assert.IsTrue(File.Exists(chunk2Path), "chunk_0002.ptc 应存在");
            var chunk2Frames = ChunkReader.ReadAllFrames(chunk2Path).ToList();
            Assert.Greater(chunk2Frames.Count, 0, "chunk_0002 中应有帧");

            // 所有 chunk 合并后帧总数等于写入帧数
            int total = 0;
            foreach (var f in files.OrderBy(x => x))
                total += ChunkReader.ReadAllFrames(f).Count();
            Assert.AreEqual(5, total, "5 帧写入，读回总数应为 5，无丢失");

            // chunk_0002 中每帧首字节均应在 1..5 范围内（无串帧/乱写）
            foreach (var frame in chunk2Frames)
            {
                Assert.GreaterOrEqual(frame[0], 1);
                Assert.LessOrEqual(frame[0], 5);
                Assert.AreEqual(payloadSize, frame.Length, "帧长度应与写入一致");
            }
        }

        [Test]
        public void BadMagic_ThrowsInvalidDataException()
        {
            var path = Path.Combine(_dir, "bad_magic.ptc");
            // 手写 4 字节非法 magic + 任意填充内容
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                fs.Write(System.Text.Encoding.ASCII.GetBytes("XXXX"), 0, 4);
                fs.Write(new byte[] { 0, 0, 0, 0, 1, 2, 3, 4 }, 0, 8);
            }
            Assert.Throws<InvalidDataException>(() =>
            {
                // ReadAllFrames 是延迟迭代器，必须枚举才会执行校验
                ChunkReader.ReadAllFrames(path).ToList();
            });
        }
    }
}
