// Assets/Tests/EditMode/Rendering/CamCalibTests.cs
using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using PicoTest.Core.Rendering;

namespace PicoTest.Tests.EditMode.Rendering
{
    /// <summary>烤进 StreamingAssets 的设备真实标定（cam_calib.json）解析 + 投影桥接测试。</summary>
    public class CamCalibTests
    {
        private static CamCalib LoadBaked()
        {
            // 编辑器下 streamingAssetsPath == <project>/Assets/StreamingAssets，直接读磁盘文件
            string path = Path.Combine(Application.streamingAssetsPath, "cam_calib.json");
            Assert.IsTrue(File.Exists(path), $"baked calib missing: {path}");
            return CamCalib.Parse(File.ReadAllText(path));
        }

        [Test]
        public void BakedCalib_ParsesHeaderFields()
        {
            var c = LoadBaked();
            Assert.AreEqual("equiDis62", c.Model);
            Assert.AreEqual(1280, c.Width);
            Assert.AreEqual(960, c.Height);
            Assert.AreEqual(0.064, c.StereoBaselineM, 1e-9);
        }

        [Test]
        public void BakedCalib_LeftIntrinsicsAndDistortion()
        {
            // 结构性断言（不钉具体数值 → 重标定后不误红）：访问器映射 + D 长度 + 合理量级
            var L = LoadBaked().Left;
            Assert.AreEqual(L.K[0][0], L.Fx, 1e-12, "fx = K[0][0]");
            Assert.AreEqual(L.K[1][1], L.Fy, 1e-12, "fy = K[1][1]");
            Assert.AreEqual(L.K[0][2], L.Cx, 1e-12, "cx = K[0][2]");
            Assert.AreEqual(L.K[1][2], L.Cy, 1e-12, "cy = K[1][2]");
            Assert.AreEqual(8, L.D.Length, "D = k1..k6,p1,p2");
            Assert.That(L.Fx, Is.InRange(100.0, 2000.0), "fx 量级合理");
            Assert.That(L.Cx, Is.InRange(0.0, 1280.0), "cx 落在像宽内");
            Assert.That(L.Cy, Is.InRange(0.0, 960.0), "cy 落在像高内");
        }

        [Test]
        public void Get_ReturnsRequestedEye()
        {
            var c = LoadBaked();
            Assert.AreSame(c.Left, c.Get(Eye.Left));
            Assert.AreSame(c.Right, c.Get(Eye.Right));
            Assert.AreNotEqual(c.Left.Fx, c.Right.Fx); // 左右内参确实不同
        }

        [Test]
        public void ToProjection_OpticalAxis_MapsToPrincipalPoint()
        {
            var c = LoadBaked();
            var p = c.ToProjection(Eye.Left, thetaMaxRad: 1.4); // R 默认单位阵
            Assert.IsTrue(p.ProjectDirection(0, 0, 1, out double u, out double v, out bool inFov));
            Assert.IsTrue(inFov);
            Assert.AreEqual(c.Left.Cx, u, 1e-6);
            Assert.AreEqual(c.Left.Cy, v, 1e-6);
        }

        [Test]
        public void ToProjection_UsesD0Through5_DropsTangential()
        {
            var L = LoadBaked().Left;
            var p = LoadBaked().ToProjection(Eye.Left, 1.4);
            double theta = 1.0, dx = Math.Sin(theta), dz = Math.Cos(theta); // θ=1 → t²=1
            Assert.IsTrue(p.ProjectDirection(dx, 0, dz, out double u, out _, out _));
            double thetaD = theta * (1 + L.D[0] + L.D[1] + L.D[2] + L.D[3] + L.D[4] + L.D[5]);
            Assert.AreEqual(L.Fx * thetaD + L.Cx, u, 1e-3); // 与 p1/p2 无关 → 证明已丢弃切向
        }

        [Test]
        public void Parse_BadJson_Throws()
        {
            Assert.Throws<FormatException>(() => CamCalib.Parse("{ not json"));
            Assert.Throws<FormatException>(() => CamCalib.Parse(""));
        }
    }
}
