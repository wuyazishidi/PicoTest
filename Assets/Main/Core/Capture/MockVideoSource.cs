// Assets/Main/Core/Capture/MockVideoSource.cs
using System;
using PicoTest.Core.Schema;

namespace PicoTest.Core.Capture
{
    /// <summary>合成视频帧（小载荷伪图样）。30fps。</summary>
    public sealed class MockVideoSource : ICaptureSource
    {
        public string StreamId => "video";
        public StreamType Type => StreamType.Video;
        public int NominalHz => 30;
        public event Action<long, byte[]> FrameProduced;

        private long _intervalNs;
        private long _nextDueNs;
        private long _frameIndex;
        private bool _running;

        public void Start()
        {
            _intervalNs = 1_000_000_000L / NominalHz;
            _nextDueNs = 0;
            _frameIndex = 0;
            _running = true;
        }

        public void Tick(long nowNs)
        {
            if (!_running) return;
            while (nowNs >= _nextDueNs)
            {
                var payload = new byte[256];
                for (int i = 0; i < payload.Length; i++)
                    payload[i] = (byte)((_frameIndex + i) & 0xFF);
                var frame = new VideoFrameMeta(_nextDueNs, _frameIndex, 640, 480, payload);
                FrameProduced?.Invoke(_nextDueNs, frame.ToBytes());
                _frameIndex++;
                _nextDueNs += _intervalNs;
            }
        }

        public void Stop() => _running = false;
    }
}
