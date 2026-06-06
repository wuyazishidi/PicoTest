// Assets/Main/Core/Schema/Frames.cs
using System;
using System.Buffers.Binary;
using System.IO;

namespace PicoTest.Core.Schema
{
    /// <summary>24 骨骼点（对齐 PICO Body Tracking 布局）。固定 680 字节。</summary>
    public sealed class BodyPoseFrame
    {
        public const int JointCount = 24;
        public const int ByteSize = 8 + JointCount * (Vec3f.Size + Quatf.Size);

        public long TimestampNs { get; }
        private readonly Vec3f[] _positions = new Vec3f[JointCount];
        private readonly Quatf[] _rotations = new Quatf[JointCount];

        public BodyPoseFrame(long timestampNs) { TimestampNs = timestampNs; }

        public void SetJoint(int index, Vec3f position, Quatf rotation)
        {
            _positions[index] = position;
            _rotations[index] = rotation;
        }

        public Vec3f GetJointPosition(int index) => _positions[index];
        public Quatf GetJointRotation(int index) => _rotations[index];

        public byte[] ToBytes()
        {
            var buf = new byte[ByteSize];
            BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(0), TimestampNs);
            int o = 8;
            for (int i = 0; i < JointCount; i++)
            {
                _positions[i].WriteTo(buf, o); o += Vec3f.Size;
                _rotations[i].WriteTo(buf, o); o += Quatf.Size;
            }
            return buf;
        }

        public static BodyPoseFrame FromBytes(byte[] buf)
        {
            if (buf.Length < ByteSize)
                throw new InvalidDataException($"BodyPoseFrame needs {ByteSize} bytes, got {buf.Length}");
            var f = new BodyPoseFrame(BinaryPrimitives.ReadInt64LittleEndian(buf.AsSpan(0)));
            int o = 8;
            for (int i = 0; i < JointCount; i++)
            {
                var p = Vec3f.ReadFrom(buf, o); o += Vec3f.Size;
                var r = Quatf.ReadFrom(buf, o); o += Quatf.Size;
                f.SetJoint(i, p, r);
            }
            return f;
        }
    }

    /// <summary>视频帧（M2 为合成载荷；真实视频 M4 走 mp4+index 外部容器）。</summary>
    public sealed class VideoFrameMeta
    {
        public long TimestampNs { get; }
        public long FrameIndex { get; }
        public int Width { get; }
        public int Height { get; }
        public byte[] Payload { get; }
        public uint Crc32 { get; }

        public VideoFrameMeta(long timestampNs, long frameIndex, int width, int height, byte[] payload)
            : this(timestampNs, frameIndex, width, height, payload, Schema.Crc32.Compute(payload)) { }

        private VideoFrameMeta(long ts, long idx, int w, int h, byte[] payload, uint crc)
        {
            TimestampNs = ts; FrameIndex = idx; Width = w; Height = h; Payload = payload; Crc32 = crc;
        }

        public byte[] ToBytes()
        {
            var buf = new byte[8 + 8 + 4 + 4 + 4 + 4 + Payload.Length];
            BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(0),  TimestampNs);
            BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(8),  FrameIndex);
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(16), Width);
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(20), Height);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(24), Crc32);
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(28), Payload.Length);
            Payload.CopyTo(buf, 32);
            return buf;
        }

        public static VideoFrameMeta FromBytes(byte[] buf)
        {
            if (buf.Length < 32)
                throw new InvalidDataException($"VideoFrameMeta needs at least 32 bytes for header, got {buf.Length}");
            long ts  = BinaryPrimitives.ReadInt64LittleEndian(buf.AsSpan(0));
            long idx = BinaryPrimitives.ReadInt64LittleEndian(buf.AsSpan(8));
            int  w   = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(16));
            int  h   = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(20));
            uint crc = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(24));
            int  len = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(28));
            if (len < 0 || 32 + (long)len > buf.Length)
                throw new InvalidDataException($"VideoFrameMeta payload length {len} is invalid for buffer size {buf.Length}");
            var payload = new byte[len];
            Array.Copy(buf, 32, payload, 0, len);
            return new VideoFrameMeta(ts, idx, w, h, payload, crc);
        }
    }
}
