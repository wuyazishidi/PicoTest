// Assets/Main/Scripts/Rendering/InvertedSphereMesh.cs
using System.Collections.Generic;
using UnityEngine;

namespace PicoTest.Rendering
{
    /// <summary>运行时生成反法线穹顶（朝内看）。coverageDeg=总视场角，从 +Z 向外展开。</summary>
    public static class InvertedSphereMesh
    {
        public static Mesh Create(float coverageDeg, int segments)
        {
            segments = Mathf.Max(8, segments);
            float maxPolar = Mathf.Deg2Rad * coverageDeg * 0.5f; // +Z 起的最大极角
            int rings = segments;          // 极角分段
            int sectors = segments * 2;    // 方位分段

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            for (int r = 0; r <= rings; r++)
            {
                float polar = maxPolar * r / rings;          // 0..maxPolar
                for (int s = 0; s <= sectors; s++)
                {
                    float azim = Mathf.PI * 2f * s / sectors;
                    // +Z 为极轴
                    var dir = new Vector3(
                        Mathf.Sin(polar) * Mathf.Cos(azim),
                        Mathf.Sin(polar) * Mathf.Sin(azim),
                        Mathf.Cos(polar));
                    verts.Add(dir);          // 单位球，半径由 transform.scale 设
                    norms.Add(-dir);         // 反法线朝内
                    uvs.Add(new Vector2((float)s / sectors, (float)r / rings));
                }
            }

            int stride = sectors + 1;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < sectors; s++)
                {
                    int a = r * stride + s, b = a + 1, c = a + stride, d = c + 1;
                    // 反法线 → 反绕序（朝内可见，配合 Cull Front）
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }

            var mesh = new Mesh { name = $"InvertedDome_{coverageDeg}deg" };
            if (verts.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
