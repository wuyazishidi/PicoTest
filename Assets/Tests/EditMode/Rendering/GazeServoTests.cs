// Assets/Tests/EditMode/Rendering/GazeServoTests.cs
using NUnit.Framework;
using PicoTest.Core.Rendering;

namespace PicoTest.Tests.EditMode.Rendering
{
    public class GazeServoTests
    {
        [Test]
        public void InsideDeadzone_DoesNotMove()
        {
            double next = GazeServo.Step(current: 0, target: 2, dt: 0.1, rateDegPerSec: 90, deadzoneDeg: 5);
            Assert.AreEqual(0.0, next, 1e-9);
        }

        [Test]
        public void OutsideDeadzone_RateLimited()
        {
            // target 远，dt=0.1, rate=90 → 单步最多 9°
            double next = GazeServo.Step(0, 100, 0.1, 90, 5);
            Assert.AreEqual(9.0, next, 1e-6);
        }

        [Test]
        public void Converges_WithoutOvershoot()
        {
            double cur = 0;
            for (int i = 0; i < 1000; i++)
            {
                double prev = cur;
                cur = GazeServo.Step(cur, 47, 0.016, 90, 1);
                Assert.LessOrEqual(cur, 47.0 + 1e-6, "不应越过目标");
                Assert.GreaterOrEqual(cur, prev - 1e-6, "单调逼近");
            }
            Assert.AreEqual(47.0, cur, 1.0); // 收敛到死区内
        }

        [Test]
        public void TakesShortestPath_AcrossWrap()
        {
            // 170 → -170 的最短路是 +20°（过 180 绕回），不是 -340°
            double next = GazeServo.Step(170, -170, 1.0, 50, 1);
            // 单步限 50°，最短差 +20° → 直接到 -170（190 归一化）
            Assert.AreEqual(-170.0, next, 1e-6);
        }
    }
}
