// Assets/Tests/EditMode/Schema/PrimitivesTests.cs
using NUnit.Framework;
using PicoTest.Core.Schema;

namespace PicoTest.Tests.EditMode.Schema
{
    public class PrimitivesTests
    {
        [Test]
        public void Vec3f_RoundTrip_ViaBytes()
        {
            var v = new Vec3f(1.5f, -2.25f, 3.75f);
            var buf = new byte[Vec3f.Size];
            v.WriteTo(buf, 0);
            var back = Vec3f.ReadFrom(buf, 0);
            Assert.AreEqual(v, back);
        }

        [Test]
        public void Quatf_RoundTrip_ViaBytes()
        {
            var q = new Quatf(0.1f, 0.2f, 0.3f, 0.9f);
            var buf = new byte[Quatf.Size];
            q.WriteTo(buf, 0);
            Assert.AreEqual(q, Quatf.ReadFrom(buf, 0));
        }

        [Test]
        public void Sizes_AreFixed()
        {
            Assert.AreEqual(12, Vec3f.Size);
            Assert.AreEqual(16, Quatf.Size);
        }
    }
}
