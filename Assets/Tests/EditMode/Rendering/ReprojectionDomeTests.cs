// Assets/Tests/EditMode/Rendering/ReprojectionDomeTests.cs
using NUnit.Framework;
using UnityEngine;
using PicoTest.Rendering;

namespace PicoTest.Tests.EditMode.Rendering
{
    public class ReprojectionDomeTests
    {
        private static Vector3[] SampleDirs() => new[]
        {
            Vector3.forward, Vector3.right, Vector3.up,
            new Vector3(1, 1, 1).normalized, new Vector3(-0.3f, 0.2f, 0.9f).normalized,
        };

        // 常量深度：每个顶点位移到 dir×depth，模长==depth，方向不变。
        [Test]
        public void Displace_ConstantDepth_PlacesVertsAtDepthAlongDirection()
        {
            var dirs = SampleDirs();
            var outV = new Vector3[dirs.Length];
            var surface = new ConstantDepthSurface(7.5f);

            ReprojectionDomeRenderer.Displace(dirs, surface, outV);

            for (int i = 0; i < dirs.Length; i++)
            {
                Assert.AreEqual(7.5f, outV[i].magnitude, 1e-3f, $"顶点 {i} 深度应=7.5");
                // 方向保持
                Assert.Less(Vector3.Angle(dirs[i], outV[i]), 1e-2f, $"顶点 {i} 方向应不变");
            }
        }

        // 常量深度面：任意方向返回同一深度。
        [Test]
        public void ConstantDepthSurface_ReturnsFixedDepth()
        {
            var s = new ConstantDepthSurface(3.2f);
            Assert.AreEqual(3.2f, s.SampleDepth(Vector3.forward), 1e-6f);
            Assert.AreEqual(3.2f, s.SampleDepth(new Vector3(1, 2, 3).normalized), 1e-6f);
        }

        // SpatialMesh 深度面：无 head（Tick 未给）→ 回退远景深度（编辑器无网格安全退化）。
        [Test]
        public void SpatialMeshDepthSurface_NoHead_ReturnsFallback()
        {
            var s = new SpatialMeshDepthSurface { fallbackDepth = 18f };
            Assert.AreEqual(18f, s.SampleDepth(Vector3.forward), 1e-6f);
        }

        // Displace 对 null 深度面守护：默认 20m，不抛。
        [Test]
        public void Displace_NullSurface_UsesDefault()
        {
            var dirs = new[] { Vector3.forward };
            var outV = new Vector3[1];
            Assert.DoesNotThrow(() => ReprojectionDomeRenderer.Displace(dirs, null, outV));
            Assert.AreEqual(20f, outV[0].magnitude, 1e-3f);
        }
    }
}
