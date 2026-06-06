// Assets/Main/Core/Recording/SessionRecorder.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace PicoTest.Core.Recording
{
    /// <summary>
    /// 写线程用的 sink 抽象。实现方必须线程安全地支持单写线程调用。
    /// </summary>
    public interface IChunkSink : IDisposable
    {
        /// <summary>追加一帧。帧数组入队后归录制器所有，调用方不得复用。</summary>
        void Append(byte[] frame);
    }

    /// <summary>
    /// 会话录制器：每流一个有界队列 + 一个写线程（IO 不进调用线程）。
    /// 队列满 → 丢帧并计数（不阻塞采集线程）。Stop() 排空队列、终化 manifest。
    ///
    /// 设计原则：录制器任何异常不得抛入采集线程/调用方；失败会话标 Failed 而非 Completed。
    /// Start() 属装配期，失败允许抛给装配方（含回滚已建 channel）。
    /// </summary>
    public sealed class SessionRecorder : IDisposable
    {
        private const long MaxChunkBytes = 64L * 1024 * 1024;

        private sealed class StreamChannel
        {
            public BlockingCollection<byte[]> Queue;
            public Thread Writer;
            public IChunkSink Sink;
            public long FrameCount;
            public long Dropped;
            /// <summary>写线程捕获的第一个异常。volatile 保证跨线程可见。</summary>
            public volatile Exception Fault;
        }

        private readonly string _root;
        private readonly SessionMeta _meta;
        private readonly int _queueCapacity;
        private readonly bool _startWriters;
        private readonly Func<string, Guid, string, long, IChunkSink> _sinkFactory;
        private readonly Dictionary<string, StreamChannel> _channels = new Dictionary<string, StreamChannel>();
        private volatile bool _running;
        private bool _disposed;

        public string SessionDir { get; }
        public SessionMeta Meta => _meta;

        /// <summary>返回所有 channel 中第一个非空 Fault，无则返回 null。</summary>
        public Exception FirstFault
        {
            get
            {
                foreach (var ch in _channels.Values)
                    if (ch.Fault != null) return ch.Fault;
                return null;
            }
        }

        /// <param name="sinkFactory">
        /// 可选注入 sink 工厂（用于测试）。
        /// 参数：(streamDir, sessionGuid, streamId, maxChunkBytes) → IChunkSink。
        /// 默认 new ChunkWriter(...)。
        /// </param>
        public SessionRecorder(
            string root,
            SessionMeta meta,
            int queueCapacity = 1024,
            bool startWriters = true,
            Func<string, Guid, string, long, IChunkSink> sinkFactory = null)
        {
            _root = root;
            _meta = meta;
            _queueCapacity = queueCapacity;
            _startWriters = startWriters;
            _sinkFactory = sinkFactory;
            SessionDir = Path.Combine(root, meta.SessionId);
        }

        /// <summary>
        /// 启动录制。属装配期调用，失败时会回滚已建 channel 后 rethrow。
        /// 采集期不应再调用此方法。
        /// </summary>
        public void Start()
        {
            var built = new List<StreamChannel>();
            try
            {
                Directory.CreateDirectory(SessionDir);
                _meta.Status = SessionStatus.Recording;
                Manifest.Save(SessionDir, _meta);

                foreach (var info in _meta.Streams)
                {
                    var streamDir = Path.Combine(SessionDir, "streams", info.Id);
                    var sessionGuid = Guid.Parse(_meta.SessionId);
                    IChunkSink sink = _sinkFactory != null
                        ? _sinkFactory(streamDir, sessionGuid, info.Id, MaxChunkBytes)
                        : new ChunkWriter(streamDir, sessionGuid, info.Id, MaxChunkBytes);

                    var ch = new StreamChannel
                    {
                        Queue = new BlockingCollection<byte[]>(_queueCapacity),
                        Sink = sink,
                    };
                    built.Add(ch);

                    if (_startWriters)
                    {
                        var capture = ch;
                        ch.Writer = new Thread(() => WriterLoop(capture))
                        {
                            IsBackground = true,
                            Name = $"rec-{info.Id}"
                        };
                        ch.Writer.Start();
                    }
                    _channels[info.Id] = ch;
                }
                _running = true;
            }
            catch
            {
                // 回滚：已建 channel 停止队列、Join 写线程、释放 sink
                foreach (var ch in built)
                {
                    try { ch.Queue.CompleteAdding(); } catch { }
                    try { ch.Writer?.Join(2000); } catch { }
                    try { ch.Sink?.Dispose(); } catch { }
                    try { ch.Queue.Dispose(); } catch { }
                }
                throw;
            }
        }

        /// <summary>
        /// 采集线程调用。队列满、已停止、未知 streamId 或队列关闭竞态均返回 false。
        /// 帧数组入队后归录制器所有，调用方不得复用。
        /// </summary>
        public bool Enqueue(string streamId, byte[] frame)
        {
            if (!_running) return false;
            if (!_channels.TryGetValue(streamId, out var ch)) return false;
            try
            {
                if (ch.Queue.TryAdd(frame)) return true;
            }
            catch (InvalidOperationException)
            {
                // CompleteAdding 后的竞态窗口
            }
            Interlocked.Increment(ref ch.Dropped);
            return false;
        }

        public long GetDroppedFrames(string streamId) => Interlocked.Read(ref _channels[streamId].Dropped);
        public long GetFrameCount(string streamId) => Interlocked.Read(ref _channels[streamId].FrameCount);

        private static void WriterLoop(StreamChannel ch)
        {
            foreach (var frame in ch.Queue.GetConsumingEnumerable())
            {
                if (ch.Fault != null)
                {
                    // 已故障：继续消费队列但丢弃帧（防 Stop 的 Queue.CompleteAdding 无法排空而阻塞）
                    continue;
                }
                try
                {
                    ch.Sink.Append(frame);
                    Interlocked.Increment(ref ch.FrameCount);
                }
                catch (Exception ex)
                {
                    // 记录第一个异常；后续帧在循环顶部被丢弃
                    if (ch.Fault == null) ch.Fault = ex;
                }
            }
        }

        /// <summary>停止：拒绝新帧 → 排空队列 → 关 writer → 终化 manifest。自身绝不抛。</summary>
        public void Stop()
        {
            if (!_running) return;
            _running = false;

            foreach (var ch in _channels.Values)
            {
                try { ch.Queue.CompleteAdding(); } catch { }
                try { ch.Writer?.Join(10000); } catch { }
                try { ch.Sink?.Dispose(); } catch { }
            }

            foreach (var info in _meta.Streams)
            {
                if (_channels.TryGetValue(info.Id, out var ch))
                {
                    info.FrameCount = Interlocked.Read(ref ch.FrameCount);
                    info.DroppedFrames = Interlocked.Read(ref ch.Dropped);
                }
            }

            // 任一 channel 存在写线程故障 → Failed，否则 Completed
            bool anyFault = false;
            foreach (var ch in _channels.Values)
                if (ch.Fault != null) { anyFault = true; break; }

            _meta.Status = anyFault ? SessionStatus.Failed : SessionStatus.Completed;
            try { Manifest.Save(SessionDir, _meta); } catch { }
        }

        /// <summary>幂等。二次调用为 no-op。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_running) Stop();
            foreach (var ch in _channels.Values)
            {
                try { ch.Queue.Dispose(); } catch { }
            }
        }
    }
}
