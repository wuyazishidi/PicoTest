using NUnit.Framework;
using PicoTest.Core;

namespace PicoTest.Tests.EditMode
{
    /// <summary>冒烟测试：验证测试链路（asmdef 引用、Test Runner、RunTests 工具）整体可用。</summary>
    public class CoreSmokeTests
    {
        [Test]
        public void CoreInfo_Ping_ReturnsPong()
        {
            Assert.AreEqual("pong", CoreInfo.Ping());
        }

        [Test]
        public void CoreInfo_SchemaVersion_IsPositive()
        {
            Assert.GreaterOrEqual(CoreInfo.SchemaVersion, 1);
        }
    }
}
