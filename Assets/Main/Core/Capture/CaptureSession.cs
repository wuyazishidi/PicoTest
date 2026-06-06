// Assets/Main/Core/Capture/CaptureSession.cs
using System;
using System.Collections.Generic;
using System.Linq;
using PicoTest.Core.Recording;

namespace PicoTest.Core.Capture
{
    public sealed class StreamSnapshot
    {
        public string Id;
        public long FrameCount;
        public long DroppedFrames;
    }

    public sealed class SessionSnapshot
    {
        public bool IsRecording;
        public string SessionId;
        public string FaultMessage; // 录制器故障时非空（供 RemoteBridge/操控台显示）
        public List<StreamSnapshot> Streams = new List<StreamSnapshot>();
    }

    /// <summary>
    /// 采集会话编排：装配源→录制器，Tick 泵送，快照供 RemoteBridge/操控台。
    /// 每次 Start 创建全新 SessionRecorder（全新 SessionId/目录）；源实例复用。
    /// 注意：源的 FrameProduced 事件在每次 Start 时重新挂接到新 recorder —— 通过先退订再订阅避免重复投递。
    /// </summary>
    public sealed class CaptureSession : IDisposable
    {
        private readonly string _root;
        private readonly string _deviceInfo;
        private readonly ICaptureSource[] _sources;
        private readonly Dictionary<ICaptureSource, Action<long, byte[]>> _handlers
            = new Dictionary<ICaptureSource, Action<long, byte[]>>();
        private SessionRecorder _recorder;
        private bool _recording;

        public string SessionDir => _recorder?.SessionDir;

        public CaptureSession(string root, string deviceInfo, ICaptureSource[] sources)
        {
            _root = root; _deviceInfo = deviceInfo; _sources = sources;
        }

        public void Start()
        {
            if (_recording) return;
            var meta = SessionMeta.CreateNew(_deviceInfo);
            foreach (var s in _sources)
                meta.Streams.Add(new StreamInfo { Id = s.StreamId, Type = s.Type, NominalHz = s.NominalHz });

            _recorder = new SessionRecorder(_root, meta);
            _recorder.Start();
            foreach (var s in _sources)
            {
                // 退订旧 handler（指向上一个 recorder），再订阅新的
                if (_handlers.TryGetValue(s, out var old)) s.FrameProduced -= old;
                var captured = s;
                Action<long, byte[]> handler = (ts, bytes) => _recorder.Enqueue(captured.StreamId, bytes);
                _handlers[s] = handler;
                s.FrameProduced += handler;
                s.Start();
            }
            _recording = true;
        }

        public void Tick(long nowNs)
        {
            if (!_recording) return;
            foreach (var s in _sources) s.Tick(nowNs);
        }

        public void AddTag(string tag)
        {
            if (_recorder != null && !_recorder.Meta.Tags.Contains(tag))
                _recorder.Meta.Tags.Add(tag);
        }

        public void Stop()
        {
            if (!_recording) return;
            foreach (var s in _sources) s.Stop();
            _recorder.Stop();
            _recording = false;
        }

        public SessionSnapshot GetSnapshot()
        {
            var snap = new SessionSnapshot
            {
                IsRecording = _recording,
                SessionId = _recorder?.Meta.SessionId,
                FaultMessage = _recorder?.FirstFault?.Message,
            };
            if (_recorder != null && _recording)
                snap.Streams = _recorder.Meta.Streams
                    .Select(i => new StreamSnapshot
                    {
                        Id = i.Id,
                        FrameCount = _recorder.GetFrameCount(i.Id),
                        DroppedFrames = _recorder.GetDroppedFrames(i.Id),
                    }).ToList();
            return snap;
        }

        public void Dispose() { Stop(); _recorder?.Dispose(); }
    }
}
