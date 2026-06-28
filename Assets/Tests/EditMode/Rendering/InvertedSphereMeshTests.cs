// Assets/Tests/EditMode/Rendering/InvertedSphereMeshTests.cs
using NUnit.Framework;
using UnityEngine;
using PicoTest.Rendering;

namespace PicoTest.Tests.EditMode.Rendering
{
    public class InvertedSphereMeshTests
    {
        [Test]
        public void Create_HasVertices_AndTriangles()
        {
            var m = InvertedSphereMesh.Create(coverageDeg: 220, segments: 32);
            Assert.Greater(m.vertexCount, 0);
            Assert.Greater(m.triangles.Length, 0);
            Assert.AreEqual(0, m.triangles.Length % 3);
        }

        [Test]
        public void NormalsPointInward()
        {
            var m = InvertedSphereMesh.Create(220, 24);
            var verts = m.vertices; var norms = m.normals;
            for (int i = 0; i < verts.Length; i += 7) // 抽样
            {
                // 法线应大致指向球心（与位置向量反向）：dot < 0
                Assert.Less(Vector3.Dot(norms[i], verts[i].normalized), 0f,
                    $"vertex {i} normal not inward");
            }
        }

        [Test]
        public void Coverage_CapsPolarAngle()
        {
            var m = InvertedSphereMesh.Create(coverageDeg: 220, segments: 32);
            // 220° 覆盖 = 从 +Z 起最大极角 110°；任何顶点与 +Z 夹角 ≤ 110°+eps
            float maxPolar = 0;
            foreach (var v in m.vertices)
                maxPolar = Mathf.Max(maxPolar, Vector3.Angle(Vector3.forward, v.normalized));
            Assert.LessOrEqual(maxPolar, 111f);
        }
    }
}
