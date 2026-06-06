// Assets/Main/Core/Schema/Primitives.cs
using System;
using System.Buffers.Binary;

namespace PicoTest.Core.Schema
{
    /// <summary>流类型。值入盘，禁止改已有值，只许追加。</summary>
    public enum StreamType { BodyPose = 1, Video = 2, PointCloud = 3 }

    public readonly struct Vec3f : IEquatable<Vec3f>
    {
        public const int Size = 12;
        public readonly float X, Y, Z;
        public Vec3f(float x, float y, float z) { X = x; Y = y; Z = z; }

        public void WriteTo(byte[] buf, int offset)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset),     BitConverter.SingleToInt32Bits(X));
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset + 4), BitConverter.SingleToInt32Bits(Y));
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset + 8), BitConverter.SingleToInt32Bits(Z));
        }

        public static Vec3f ReadFrom(byte[] buf, int offset) =>
            new Vec3f(
                BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(offset))),
                BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(offset + 4))),
                BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(offset + 8))));

        public bool Equals(Vec3f o) => X == o.X && Y == o.Y && Z == o.Z;
        public override bool Equals(object o) => o is Vec3f v && Equals(v);
        public override int GetHashCode() => System.HashCode.Combine(X, Y, Z);
    }

    public readonly struct Quatf : IEquatable<Quatf>
    {
        public const int Size = 16;
        public readonly float X, Y, Z, W;
        public Quatf(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }

        public void WriteTo(byte[] buf, int offset)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset),      BitConverter.SingleToInt32Bits(X));
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset + 4),  BitConverter.SingleToInt32Bits(Y));
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset + 8),  BitConverter.SingleToInt32Bits(Z));
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset + 12), BitConverter.SingleToInt32Bits(W));
        }

        public static Quatf ReadFrom(byte[] buf, int offset) =>
            new Quatf(
                BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(offset))),
                BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(offset + 4))),
                BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(offset + 8))),
                BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(offset + 12))));

        public bool Equals(Quatf o) => X == o.X && Y == o.Y && Z == o.Z && W == o.W;
        public override bool Equals(object o) => o is Quatf q && Equals(q);
        public override int GetHashCode() => System.HashCode.Combine(X, Y, Z, W);
    }
}
