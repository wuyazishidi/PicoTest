// Assets/Tests/EditMode/Rendering/FisheyeProjectionTests.cs
using System;
using NUnit.Framework;
using PicoTest.Core.Rendering;

namespace PicoTest.Tests.EditMode.Rendering
{
    public class FisheyeProjectionTests
    {
        // 典型 220° 鱼眼内参（width=1600,height=1600,光心居中,等距 fx=像素/弧度）
        // thetaMax = 110° = 1.91986 rad；等距模型 fx ≈ (width/2)/thetaMax
        private static FisheyeProjection MakeIdeal(out double thetaMax)
        {
            thetaMax = 110.0 * Math.PI / 180.0;
            double fx = 800.0 / thetaMax; // 半宽 800 像素映射到 thetaMax
            return new FisheyeProjection(
                fx: fx, fy: fx, cx: 800, cy: 800,
                k1: 0, k2: 0, k3: 0, k4: 0,
                width: 1600, height: 1600, thetaMaxRad: thetaMax,
                rEyeRowMajor: Identity3x3());
        }

        private static double[] Identity3x3() => new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };

        [Test]
        public void OpticalAxis_MapsToPrincipalPoint()
        {
            var p = MakeIdeal(out _);
            Assert.IsTrue(p.ProjectDirection(0, 0, 1, out double u, out double v, out bool inFov));
            Assert.IsTrue(inFov);
            Assert.AreEqual(800.0, u, 1e-6);
            Assert.AreEqual(800.0, v, 1e-6);
        }

        [Test]
        public void EquidistantNoDistortion_IsAnalytic()
        {
            // 方向偏右 30°：theta=π/6, phi=0 → u = cx + fx*theta, v = cy
            var p = MakeIdeal(out _);
            double theta = Math.PI / 6;
            double dx = Math.Sin(theta), dz = Math.Cos(theta);
            Assert.IsTrue(p.ProjectDirection(dx, 0, dz, out double u, out double v, out _));
            double fx = 800.0 / (110.0 * Math.PI / 180.0);
            Assert.AreEqual(800.0 + fx * theta, u, 1e-4);
            Assert.AreEqual(800.0, v, 1e-4);
        }

        [Test]
        public void BeyondThetaMax_IsOutOfFov()
        {
            var p = MakeIdeal(out double thetaMax);
            double theta = thetaMax + 0.05;
            double dx = Math.Sin(theta), dz = Math.Cos(theta);
            p.ProjectDirection(dx, 0, dz, out _, out _, out bool inFov);
            Assert.IsFalse(inFov);
        }

        [Test]
        public void NearAxis_NoNaN()
        {
            var p = MakeIdeal(out _);
            Assert.IsTrue(p.ProjectDirection(1e-9, 1e-9, 1, out double u, out double v, out _));
            Assert.IsFalse(double.IsNaN(u) || double.IsNaN(v));
        }

        [Test]
        public void Distortion_MatchesForwardModelGolden()
        {
            // k1..k4 非零；golden = 同一前向多项式 double 精度求值（钉住实现一致性）
            // theta=π/4, k1=0.05,k2=-0.01 → theta_d = theta*(1+k1*θ²+k2*θ⁴)
            var p = new FisheyeProjection(
                fx: 500, fy: 500, cx: 800, cy: 800,
                k1: 0.05, k2: -0.01, k3: 0, k4: 0,
                width: 1600, height: 1600, thetaMaxRad: 2.0,
                rEyeRowMajor: Identity3x3());
            double theta = Math.PI / 4;
            double t2 = theta * theta;
            double thetaD = theta * (1 + 0.05 * t2 + (-0.01) * t2 * t2);
            double dx = Math.Sin(theta), dz = Math.Cos(theta);
            p.ProjectDirection(dx, 0, dz, out double u, out _, out _);
            Assert.AreEqual(800.0 + 500.0 * thetaD, u, 1e-4);
        }

        [Test]
        public void Extrinsic_RotatesDirectionBeforeProjection()
        {
            // R_eye 把世界 +Y 转到相机 +Z：绕 X 轴 +90°(Rx90·(0,1,0)=(0,0,1))→落在光心
            double[] rotXpos90 = { 1, 0, 0, 0, 0, -1, 0, 1, 0 };
            var p = new FisheyeProjection(500, 500, 800, 800, 0, 0, 0, 0, 1600, 1600, 2.0, rotXpos90);
            p.ProjectDirection(0, 1, 0, out double u, out double v, out bool inFov);
            Assert.IsTrue(inFov);
            Assert.AreEqual(800.0, u, 1e-4);
            Assert.AreEqual(800.0, v, 1e-4);
        }
    }
}
