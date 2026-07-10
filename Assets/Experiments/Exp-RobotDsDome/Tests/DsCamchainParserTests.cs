// Assets/Experiments/Exp-RobotDsDome/Tests/DsCamchainParserTests.cs
using System.IO;
using NUnit.Framework;
using PicoTest.Experiments.RobotDsDome;
using UnityEngine;

namespace PicoTest.Experiments.RobotDsDome.Tests
{
    /// <summary>
    /// DsCamchainParser 解析真实 3-camchain.yaml（随库存在，纯数字）→ cam0/cam1 DS 内参 + 基线。
    /// </summary>
    public class DsCamchainParserTests
    {
        private static DsCamchain Load()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "3-camchain.yaml");
            Assert.That(File.Exists(path), $"缺 {path}");
            return DsCamchainParser.Parse(File.ReadAllText(path));
        }

        [Test]
        public void Cam0_DsIntrinsics()
        {
            var c = Load().cam0;
            Assert.AreEqual("ds", c.model);
            Assert.AreEqual(-0.0013188131, c.xi, 1e-9);
            Assert.AreEqual(0.5698437483, c.alpha, 1e-9);
            Assert.AreEqual(509.53190418, c.fx, 1e-6);
            Assert.AreEqual(509.08963735, c.fy, 1e-6);
            Assert.AreEqual(962.14990949, c.cx, 1e-6);
            Assert.AreEqual(551.43953780, c.cy, 1e-6);
            Assert.AreEqual(1920, c.width);
            Assert.AreEqual(1080, c.height);
        }

        [Test]
        public void Cam1_DsIntrinsics()
        {
            var c = Load().cam1;
            Assert.AreEqual("ds", c.model);
            Assert.AreEqual(-0.0019381869, c.xi, 1e-9);
            Assert.AreEqual(0.5697481375, c.alpha, 1e-9);
            Assert.AreEqual(510.02628602, c.fx, 1e-6);
            Assert.AreEqual(961.92859227, c.cx, 1e-6);
            Assert.AreEqual(520.95942832, c.cy, 1e-6);
            Assert.AreEqual(1920, c.width);
        }

        [Test]
        public void Baseline_FromCam1Translation()
        {
            // ‖T_cn_cnm1 平移‖ = √(0.05570²+0.00025²+0.00020²) ≈ 55.7mm
            Assert.AreEqual(0.055704, Load().baselineM, 1e-4);
        }
    }
}
