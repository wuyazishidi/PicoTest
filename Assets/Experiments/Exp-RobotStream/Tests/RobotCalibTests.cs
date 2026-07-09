// Assets/Experiments/Exp-RobotStream/Tests/RobotCalibTests.cs
using System.IO;
using NUnit.Framework;
using PicoTest.Core.Rendering;
using PicoTest.Experiments.RobotStream;
using PicoTest.Rendering;
using UnityEngine;

namespace PicoTest.Experiments.RobotStream.Tests
{
    /// <summary>
    /// RobotCalib：把 Pico 的 cam_calib.json 当"机器人相机"分离出正常标定参数
    /// （内参 K + 畸变 D + 外参经 ImuCamRig）→ 左右 FisheyeCalibration。真实数据入库（纯数字）。
    /// </summary>
    public class RobotCalibTests
    {
        private static CamCalib LoadReal()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "cam_calib.json");
            Assert.That(File.Exists(path), $"缺 {path}（应随库存在）");
            return CamCalib.Parse(File.ReadAllText(path));
        }

        [Test]
        public void BuildEyes_IntrinsicsMatchJsonK()
        {
            var c = LoadReal();
            var (left, right) = RobotCalib.BuildEyeCalibrations(c, useExtrinsics: true);

            Assert.AreEqual((float)c.Left.Fx, left.fx, 1e-3f, "左 fx=K[0][0]");
            Assert.AreEqual((float)c.Left.Fy, left.fy, 1e-3f, "左 fy=K[1][1]");
            Assert.AreEqual((float)c.Left.Cx, left.cx, 1e-3f, "左 cx=K[0][2]");
            Assert.AreEqual((float)c.Left.Cy, left.cy, 1e-3f, "左 cy=K[1][2]");
            Assert.AreEqual((float)c.Right.Fx, right.fx, 1e-3f, "右 fx");
            Assert.AreEqual(c.Width, left.width);
            Assert.AreEqual(c.Height, left.height);
        }

        [Test]
        public void BuildEyes_DistortionIsFirstSixRadial()
        {
            var c = LoadReal();
            var (left, _) = RobotCalib.BuildEyeCalibrations(c, useExtrinsics: true);
            var d = c.Left.D;
            Assert.AreEqual((float)d[0], left.k1, 1e-4f);
            Assert.AreEqual((float)d[1], left.k2, 1e-4f);
            Assert.AreEqual((float)d[2], left.k3, 1e-4f);
            Assert.AreEqual((float)d[3], left.k4, 1e-4f);
            Assert.AreEqual((float)d[4], left.k5, 1e-4f);
            Assert.AreEqual((float)d[5], left.k6, 1e-4f);
        }

        [Test]
        public void BuildEyes_ExtrinsicsApplied_WhenRequested()
        {
            var c = LoadReal();
            var (left, right) = RobotCalib.BuildEyeCalibrations(c, useExtrinsics: true);

            // ImuCamRig 换算后近单位阵但含残留安装角 → 非精确单位、左右不同
            Assert.That(Quaternion.Angle(left.extrinsicRotation, Quaternion.identity), Is.GreaterThan(0.05f),
                "外参应含非零安装角（ImuCamRig 已施加）");
            Assert.That(Quaternion.Angle(left.extrinsicRotation, right.extrinsicRotation), Is.GreaterThan(0.01f),
                "左右外参应不同");
        }

        [Test]
        public void BuildEyes_IdentityExtrinsics_WhenNotRequested()
        {
            var c = LoadReal();
            var (left, right) = RobotCalib.BuildEyeCalibrations(c, useExtrinsics: false);
            Assert.That(Quaternion.Angle(left.extrinsicRotation, Quaternion.identity), Is.LessThan(1e-3f));
            Assert.That(Quaternion.Angle(right.extrinsicRotation, Quaternion.identity), Is.LessThan(1e-3f));
        }
    }
}
