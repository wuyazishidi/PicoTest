// Assets/Experiments/Exp-RobotDsDome/Tests/DoubleSphereProjectionTests.cs
using NUnit.Framework;
using PicoTest.Experiments.RobotDsDome;

namespace PicoTest.Experiments.RobotDsDome.Tests
{
    /// <summary>
    /// DS 前向投影 golden 断言：基准值由 Python 原版 ds_project（Tools/undistort_ds.py）
    /// 用 cam0 真实内参算出，逐条 &lt;0.01px。保证 C# 与 shader 照抄的公式与工具一致。
    /// </summary>
    public class DoubleSphereProjectionTests
    {
        // cam0（3-camchain.yaml），标定分辨率 1920×1080
        private const double Xi = -0.0013188131126766385;
        private const double Alpha = 0.5698437482720973;
        private const double Fx = 509.53190418244196;
        private const double Fy = 509.0896373470489;
        private const double Cx = 962.149909488436;
        private const double Cy = 551.4395377954257;
        private const int W = 1920, H = 1080;

        private static DoubleSphereProjection Cam0() =>
            new DoubleSphereProjection(Xi, Alpha, Fx, Fy, Cx, Cy, W, H);

        private static void AssertRay(double x, double y, double z, double eu, double ev, bool eValid)
        {
            var p = Cam0();
            p.ProjectDirection(x, y, z, out double u, out double v, out bool valid);
            Assert.AreEqual(eu, u, 0.01, $"u for ray({x},{y},{z})");
            Assert.AreEqual(ev, v, 0.01, $"v for ray({x},{y},{z})");
            Assert.AreEqual(eValid, valid, $"valid for ray({x},{y},{z})");
        }

        [Test] public void Center_MapsToPrincipalPoint() => AssertRay(0, 0, 1, 962.149909, 551.439538, true);
        [Test] public void RightOffset() => AssertRay(0.3, 0, 1, 1111.463749, 551.439538, true);
        [Test] public void DownOffset() => AssertRay(0, 0.3, 1, 962.149909, 700.623774, true);
        [Test] public void DiagonalOffset() => AssertRay(0.5, 0.5, 1, 1188.282229, 777.375578, true);
        [Test] public void Wide_X1() => AssertRay(1, 0, 1, 1374.898948, 551.439538, true);
        [Test] public void Wide_X15() => AssertRay(1.5, 0, 1, 1487.194042, 551.439538, true);
        [Test] public void VeryWide_ShallowZ() => AssertRay(1, 0, 0.2, 1726.717145, 551.439538, true);
        [Test] public void StraightBehind_Invalid() => AssertRay(0, 0, -0.3, 962.149909, 551.439538, false);
        [Test] public void WideBehind_StillValidRay() => AssertRay(2, 0, -0.5, 2024.901229, 551.439538, true);

        [Test]
        public void W2_MatchesPython()
        {
            Assert.AreEqual(0.7542988498, DoubleSphereProjection.ComputeW2(Xi, Alpha), 1e-9);
        }

        [Test]
        public void NormalizedUV_DividesByResolution()
        {
            var p = Cam0();
            p.ProjectToUV(0, 0, 1, out double un, out double vn, out _);
            Assert.AreEqual(Cx / W, un, 1e-9);
            Assert.AreEqual(Cy / H, vn, 1e-9);
        }
    }
}
