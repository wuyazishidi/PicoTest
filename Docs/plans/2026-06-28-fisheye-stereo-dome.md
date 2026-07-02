# 双目鱼眼球面投影 Implementation Plan

> **状态：📝 待执行**（2026-06-28）。设计见 `Docs/designs/2026-06-28-fisheye-stereo-dome.md`（4 项关键决策已定）。

> **For agentic workers:** REQUIRED SUB-SKILL: 用 superpowers:subagent-driven-development 或 superpowers:executing-plans 逐任务实现。步骤用 `- [ ]` 复选框跟踪。

**Goal:** 把机器人双目鱼眼画面以**角度 1:1 反投影**投到左右眼各自的 220° 反法线穹顶，双目视差给深度，转头本地瞬时响应（WorldLocked 主体 + 可选低速云台伺服）。喂两张静态样图即可在 PC 端渲染冒烟验收。

**核心测试策略（本计划的灵魂）:** 把"角度 1:1"的鱼眼正投影数学**先用纯 C# 实现在 `Main.Core`（零 UnityEngine，EditMode 秒测）**，对解析/OpenCV golden 值断言 <1px；**shader HLSL 逐行照抄同一公式**。这样"角度对不对"是可回归的单测，不是肉眼玄学。Mesh 几何不变量、Renderer 参数推送、整链渲染各有其测试层级。

**Architecture:**
- `Main.Core/Rendering/FisheyeProjection`：纯 C# 鱼眼正投影（方向→UV + FOV 判定），shader 的可测镜像。
- `Main/Scripts/Rendering/`：`FisheyeCalibration`(ScriptableObject)、`InvertedSphereMesh`(Mesh 工具)、`FisheyeDomeRenderer`(MonoBehaviour)、`RobotHeadPoseDriver`(可选)。
- `Main/Shaders/FisheyeDome.shader`：URP unlit，立体单通道实例化，照抄 Core 公式，FOV 裁剪，`_FlipV/_Mirror`。

**Tech Stack:** Unity 2022.3.16f1 / URP 14.0.9 / XRI 2.6.4 / C#。Core 用 `System.Math`（禁 UnityEngine，故不用 Vector3/Mathf）。

**约定（所有任务遵守）：**
- 命名空间：Core 数学 `PicoTest.Core.Rendering`；MonoBehaviour `PicoTest.Rendering`；测试 `PicoTest.Tests.EditMode.Rendering` / `...PlayMode.Rendering`。
- 改完代码先编译：`powershell -ExecutionPolicy Bypass -Command "& '.\Packages\cn.etetet.yiuimcp\Config\compile-unity-flow.ps1' -Force 0 -NoWait 1"`（前置：Unity 编辑器开着；新文件出现需此 flow 触发 Refresh）。
- 跑测试：`powershell -ExecutionPolicy Bypass -File Tools\run-tests.ps1 -Mode EditMode|PlayMode`；提交前必须全绿（hook 强制 `.gates/tests-green`）。
- commit：conventional commits + `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`。
- 期望测试数用**增量**（+N）表述，不写绝对累计（避免与现有基线脱节）。
- 角度/方向约定（全程统一，违反即镜像/上下颠倒）：眼坐标系 +Z 为相机光轴前方、+X 右、+Y 上；像素 v 默认不翻转（决策 3 `_FlipV=0`）；外参 `R_eye = R(eye→robotHead)`，shader 中 `d_cam = R_eye * d`。

---

### Task 1: FisheyeProjection 纯 C# 数学（Main.Core）+ EditMode 单测

**Files:**
- Create: `Assets/Main/Core/Rendering/FisheyeProjection.cs`
- Test: `Assets/Tests/EditMode/Rendering/FisheyeProjectionTests.cs`

这是整个特性的地基：把设计 §3 的逐方向鱼眼正投影实现为纯函数，用**解析可算**的零畸变情形钉死方向/符号约定，再用一组畸变 golden 值锁多项式。

- [ ] **Step 1: 写失败测试**

```csharp
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

        private static double[] Identity3x3() => new double[] { 1,0,0, 0,1,0, 0,0,1 };

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
            double[] rotXpos90 = { 1,0,0,  0,0,-1,  0,1,0 };
            var p = new FisheyeProjection(500,500,800,800, 0,0,0,0, 1600,1600, 2.0, rotXpos90);
            p.ProjectDirection(0, 1, 0, out double u, out double v, out bool inFov);
            Assert.IsTrue(inFov);
            Assert.AreEqual(800.0, u, 1e-4);
            Assert.AreEqual(800.0, v, 1e-4);
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**（编译错误 `FisheyeProjection does not exist` = 失败形态）

- [ ] **Step 3: 最小实现**

```csharp
// Assets/Main/Core/Rendering/FisheyeProjection.cs
using System;

namespace PicoTest.Core.Rendering
{
    /// <summary>
    /// 鱼眼正投影（OpenCV fisheye 等距模型）：眼坐标系视线方向 → 像素(u,v) + FOV 判定。
    /// 纯 C#（Main.Core 禁 UnityEngine）；shader HLSL 必须逐行照抄此公式以保证角度 1:1。
    /// 约定：+Z 光轴前方、+X 右、+Y 上；R_eye = R(eye→robotHead) 行主序 3x3。
    /// </summary>
    public readonly struct FisheyeProjection
    {
        private readonly double _fx, _fy, _cx, _cy, _k1, _k2, _k3, _k4, _thetaMax;
        private readonly int _w, _h;
        private readonly double[] _r; // 9, 行主序

        public FisheyeProjection(double fx, double fy, double cx, double cy,
            double k1, double k2, double k3, double k4,
            int width, int height, double thetaMaxRad, double[] rEyeRowMajor)
        {
            _fx = fx; _fy = fy; _cx = cx; _cy = cy;
            _k1 = k1; _k2 = k2; _k3 = k3; _k4 = k4;
            _w = width; _h = height; _thetaMax = thetaMaxRad;
            _r = rEyeRowMajor ?? new double[] { 1,0,0, 0,1,0, 0,0,1 };
        }

        /// <summary>方向(dx,dy,dz) 不必归一化。返回是否落在图像内；inFov=是否在 thetaMax 内。</summary>
        public bool ProjectDirection(double dx, double dy, double dz,
            out double u, out double v, out bool inFov)
        {
            // 1) 外参旋转 d_cam = R_eye * d
            double cx = _r[0]*dx + _r[1]*dy + _r[2]*dz;
            double cy = _r[3]*dx + _r[4]*dy + _r[5]*dz;
            double cz = _r[6]*dx + _r[7]*dy + _r[8]*dz;

            // 2) 离轴角
            double rxy = Math.Sqrt(cx*cx + cy*cy);
            double theta = Math.Atan2(rxy, cz);
            inFov = theta <= _thetaMax;

            // 3) 等距畸变
            double t2 = theta*theta;
            double thetaD = theta * (1 + _k1*t2 + _k2*t2*t2 + _k3*t2*t2*t2 + _k4*t2*t2*t2*t2);

            // 4) 方位 → 像素（near-axis 守护）
            double cosPhi, sinPhi;
            if (rxy < 1e-12) { cosPhi = 0; sinPhi = 0; }
            else { cosPhi = cx / rxy; sinPhi = cy / rxy; }
            u = _fx * (thetaD * cosPhi) + _cx;
            v = _fy * (thetaD * sinPhi) + _cy;

            return u >= 0 && u <= _w && v >= 0 && v <= _h;
        }

        /// <summary>归一化 UV（v 翻转留给 shader 的 _FlipV 处理；此处不翻）。</summary>
        public bool ProjectToUV(double dx, double dy, double dz, out double uNorm, out double vNorm, out bool inFov)
        {
            bool inImg = ProjectDirection(dx, dy, dz, out double u, out double v, out inFov);
            uNorm = u / _w; vNorm = v / _h;
            return inImg;
        }
    }
}
```

- [ ] **Step 4: 编译通过 + 测试绿**（EditMode +6）
- [ ] **Step 5: Commit** `feat(core): fisheye forward projection math (shader-mirror, unit-tested)`

---

### Task 2: FisheyeCalibration（ScriptableObject）+ 示例资产 + 单测

**Files:**
- Create: `Assets/Main/Scripts/Rendering/FisheyeCalibration.cs`
- Create: `Assets/Main/Settings/Calibration/SampleLeft.asset` + `SampleRight.asset`（Step 3 用菜单/代码生成）
- Test: `Assets/Tests/EditMode/Rendering/CalibrationTests.cs`

一只眼一份资产：内参/畸变/尺寸/外参旋转(相对机器人头，决策 4)。提供 `ToProjection(thetaMax)` 转成 Core 结构 → 复用 Task 1 的全部正确性。

- [ ] **Step 1: 写失败测试**

```csharp
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
        public void ExtrinsicQuaternion_FlattensToRowMajor3x3_Consistently()
        {
            var cal = ScriptableObject.CreateInstance<FisheyeCalibration>();
            cal.fx = cal.fy = 500; cal.cx = cal.cy = 800; cal.width = cal.height = 1600;
            cal.extrinsicRotation = Quaternion.Euler(-90, 0, 0); // 世界+Y→相机+Z
            var proj = cal.ToProjection(2.0);
            proj.ProjectDirection(0, 1, 0, out double u, out double v, out bool inFov);
            Assert.IsTrue(inFov);
            Assert.AreEqual(800.0, u, 1e-3);
            Assert.AreEqual(800.0, v, 1e-3);
        }
    }
}
```

- [ ] **Step 2: 编译失败确认** → **Step 3: 实现**

```csharp
// Assets/Main/Scripts/Rendering/FisheyeCalibration.cs
using UnityEngine;
using PicoTest.Core.Rendering;

namespace PicoTest.Rendering
{
    /// <summary>一只眼的鱼眼标定（决策 4：外参为 R(eye→robotHead)）。由真实标定填充。</summary>
    [CreateAssetMenu(menuName = "PicoTest/Fisheye Calibration", fileName = "FisheyeCalibration")]
    public sealed class FisheyeCalibration : ScriptableObject
    {
        [Header("内参 (像素)")] public float fx, fy, cx, cy;
        [Header("等距畸变 k1..k4")] public float k1, k2, k3, k4;
        [Header("图像尺寸")] public int width = 1600, height = 1600;
        [Header("外参：相机→机器人头 旋转")] public Quaternion extrinsicRotation = Quaternion.identity;

        /// <summary>转 Core 纯数学结构。R_eye 行主序由四元数矩阵展开。</summary>
        public FisheyeProjection ToProjection(double thetaMaxRad)
        {
            var m = Matrix4x4.Rotate(extrinsicRotation);
            // 行主序：row0=(m00,m01,m02)...
            double[] r =
            {
                m.m00, m.m01, m.m02,
                m.m10, m.m11, m.m12,
                m.m20, m.m21, m.m22,
            };
            return new FisheyeProjection(fx, fy, cx, cy, k1, k2, k3, k4, width, height, thetaMaxRad, r);
        }

        /// <summary>shader 需要的 3x3（Matrix4x4，平移/缩放为单位）。</summary>
        public Matrix4x4 ExtrinsicMatrix() => Matrix4x4.Rotate(extrinsicRotation);
    }
}
```

示例资产由小工具生成（避免手搓 YAML）：在 `Assets/Editor/` 加一个菜单项 `PicoTest/Create Sample Calibrations` 用 `AssetDatabase.CreateAsset` 落两份占位（待真实标定覆盖）。

- [ ] **Step 4: 编译 + 测试绿**（EditMode +2） → **Step 5: Commit** `feat(render): fisheye calibration SO + sample assets`

---

### Task 3: InvertedSphereMesh（220° 反法线穹顶）+ EditMode 几何单测

**Files:**
- Create: `Assets/Main/Scripts/Rendering/InvertedSphereMesh.cs`
- Test: `Assets/Tests/EditMode/Rendering/InvertedSphereMeshTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
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
```

- [ ] **Step 2: 编译失败确认** → **Step 3: 实现**

```csharp
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
```

- [ ] **Step 4: 编译 + 测试绿**（EditMode +3） → **Step 5: Commit** `feat(render): inverted dome mesh generator (220deg, inward normals)`

---

### Task 4: FisheyeDome.shader（URP unlit，立体实例化，照抄 §3 数学）

**Files:**
- Create: `Assets/Main/Shaders/FisheyeDome.shader`
- Create: `Assets/Main/Shaders/FisheyeDome.mat`（Step 3 生成，指向该 shader）

shader 无单元测试 → 正确性由两道门保证：①HLSL 公式逐行对照 Task 1 的 `FisheyeProjection`（同一套 atan2/多项式/符号）；②Task 6 整链渲染冒烟回读像素断言。本任务验收=**编译零错误 + 在测试场景肉眼出图**。

- [ ] **Step 1: 写 shader**（关键片元逻辑，须与 `FisheyeProjection.ProjectDirection` 字字对应）

```hlsl
// Assets/Main/Shaders/FisheyeDome.shader  （骨架，URP unlit + 立体实例化）
Shader "PicoTest/FisheyeDome"
{
    Properties
    {
        _LeftTex ("Left Eye", 2D) = "black" {}
        _RightTex ("Right Eye", 2D) = "black" {}
        _ThetaMax ("Theta Max (rad)", Float) = 1.91986
        _FlipV ("Flip V", Float) = 0
        _Mirror ("Mirror U", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Background" "Queue"="Background" "RenderPipeline"="UniversalPipeline" }
        Cull Front      // 看球内壁
        ZWrite Off
        ZTest LEqual

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ STEREO_INSTANCING_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 左右各一套：内参(fx,fy,cx,cy)、畸变(k1..k4)、外参 3x3、图像尺寸
            float4 _LeftIntrin, _RightIntrin;   // xy=f, zw=c
            float4 _LeftDist, _RightDist;       // k1..k4
            float4x4 _LeftRot, _RightRot;       // 3x3 置于左上
            float4 _ImgSize;                    // xy = (w,h)
            float _ThetaMax, _FlipV, _Mirror;
            TEXTURE2D(_LeftTex); SAMPLER(sampler_LeftTex);
            TEXTURE2D(_RightTex); SAMPLER(sampler_RightTex);

            struct Attributes { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings {
                float4 positionCS:SV_POSITION; float3 dirOS:TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dirOS = normalize(v.positionOS.xyz);  // 视线方向 = 顶点方向（穹顶居中眼点）
                return o;
            }

            // === 与 FisheyeProjection.ProjectDirection 逐行一致 ===
            float2 ProjectUV(float3 d, float4 intrin, float4 k, float4x4 R, out bool inFov)
            {
                float3 c = mul((float3x3)R, d);
                float rxy = length(c.xy);
                float theta = atan2(rxy, c.z);
                inFov = theta <= _ThetaMax;
                float t2 = theta*theta;
                float thetaD = theta * (1 + k.x*t2 + k.y*t2*t2 + k.z*t2*t2*t2 + k.w*t2*t2*t2*t2);
                float2 phi = (rxy < 1e-6) ? float2(0,0) : c.xy / rxy;
                float u = intrin.x * (thetaD*phi.x) + intrin.z;
                float v = intrin.y * (thetaD*phi.y) + intrin.w;
                float2 uv = float2(u/_ImgSize.x, v/_ImgSize.y);
                if (_Mirror > 0.5) uv.x = 1 - uv.x;
                if (_FlipV > 0.5)  uv.y = 1 - uv.y;
                return uv;
            }

            half4 frag(Varyings i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                bool isRight = unity_StereoEyeIndex == 1;
                bool inFov;
                float2 uv = isRight
                    ? ProjectUV(i.dirOS, _RightIntrin, _RightDist, _RightRot, inFov)
                    : ProjectUV(i.dirOS, _LeftIntrin,  _LeftDist,  _LeftRot,  inFov);
                if (!inFov) return half4(0,0,0,1);            // FOV 裁剪
                return isRight
                    ? SAMPLE_TEXTURE2D(_RightTex, sampler_RightTex, uv)
                    : SAMPLE_TEXTURE2D(_LeftTex,  sampler_LeftTex,  uv);
            }
            ENDHLSL
        }
    }
}
```

- [ ] **Step 2: 编译 flow** → GetConsoleLog 确认 shader 无报错。
- [ ] **Step 3:** 创建 `FisheyeDome.mat` 指向该 shader（可代码或编辑器；提交 .mat + .meta）。
- [ ] **Step 4: 验收**：本任务无自动断言；编译干净即过，渲染正确性留 Task 6 回读断言。
- [ ] **Step 5: Commit** `feat(render): URP stereo fisheye dome shader (mirrors core math)`

---

### Task 5: FisheyeDomeRenderer（MonoBehaviour，参数推送 + RenderFrame）+ PlayMode 单测

**Files:**
- Create: `Assets/Main/Scripts/Rendering/FisheyeDomeRenderer.cs`
- Test: `Assets/Tests/PlayMode/Rendering/FisheyeDomeRendererTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// Assets/Tests/PlayMode/Rendering/FisheyeDomeRendererTests.cs
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PicoTest.Rendering;

namespace PicoTest.Tests.PlayMode.Rendering
{
    public class FisheyeDomeRendererTests
    {
        private static FisheyeCalibration Cal()
        {
            var c = ScriptableObject.CreateInstance<FisheyeCalibration>();
            c.fx = c.fy = 500; c.cx = c.cy = 800; c.width = c.height = 1600;
            return c;
        }

        [UnityTest]
        public IEnumerator WorldLocked_ParentsDomeToAnchor_NotCamera()
        {
            var go = new GameObject("renderer");
            var anchor = new GameObject("RobotHeadAnchor").transform;
            var r = go.AddComponent<FisheyeDomeRenderer>();
            r.frame = FisheyeDomeRenderer.RenderFrame.WorldLocked;
            r.robotHeadAnchor = anchor;
            r.leftCalibration = Cal(); r.rightCalibration = Cal();
            r.Initialize();
            yield return null;
            Assert.AreEqual(anchor, r.DomeTransform.parent);
            Object.Destroy(go); Object.Destroy(anchor.gameObject);
        }

        [UnityTest]
        public IEnumerator PushesIntrinsicsToMaterialBlock()
        {
            var go = new GameObject("renderer");
            var r = go.AddComponent<FisheyeDomeRenderer>();
            r.leftCalibration = Cal(); r.rightCalibration = Cal();
            r.Initialize();
            r.PushParameters();
            yield return null;
            var mpb = new MaterialPropertyBlock();
            r.DomeRenderer.GetPropertyBlock(mpb);
            var intrin = mpb.GetVector("_LeftIntrin");
            Assert.AreEqual(500f, intrin.x, 1e-3);
            Assert.AreEqual(800f, intrin.z, 1e-3);
            Object.Destroy(go);
        }
    }
}
```

- [ ] **Step 2: 编译失败确认** → **Step 3: 实现**（要点，非全文）

```csharp
// Assets/Main/Scripts/Rendering/FisheyeDomeRenderer.cs  （骨架）
using UnityEngine;

namespace PicoTest.Rendering
{
    [RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
    public sealed class FisheyeDomeRenderer : MonoBehaviour
    {
        public enum RenderFrame { WorldLocked, HeadLocked } // 决策 1：默认 WorldLocked
        public RenderFrame frame = RenderFrame.WorldLocked;

        [Header("标定（左右各一）")] public FisheyeCalibration leftCalibration, rightCalibration;
        [Header("纹理")] public Texture leftTex, rightTex;
        [Header("WorldLocked 锚点")] public Transform robotHeadAnchor;
        [Header("穹顶")] public float coverageDeg = 220f; public int segments = 48; public float radius = 20f;
        [Range(0,1)] public float flipV = 0, mirror = 0;
        public Shader domeShader; // 指 PicoTest/FisheyeDome

        public Transform DomeTransform { get; private set; }
        public MeshRenderer DomeRenderer { get; private set; }
        private MaterialPropertyBlock _mpb;

        public void Initialize()
        {
            var dome = new GameObject("FisheyeDome");
            DomeTransform = dome.transform;
            DomeTransform.SetParent(frame == RenderFrame.WorldLocked && robotHeadAnchor != null
                ? robotHeadAnchor : transform, false);
            DomeTransform.localScale = Vector3.one * radius;

            dome.AddComponent<MeshFilter>().sharedMesh = InvertedSphereMesh.Create(coverageDeg, segments);
            DomeRenderer = dome.AddComponent<MeshRenderer>();
            DomeRenderer.sharedMaterial = new Material(domeShader != null ? domeShader : Shader.Find("PicoTest/FisheyeDome"));
            _mpb = new MaterialPropertyBlock();
        }

        public void PushParameters()
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            DomeRenderer.GetPropertyBlock(_mpb);
            double thetaMax = Mathf.Deg2Rad * coverageDeg * 0.5;
            SetEye(_mpb, "_Left",  leftCalibration,  leftTex,  "_LeftTex");
            SetEye(_mpb, "_Right", rightCalibration, rightTex, "_RightTex");
            _mpb.SetVector("_ImgSize", new Vector4(leftCalibration.width, leftCalibration.height, 0, 0));
            _mpb.SetFloat("_ThetaMax", (float)thetaMax);
            _mpb.SetFloat("_FlipV", flipV); _mpb.SetFloat("_Mirror", mirror);
            DomeRenderer.SetPropertyBlock(_mpb);
        }

        private static void SetEye(MaterialPropertyBlock mpb, string prefix, FisheyeCalibration c, Texture tex, string texProp)
        {
            mpb.SetVector(prefix + "Intrin", new Vector4(c.fx, c.fy, c.cx, c.cy));
            mpb.SetVector(prefix + "Dist", new Vector4(c.k1, c.k2, c.k3, c.k4));
            mpb.SetMatrix(prefix + "Rot", c.ExtrinsicMatrix());
            if (tex != null) mpb.SetTexture(texProp, tex);
        }

        // 首版 WorldLocked：dome 不随头转；HeadLocked / 低速云台伺服见 Task 7。
    }
}
```

- [ ] **Step 4: 编译 + 测试绿**（PlayMode +2） → **Step 5: Commit** `feat(render): fisheye dome renderer (param push + render frame)`

---

### Task 6: 整链渲染冒烟（两张静态样图 → 回读像素断言）

**Files:**
- Create: `Assets/Tests/PlayMode/Rendering/FisheyeDomeRenderSmokeTests.cs`
- （测试内程序化造图，不落资产）

这是把 shader 正确性变成**可回归断言**的关键：造两张可分辨的合成鱼眼图（如左图中心红、右图中心绿，边角黑），用一台相机渲染穹顶到 RenderTexture，回读像素断言：①视野中心非黑且取到对应眼颜色；②thetaMax 外的方向为黑（FOV 裁剪）。

- [ ] **Step 1: 写失败测试**

```csharp
// Assets/Tests/PlayMode/Rendering/FisheyeDomeRenderSmokeTests.cs
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PicoTest.Rendering;

namespace PicoTest.Tests.PlayMode.Rendering
{
    public class FisheyeDomeRenderSmokeTests
    {
        private static Texture2D SolidCenterTex(Color center)
        {
            var t = new Texture2D(64, 64, TextureFormat.RGB24, false);
            var px = new Color[64 * 64];
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float r = Vector2.Distance(new Vector2(x, y), new Vector2(32, 32)) / 32f;
                    px[y * 64 + x] = r < 0.5f ? center : Color.black;
                }
            t.SetPixels(px); t.Apply(); return t;
        }

        private static FisheyeCalibration Cal()
        {
            var c = ScriptableObject.CreateInstance<FisheyeCalibration>();
            c.fx = c.fy = 20f / (110f * Mathf.Deg2Rad); // 64px 半宽~ small; 仅需中心采到
            c.cx = c.cy = 32; c.width = c.height = 64;
            return c;
        }

        [UnityTest]
        public IEnumerator CenterPixel_IsNonBlack_AndForwardLooksLeftEyeColor()
        {
            var rig = new GameObject("rig");
            var cam = rig.AddComponent<Camera>();
            cam.transform.position = Vector3.zero;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            var rt = new RenderTexture(128, 128, 16); cam.targetTexture = rt;

            var ro = new GameObject("renderer");
            var r = ro.AddComponent<FisheyeDomeRenderer>();
            r.leftCalibration = Cal(); r.rightCalibration = Cal();
            r.leftTex = SolidCenterTex(Color.red);
            r.rightTex = SolidCenterTex(Color.green);
            r.coverageDeg = 220; r.radius = 10;
            r.Initialize(); r.PushParameters();
            yield return null;

            cam.Render();
            RenderTexture.active = rt;
            var read = new Texture2D(128, 128, TextureFormat.RGB24, false);
            read.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); read.Apply();
            RenderTexture.active = null;

            var center = read.GetPixel(64, 64);
            Assert.Greater(center.r + center.g + center.b, 0.1f, "穹顶中心不应为黑（采样失败）");
            // 非立体单眼渲染默认走左眼参数 → 红占优
            Assert.Greater(center.r, center.g, "正前方应取到左眼(红)");

            Object.Destroy(rig); Object.Destroy(ro); rt.Release();
        }
    }
}
```

> 注：非立体（编辑器单视图）渲染时 `unity_StereoEyeIndex==0` 走左眼，断言据此。真机立体的左右分眼留真机级 PlayMode/截图验收。

- [ ] **Step 2: 编译失败/红** → **Step 3:** 修通（多为 shader 属性名/采样/裁剪边界对齐 Task 4；若中心黑，查 dirOS 归一化、ImgSize、thetaMax 单位）。
- [ ] **Step 4: 测试绿**（PlayMode +1） → **Step 5: Commit** `test(render): end-to-end dome render smoke (pixel readback)`

---

### Task 7（可选，可延后）: RobotHeadPoseDriver 低速云台伺服

**Files:**
- Create: `Assets/Main/Core/Rendering/GazeServo.cs`（纯数学）+ `Assets/Main/Scripts/Rendering/RobotHeadPoseDriver.cs`
- Test: `Assets/Tests/EditMode/Rendering/GazeServoTests.cs`

把"低速率跟到平均注视方向、带死区、限速"做成纯函数 `GazeServo.Step(current, targetGaze, dt, rateDegPerSec, deadzoneDeg)`，EditMode 测：①死区内不动；②超死区按限速逼近；③多步收敛不过冲。`RobotHeadPoseDriver` 仅把它接到 `robotHeadAnchor` 与遥测/IMU。首版可跳过（纯 WorldLocked 已满足沉浸+同步主目标）。

- [ ] 失败测试 → 实现 → 绿（EditMode +3） → **Commit** `feat(render): low-rate gaze servo for gimbal anchor (optional)`

---

## 验收对账（回填设计 §1 验收标准）
| 目标 | 本计划覆盖 | 验收方式 |
|---|---|---|
| 1:1 还原 | Task 1 数学 + Task 4 shader 照抄 | EditMode <1px 断言 + Task 6 回读 |
| 3D 效果 | Task 4 立体分眼 + Task 5 左右参数 | Task 6 单眼色判 + 真机分眼（真机级） |
| 沉浸感 | Task 3 220° 穹顶 + Task 5 WorldLocked | Task 6 出图 + 真机环视 |
| 同步 | Task 5 WorldLocked 本地转头 + Task 7 慢伺服 | 真机级延迟实测（传输层另案） |

## 非目标（YAGNI / 留后续）
传输/解码（RTSP/WebRTC/UVC）、真机分眼自动断言、预测性 reprojection、6DoF 平移视差/体积重建、真实标定采集流程。

## 前置依赖（不阻塞编码，阻塞"真 1:1/真 3D"验收）
- 机器人双相机**基线 mm**（→ 3D 是否 1:1）。
- **鱼眼标定来源**（OpenCV `cv::fisheye::calibrate` / 厂商）→ 覆盖 Task 2 占位资产。
