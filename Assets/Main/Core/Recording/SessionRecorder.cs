// Assets/Main/Core/Recording/SessionRecorder.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace PicoTest.Core.Recording
{
    /// <summary>
    /// 会话录制器：每流一个有界队列 + 一个写线程（IO 不进调用线程）。
    /// 队列满 → 丢帧并计数（不阻塞采集线程）。Stop() 排空队列、终化 manifest(Completed)。
    /// </summary>
    public sealed class SessionRecorder : IDisposable
    {
        private const long MaxChunkBytes = 64L * 1024 * 1024;

        private sealed class StreamChannel
        {
            public BlockingCollection<byte[]> Queue;
            public Thread Writer;
            public ChunkWriter ChunkWriter;
            public long FrameCount;
            public long Dropped;
        }

        private readonly string _root;
        private readonly SessionMeta _meta;
        private readonly int _queueCapacity;
        private readonly bool _startWriters;
        private readonly Dictionary<string, StreamChannel> _channels = new Dictionary<string, StreamChannel>();
        private volatile bool _running;

        public string SessionDir { get; }
        public SessionMeta Meta => _meta;

        public SessionRecorder(string root, SessionMeta meta, int queueCapacity = 1024, bool startWriters = true)
        {
            _root = root; _meta = meta; _queueCapacity = queueCapacity; _startWriters = startWriters;
            SessionDir = Path.Combine(root, meta.SessionId);
        }

        public void Start()
        {
            Directory.CreateDirectory(SessionDir);
            _meta.Status = SessionStatus.Recording;
            Manifest.Save(SessionDir, _meta);

            foreach (var info in _meta.Streams)
            {
                var streamDir = Path.Combine(SessionDir, "streams", info.Id);
                var ch = new StreamChannel
                {
                    Queue = new BlockingCollection<byte[]>(_queueCapacity),
                    ChunkWriter = new ChunkWriter(streamDir, Guid.Parse(_meta.SessionId), info.Id, MaxChunkBytes),
                };
                if (_startWriters)
                {
                    ch.Writer = new Thread(() => WriterLoop(ch)) { IsBackground = true, Name = $"rec-{info.Id}" };
                    ch.Writer.Start();
                }
                _channels[info.Id] = ch;
            }
            _running = true;
        }

        /// <summary>采集线程调用。队列满或已停止返回 false。</summary>
        public bool Enqueue(string streamId, byte[] frame)
        {
            if (!_running) return false;
            var ch = _channels[streamId];
            if (ch.Queue.TryAdd(frame)) return true;
            Interlocked.Increment(ref ch.Dropped);
            return false;
        }

        public long GetDroppedFrames(string streamId) => Interlocked.Read(ref _channels[streamId].Dropped);
        public long GetFrameCount(string streamId) => Interlocked.Read(ref _channels[streamId].FrameCount);

        private static void WriterLoop(StreamChannel ch)
        {
            foreach (var frame in ch.Queue.GetConsumingEnumerable())
            {
                ch.ChunkWriter.AppendFrame(frame);
                Interlocked.Increment(ref ch.FrameCount);
            }
        }

        /// <summary>停止：拒绝新帧 → 排空队列 → 关 writer → 终化 manifest。</summary>
        public void Stop()
        {
            if (!_running) return;
            _running = false;

            foreach (var ch in _channels.Values)
            {
                ch.Queue.CompleteAdding();
                ch.Writer?.Join(10000);
                ch.ChunkWriter.Dispose();
            }

            foreach (var info in _meta.Streams)
            {
                var ch = _channels[info.Id];
                info.FrameCount = Interlocked.Read(ref ch.FrameCount);
                info.DroppedFrames = Interlocked.Read(ref ch.Dropped);
            }
            _meta.Status = SessionStatus.Completed;
            Manifest.Save(SessionDir, _meta);
        }

        public void Dispose()
        {
            if (_running) Stop();
            foreach (var ch in _channels.Values) { ch.Queue.Dispose(); }
        }
    }
}
