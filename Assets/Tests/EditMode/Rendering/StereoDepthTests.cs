// Assets/Tests/EditMode/Rendering/StereoDepthTests.cs
using System;
using NUnit.Framework;
using PicoTest.Core.Rendering;

namespace PicoTest.Tests.EditMode.Rendering
{
    public class StereoDepthTests
    {
        // 本机真机 K_rectified.fx≈371.4，基线 0.064m
        private const double Fx = 371.38806263252195;
        private const double Baseline = 0.064;
        private const double Cx = 640.0, Cy = 480.0;

        // Z = fx·B / d：d=10px → 371.388*0.064/10 = 2.377m
        [Test]
        public void DepthFromDisparity_MatchesFormula()
        {
            double z = StereoDepth.DepthFromDisparity(10.0, Fx, Baseline);
            Assert.AreEqual(Fx * Baseline / 10.0, z, 1e-9);
            Assert.AreEqual(2.3768836, z, 1e-6);
        }

        // 视差越大 → 深度越小（单调，近物体视差大）
        [Test]
        public void LargerDisparity_MeansCloser()
        {
            double near = StereoDepth.DepthFromDisparity(40.0, Fx, Baseline);
            double far = StereoDepth.DepthFromDisparity(5.0, Fx, Baseline);
            Assert.Less(near, far);
        }

        // 无效视差（0/负）→ +∞ 且 valid=false
        [Test]
        public void ZeroOrNegativeDisparity_IsInvalidInfinity()
        {
            Assert.IsTrue(double.IsPositiveInfinity(StereoDepth.DepthFromDisparity(0.0, Fx, Baseline)));
            Assert.IsFalse(StereoDepth.TryDepthFromDisparity(0.0, Fx, Baseline, out _));
            Assert.IsFalse(StereoDepth.TryDepthFromDisparity(-3.0, Fx, Baseline, out _));
            Assert.IsTrue(StereoDepth.TryDepthFromDisparity(12.0, Fx, Baseline, out double d) && d > 0);
        }

        // 主点像素 (cx,cy) + 视差 → 点在光轴上：X=0,Y=0,Z=深度
        [Test]
        public void PrincipalPointPixel_LiesOnOpticalAxis()
        {
            bool ok = StereoDepth.PointFromDisparity(Cx, Cy, 20.0, Fx, Fx, Cx, Cy, Baseline,
                out double x, out double y, out double z);
            Assert.IsTrue(ok);
            Assert.AreEqual(0.0, x, 1e-9);
            Assert.AreEqual(0.0, y, 1e-9);
            Assert.AreEqual(Fx * Baseline / 20.0, z, 1e-9);
        }

        // 偏离主点：X = (u−cx)·Z/fx，方向/量级正确
        [Test]
        public void OffAxisPixel_BackprojectsCorrectly()
        {
            double u = Cx + 100.0, d = 20.0;
            bool ok = StereoDepth.PointFromDisparity(u, Cy, d, Fx, Fx, Cx, Cy, Baseline,
                out double x, out double y, out double z);
            Assert.IsTrue(ok);
            Assert.AreEqual(100.0 * z / Fx, x, 1e-9);
            Assert.Greater(x, 0.0);              // 主点右侧 → +X
            Assert.AreEqual(0.0, y, 1e-9);
        }

        // 无效视差 → PointFromDisparity 返回 false，点置零
        [Test]
        public void InvalidDisparity_PointReturnsFalse()
        {
            bool ok = StereoDepth.PointFromDisparity(Cx, Cy, 0.0, Fx, Fx, Cx, Cy, Baseline,
                out double x, out double y, out double z);
            Assert.IsFalse(ok);
            Assert.AreEqual(0.0, x + y + z, 1e-12);
        }
    }
}
