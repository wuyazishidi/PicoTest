// Assets/Tests/EditMode/Recording/SessionRecorderTests.cs
using System;
using System.IO;
using System.Linq;
using System.Threading;
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

        // ──────────────────────────────────────────────────────────
        // 原有 3 个测试
        // ──────────────────────────────────────────────────────────

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

        // ──────────────────────────────────────────────────────────
        // 新增 4 个测试（review blockers）
        // ──────────────────────────────────────────────────────────

        /// <summary>
        /// 注入一个 Append 即抛 IOException 的假 sink：
        /// Stop 不抛、manifest 状态 Failed、FirstFault 非空。
        /// </summary>
        [Test]
        public void WriterFault_MarksSessionFailed_AndDoesNotThrow()
        {
            var meta = SessionMeta.CreateNew("test");
            meta.Streams.Add(new StreamInfo { Id = "s", Type = StreamType.BodyPose, NominalHz = 72 });

            IChunkSink FaultySinkFactory(string dir, Guid sid, string streamId, long maxBytes)
                => new FaultySink();

            var rec = new SessionRecorder(_root, meta, sinkFactory: FaultySinkFactory);
            rec.Start();

            // 入队若干帧触发写线程故障
            for (int i = 0; i < 10; i++)
                rec.Enqueue("s", new byte[4]);

            // Stop 必须不抛
            Assert.DoesNotThrow(() => rec.Stop());

            // manifest 状态 Failed
            var dir = Path.Combine(_root, meta.SessionId);
            var back = Manifest.Load(dir);
            Assert.AreEqual(SessionStatus.Failed, back.Status);

            // FirstFault 非空
            Assert.IsNotNull(rec.FirstFault);

            rec.Dispose();
        }

        /// <summary>4 个线程并发 Enqueue，主线程同时 Stop；任何线程都不应观测到异常。</summary>
        [Test]
        public void ConcurrentEnqueue_DuringStop_NeverThrows()
        {
            var meta = SessionMeta.CreateNew("test");
            meta.Streams.Add(new StreamInfo { Id = "s", Type = StreamType.BodyPose, NominalHz = 72 });

            using var rec = new SessionRecorder(_root, meta);
            rec.Start();

            int threadExceptions = 0;
            var threads = new Thread[4];
            var stopEvent = new ManualResetEventSlim(false);

            for (int t = 0; t < threads.Length; t++)
            {
                threads[t] = new Thread(() =>
                {
                    stopEvent.Wait(); // 等主线程信号，尽量并发
                    for (int i = 0; i < 500; i++)
                    {
                        try { rec.Enqueue("s", new byte[4]); }
                        catch { Interlocked.Increment(ref threadExceptions); }
                    }
                }) { IsBackground = true };
                threads[t].Start();
            }

            stopEvent.Set();
            rec.Stop();

            foreach (var th in threads) th.Join(5000);

            Assert.AreEqual(0, threadExceptions, "采集线程不应观测到任何异常");
        }

        /// <summary>连续两次 Dispose 不抛。</summary>
        [Test]
        public void Dispose_Twice_IsSafe()
        {
            var meta = SessionMeta.CreateNew("test");
            meta.Streams.Add(new StreamInfo { Id = "s", Type = StreamType.BodyPose, NominalHz = 72 });

            var rec = new SessionRecorder(_root, meta);
            rec.Start();
            rec.Stop();

            Assert.DoesNotThrow(() => rec.Dispose());
            Assert.DoesNotThrow(() => rec.Dispose());
        }

        /// <summary>未知 streamId → Enqueue 返回 false，不抛。</summary>
        [Test]
        public void Enqueue_UnknownStream_ReturnsFalse()
        {
            var meta = SessionMeta.CreateNew("test");
            meta.Streams.Add(new StreamInfo { Id = "s", Type = StreamType.BodyPose, NominalHz = 72 });

            using var rec = new SessionRecorder(_root, meta);
            rec.Start();

            bool result = true;
            Assert.DoesNotThrow(() => result = rec.Enqueue("no_such_stream", new byte[4]));
            Assert.IsFalse(result);
        }

        // ──────────────────────────────────────────────────────────
        // 辅助：会立即抛 IOException 的假 sink
        // ──────────────────────────────────────────────────────────
        private sealed class FaultySink : IChunkSink
        {
            public void Append(byte[] frame) => throw new IOException("simulated disk fault");
            public void Dispose() { }
        }
    }
}
