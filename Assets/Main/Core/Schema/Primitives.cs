// Assets/Main/Core/Schema/Primitives.cs
using System;

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
            BitConverter.GetBytes(X).CopyTo(buf, offset);
            BitConverter.GetBytes(Y).CopyTo(buf, offset + 4);
            BitConverter.GetBytes(Z).CopyTo(buf, offset + 8);
        }

        public static Vec3f ReadFrom(byte[] buf, int offset) =>
            new Vec3f(BitConverter.ToSingle(buf, offset),
                      BitConverter.ToSingle(buf, offset + 4),
                      BitConverter.ToSingle(buf, offset + 8));

        public bool Equals(Vec3f o) => X == o.X && Y == o.Y && Z == o.Z;
        public override bool Equals(object o) => o is Vec3f v && Equals(v);
        public override int GetHashCode() => X.GetHashCode() ^ Y.GetHashCode() ^ Z.GetHashCode();
    }

    public readonly struct Quatf : IEquatable<Quatf>
    {
        public const int Size = 16;
        public readonly float X, Y, Z, W;
        public Quatf(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }

        public void WriteTo(byte[] buf, int offset)
        {
            BitConverter.GetBytes(X).CopyTo(buf, offset);
            BitConverter.GetBytes(Y).CopyTo(buf, offset + 4);
            BitConverter.GetBytes(Z).CopyTo(buf, offset + 8);
            BitConverter.GetBytes(W).CopyTo(buf, offset + 12);
        }

        public static Quatf ReadFrom(byte[] buf, int offset) =>
            new Quatf(BitConverter.ToSingle(buf, offset),
                      BitConverter.ToSingle(buf, offset + 4),
                      BitConverter.ToSingle(buf, offset + 8),
                      BitConverter.ToSingle(buf, offset + 12));

        public bool Equals(Quatf o) => X == o.X && Y == o.Y && Z == o.Z && W == o.W;
        public override bool Equals(object o) => o is Quatf q && Equals(q);
        public override int GetHashCode() => X.GetHashCode() ^ Y.GetHashCode() ^ Z.GetHashCode() ^ W.GetHashCode();
    }
}
