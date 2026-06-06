// Assets/Tests/EditMode/Schema/Crc32Tests.cs
using NUnit.Framework;
using PicoTest.Core.Schema;
using System.Text;

namespace PicoTest.Tests.EditMode.Schema
{
    public class Crc32Tests
    {
        [Test]
        public void Crc32_KnownVector()
        {
            // CRC32("123456789") = 0xCBF43926（标准测试向量）
            var crc = Crc32.Compute(Encoding.ASCII.GetBytes("123456789"));
            Assert.AreEqual(0xCBF43926u, crc);
        }

        [Test]
        public void Crc32_EmptyInput_ReturnsZero()
        {
            Assert.AreEqual(0u, Crc32.Compute(new byte[0]));
        }

        [Test]
        public void Crc32_OffsetSlice_MatchesFullCompute()
        {
            // "XX123456789" with offset=2, count=9 should equal CRC32("123456789")
            var data = Encoding.ASCII.GetBytes("XX123456789");
            var crc = Crc32.Compute(data, 2, 9);
            Assert.AreEqual(0xCBF43926u, crc);
        }
    }
}
