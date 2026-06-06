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
    }
}
