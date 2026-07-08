// Assets/Experiments/Exp-VstPassthrough/Tests/ImuCamRigTests.cs
using System;
using System.IO;
using NUnit.Framework;
using PicoTest.Core.Rendering;
using PicoTest.Experiments.VstPassthrough;
using UnityEngine;

namespace PicoTest.Experiments.VstPassthrough.Tests
{
    /// <summary>
    /// ImuCamRig 外参自标定换算测试。真实数据 = Assets/StreamingAssets/cam_calib.json（纯数字，已入库）。
    /// 关键结论（数据推导，非字段名）：json 的 T_imu_to_cam 实为 **相机在 IMU 系的位姿（cam→imu）**——
    /// 按字段名"imu→cam"解读时基线方向(x̂_imu)与图像横轴(−ŷ_imu)垂直、物理矛盾；
    /// 按 cam→imu 解读则基线 ∥ 图像横轴、光轴朝前，完全自洽。ImuCamRig 对两种解读打分自动选择。
    /// </summary>
    public class ImuCamRigTests
    {
        private static CamCalib LoadReal()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "cam_calib.json");
            Assert.That(File.Exists(path), $"缺 {path}（应随库存在，纯数字标定）");
            return CamCalib.Parse(File.ReadAllText(path));
        }

        [Test]
        public void RealCalib_AutoDetectsCamToImuReading()
        {
            var rig = ImuCamRig.FromCalib(LoadReal());
            Assert.AreEqual("cam_to_imu", rig.TReading, "真实数据应判定为 cam→imu 解读（基线∥图像横轴）");
            Assert.Greater(rig.ConsistencyScore, 0.99, "选中解读的基线/图像横轴共线度");
        }

        [Test]
        public void RealCalib_RecoversBaseline()
        {
            var rig = ImuCamRig.FromCalib(LoadReal());
            Assert.AreEqual(0.064, rig.BaselineM, 0.001, "相机中心距应复原 stereo_baseline_m");
        }

        [Test]
        public void RealCalib_REye_OrthonormalProperRotation()
        {
            var rig = ImuCamRig.FromCalib(LoadReal());
            foreach (var r in new[] { rig.LeftREye, rig.RightREye })
            {
                AssertOrthonormal(r);
                Assert.AreEqual(1.0, Det3(r), 1e-9, "det=+1（真旋转，可转四元数）");
            }
        }

        [Test]
        public void RealCalib_REye_NearIdentity_YUpConvention()
        {
            // 现管线（identity 外参 + flipV=1 实测画面正立）的有效相机系约定为 y 朝上；
            // 换算后 R_eye 应接近单位阵（仅残留几度安装角），三个基方向各自映射到自身附近。
            var rig = ImuCamRig.FromCalib(LoadReal());
            foreach (var r in new[] { rig.LeftREye, rig.RightREye })
            {
                Assert.Greater(Apply(r, 0, 0, 1)[2], 0.95, "头前向 → 相机 z（光轴）");
                Assert.Greater(Apply(r, 0, 1, 0)[1], 0.95, "头上方 → 相机 y（有效 y-up 约定）");
                Assert.Greater(Apply(r, 1, 0, 0)[0], 0.95, "头右方 → 相机 x（图像右）");
            }
        }

        [Test]
        public void RealCalib_CamPositionsInHeadFrame()
        {
            var rig = ImuCamRig.FromCalib(LoadReal());
            Assert.AreEqual(-0.032, rig.LeftCamPosHead[0], 0.004, "左相机在头系 x≈-半基线");
            Assert.AreEqual(+0.032, rig.RightCamPosHead[0], 0.004, "右相机在头系 x≈+半基线");
            foreach (var p in new[] { rig.LeftCamPosHead, rig.RightCamPosHead })
            {
                Assert.Less(Math.Abs(p[1]), 0.03, "头系 y 偏移应为 cm 级");
                Assert.Less(Math.Abs(p[2]), 0.03, "头系 z 偏移应为 cm 级");
            }
        }

        [Test]
        public void SyntheticImuToCamReading_AutoDetectedAndConverted()
        {
            // 构造真正按字段名 imu→cam 存的标定（IMU 轴排布模仿真机：x̂=上, ŷ=用户左, ẑ=后；
            // 相机各绕竖轴 5° toe-in）。验证：自动判读选 imu_to_cam；换算链把头前向映射到偏 toe 角处。
            const double toe = 5.0 * Math.PI / 180.0;
            var calib = new CamCalib
            {
                ResolutionPerEyeWh = new[] { 1280, 960 },
                StereoBaselineM = 0.064,
                Left = SyntheticEye(+0.032, +toe),   // 左相机在用户左 = +ŷ 侧
                Right = SyntheticEye(-0.032, -toe),
            };
            var rig = ImuCamRig.FromCalib(calib);

            Assert.AreEqual("imu_to_cam", rig.TReading);
            Assert.Greater(rig.ConsistencyScore, 0.99);
            Assert.AreEqual(0.064, rig.BaselineM, 1e-9);

            var fL = Apply(rig.LeftREye, 0, 0, 1);
            var fR = Apply(rig.RightREye, 0, 0, 1);
            Assert.AreEqual(Math.Cos(toe), fL[2], 1e-6, "头前向到左相机光轴差 toe 角");
            Assert.AreEqual(Math.Cos(toe), fR[2], 1e-6, "头前向到右相机光轴差 toe 角");
            Assert.Less(fL[0] * fR[0], 0, "左右 toe-in 方向相反");
            Assert.Greater(Apply(rig.LeftREye, 0, 1, 0)[1], 0.99, "头上方 → 相机 y（y-up 约定）");
        }

        /// <summary>
        /// 合成单眼：IMU 系 (x̂=上, ŷ=用户左, ẑ=后)（右手系，模仿真机排布）；相机绕上轴(x̂) yaw=toe 内旋。
        /// 相机轴（imu 坐标）：图像右=−ŷ、图像下=−x̂、光轴=−ẑ，再绕 x̂ 旋 toe。
        /// T 按真 imu→cam 约定存：R 行 = 相机轴，t = −R·center。
        /// </summary>
        private static EyeCalib SyntheticEye(double centerY, double toe)
        {
            double c = Math.Cos(toe), s = Math.Sin(toe);
            double[] ax = { 0, -c, -s };   // 图像右：−ŷ 绕 x̂ 旋 toe
            double[] ay = { -1, 0, 0 };    // 图像下：−x̂（不受绕 x̂ 旋转影响）
            double[] az = { 0, s, -c };    // 光轴：−ẑ 绕 x̂ 旋 toe
            double[] center = { 0, centerY, 0 };
            double[] t =
            {
                -(ax[0] * center[0] + ax[1] * center[1] + ax[2] * center[2]),
                -(ay[0] * center[0] + ay[1] * center[1] + ay[2] * center[2]),
                -(az[0] * center[0] + az[1] * center[1] + az[2] * center[2]),
            };
            return new EyeCalib
            {
                TImuToCam = new[]
                {
                    new[] { ax[0], ax[1], ax[2], t[0] },
                    new[] { ay[0], ay[1], ay[2], t[1] },
                    new[] { az[0], az[1], az[2], t[2] },
                    new[] { 0.0, 0.0, 0.0, 1.0 },
                },
            };
        }

        private static double[] Apply(double[] r, double x, double y, double z) => new[]
        {
            r[0] * x + r[1] * y + r[2] * z,
            r[3] * x + r[4] * y + r[5] * z,
            r[6] * x + r[7] * y + r[8] * z,
        };

        private static double Det3(double[] r) =>
            r[0] * (r[4] * r[8] - r[5] * r[7])
            - r[1] * (r[3] * r[8] - r[5] * r[6])
            + r[2] * (r[3] * r[7] - r[4] * r[6]);

        private static void AssertOrthonormal(double[] r)
        {
            Assert.AreEqual(9, r.Length);
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    double dot = r[i * 3] * r[j * 3] + r[i * 3 + 1] * r[j * 3 + 1] + r[i * 3 + 2] * r[j * 3 + 2];
                    Assert.AreEqual(i == j ? 1.0 : 0.0, dot, 1e-9, $"行 {i}·行 {j}");
                }
        }
    }
}
