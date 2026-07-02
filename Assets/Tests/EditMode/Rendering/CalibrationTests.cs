// Assets/Tests/EditMode/Rendering/CalibrationTests.cs
using NUnit.Framework;
using UnityEngine;
using PicoTest.Rendering;

namespace PicoTest.Tests.EditMode.Rendering
{
    public class CalibrationTests
    {
        [Test]
        public void ToProjection_RoundTripsFields_AndMatchesCoreMath()
        {
            var cal = ScriptableObject.CreateInstance<FisheyeCalibration>();
            cal.fx = 500; cal.fy = 500; cal.cx = 800; cal.cy = 800;
            cal.k1 = 0; cal.k2 = 0; cal.k3 = 0; cal.k4 = 0;
            cal.width = 1600; cal.height = 1600;
            cal.extrinsicRotation = Quaternion.identity;

            var proj = cal.ToProjection(thetaMaxRad: 2.0);
            proj.ProjectDirection(0, 0, 1, out double u, out double v, out bool inFov);
            Assert.IsTrue(inFov);
            Assert.AreEqual(800.0, u, 1e-4);
            Assert.AreEqual(800.0, v, 1e-4);
        }

        [Test]
        public void ExtrinsicQuaternion_FlattensToRowMajor3x3_ConsistentWithCoreMath()
        {
            // 用 FromToRotation 构造把世界 +Y 映到相机 +Z 的外参（规避 Euler 约定歧义）
            var cal = ScriptableObject.CreateInstance<FisheyeCalibration>();
            cal.fx = cal.fy = 500; cal.cx = cal.cy = 800; cal.width = cal.height = 1600;
            cal.extrinsicRotation = Quaternion.FromToRotation(Vector3.up, Vector3.forward);

            var proj = cal.ToProjection(2.0);
            proj.ProjectDirection(0, 1, 0, out double u, out double v, out bool inFov);
            Assert.IsTrue(inFov);
            Assert.AreEqual(800.0, u, 1e-3);
            Assert.AreEqual(800.0, v, 1e-3);
        }
    }
}
