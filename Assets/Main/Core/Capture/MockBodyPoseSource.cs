// Assets/Main/Core/Capture/MockBodyPoseSource.cs
using System;
using PicoTest.Core.Schema;

namespace PicoTest.Core.Capture
{
    /// <summary>合成 24 关节运动（正弦驱动，种子确定）。72Hz。</summary>
    public sealed class MockBodyPoseSource : ICaptureSource
    {
        public string StreamId => "body_pose";
        public StreamType Type => StreamType.BodyPose;
        public int NominalHz => 72;
        public event Action<long, byte[]> FrameProduced;

        private readonly int _seed;
        private long _intervalNs;
        private long _nextDueNs;
        private bool _running;

        public MockBodyPoseSource(int seed = 1) { _seed = seed; }

        public void Start()
        {
            _intervalNs = 1_000_000_000L / NominalHz;
            _nextDueNs = 0;
            _running = true;
        }

        public void Tick(long nowNs)
        {
            if (!_running) return;
            while (nowNs >= _nextDueNs)
            {
                FrameProduced?.Invoke(_nextDueNs, Synthesize(_nextDueNs));
                _nextDueNs += _intervalNs;
            }
        }

        public void Stop() => _running = false;

        private byte[] Synthesize(long tsNs)
        {
            var f = new BodyPoseFrame(tsNs);
            double t = tsNs / 1e9;
            for (int j = 0; j < BodyPoseFrame.JointCount; j++)
            {
                float phase = _seed * 0.37f + j * 0.21f;
                f.SetJoint(j,
                    new Vec3f((float)Math.Sin(t + phase), 1.0f + j * 0.05f, (float)Math.Cos(t + phase)),
                    new Quatf(0, (float)Math.Sin((t + phase) / 2), 0, (float)Math.Cos((t + phase) / 2)));
            }
            return f.ToBytes();
        }
    }
}
