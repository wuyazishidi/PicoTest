// Assets/Tests/EditMode/Rendering/EyeReprojectionTests.cs
using System;
using NUnit.Framework;
using PicoTest.Core.Rendering;

namespace PicoTest.Tests.EditMode.Rendering
{
    public class EyeReprojectionTests
    {
        // 相机与眼睛重合(t=0)：采样方向 == 归一化的眼视线，与深度无关（无视差）。
        [Test]
        public void ZeroOffset_ReturnsEyeRay_RegardlessOfDepth()
        {
            EyeReprojection.CameraRayForEyeRay(0.3, -0.2, 1.0, depth: 5.0,
                tx: 0, ty: 0, tz: 0, out double dx, out double dy, out double dz);
            double len = Math.Sqrt(0.3 * 0.3 + 0.2 * 0.2 + 1.0);
            Assert.AreEqual(0.3 / len * 5.0, dx, 1e-9);
            Assert.AreEqual(-0.2 / len * 5.0, dy, 1e-9);
            Assert.AreEqual(1.0 / len * 5.0, dz, 1e-9);
        }

        // 深度→∞：平移被淹没，归一化采样方向收敛到眼视线（视差消失 = 退化为纯旋转穹顶）。
        [Test]
        public void DepthToInfinity_ConvergesToEyeDirection()
        {
            // 眼视线正前方 +Z，相机在右侧 0.05m（典型半基线）
            void Dir(double depth, out double nx, out double ny, out double nz)
            {
                EyeReprojection.CameraRayForEyeRay(0, 0, 1, depth,
                    tx: 0.05, ty: 0, tz: 0, out double dx, out double dy, out double dz);
                double l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                nx = dx / l; ny = dy / l; nz = dz / l;
            }

            Dir(1.0, out double nx1, out _, out _);
            Dir(1000.0, out double nx2, out _, out double nz2);
            // 远处方向的 x 分量应远小于近处（视差随距离衰减）
            Assert.Less(Math.Abs(nx2), Math.Abs(nx1));
            Assert.AreEqual(1.0, nz2, 1e-4);       // 收敛到 +Z
            Assert.AreEqual(0.0, nx2, 1e-4);
        }

        // 有限深度 + 平移：近处物体在相机里的方向相对眼睛发生可预测偏移（视差核心）。
        [Test]
        public void FiniteDepth_ProducesParallaxShift()
        {
            // 正前方 1m 处的点，相机在眼睛右方 0.05m → 相机看它应偏左（-x）
            EyeReprojection.CameraRayForEyeRay(0, 0, 1, depth: 1.0,
                tx: 0.05, ty: 0, tz: 0, out double dx, out double dy, out double dz);
            // P−C = (0,0,1) − (0.05,0,0) = (−0.05,0,1)
            Assert.AreEqual(-0.05, dx, 1e-9);
            Assert.AreEqual(0.0, dy, 1e-9);
            Assert.AreEqual(1.0, dz, 1e-9);
        }

        // 组合验证：重投影方向喂给 FisheyeProjection，远景应落在近主点附近（正前方≈光心）。
        [Test]
        public void ComposedWithFisheye_FarPoint_NearPrincipalPoint()
        {
            double[] identity = { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
            var proj = new FisheyeProjection(
                fx: 583, fy: 577, cx: 640, cy: 480,
                k1: 0, k2: 0, k3: 0, k4: 0, k5: 0, k6: 0,
                width: 1280, height: 960, thetaMaxRad: 1.4, rEyeRowMajor: identity);

            EyeReprojection.CameraRayForEyeRay(0, 0, 1, depth: 1000.0,
                tx: 0.032, ty: 0, tz: 0, out double dx, out double dy, out double dz);
            Assert.IsTrue(proj.ProjectDirection(dx, dy, dz, out double u, out double v, out bool inFov));
            Assert.IsTrue(inFov);
            // 远处正前方应几乎落在主点(cx,cy)
            Assert.AreEqual(640.0, u, 1.0);
            Assert.AreEqual(480.0, v, 1.0);
        }

        // near-axis / 零向量守护：不产生 NaN。
        [Test]
        public void ZeroEyeRay_NoNaN()
        {
            EyeReprojection.CameraRayForEyeRay(0, 0, 0, depth: 1.0,
                tx: 0.05, ty: 0, tz: 0, out double dx, out double dy, out double dz);
            Assert.IsFalse(double.IsNaN(dx) || double.IsNaN(dy) || double.IsNaN(dz));
        }
    }
}
