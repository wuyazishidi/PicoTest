// Assets/Experiments/Exp-RobotDsDome/Tests/PlayMode/DsDomeRenderSmokeTests.cs
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using PicoTest.Experiments.RobotDsDome;

namespace PicoTest.Experiments.RobotDsDome.Tests
{
    /// <summary>
    /// DS 穹顶整链渲染冒烟：RobotDsDome.shader 编译 + 前向采样正确。SBS 左半红/右半绿喂入，
    /// URP StandardRequest 同步渲染回读，中心非黑且左眼采左半（红）。捕捉 shader 编译/采样回归。
    /// </summary>
    public class DsDomeRenderSmokeTests
    {
        private static DsEyeCalibration Cal()
        {
            // 前向(0,0,1) → (cx,cy)=(32,32) → 归一化中心；fx 对中心光线无影响
            var c = ScriptableObject.CreateInstance<DsEyeCalibration>();
            c.xi = 0f; c.alpha = 0.5f; c.fx = c.fy = 32f; c.cx = c.cy = 32f;
            c.width = c.height = 64;
            return c;
        }

        private static Texture2D SbsTex()
        {
            var t = new Texture2D(128, 64, TextureFormat.RGB24, false);
            var px = new Color[128 * 64];
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 128; x++)
                {
                    bool left = x < 64;
                    var ctr = new Vector2(left ? 32 : 96, 32);
                    float rr = Vector2.Distance(new Vector2(x, y), ctr) / 32f;
                    px[y * 128 + x] = rr < 0.5f ? (left ? Color.red : Color.green) : Color.black;
                }
            t.SetPixels(px); t.Apply(); return t;
        }

        [UnityTest]
        public IEnumerator ForwardCenter_NonBlack_LeftEyeSamplesLeftHalf()
        {
            var rig = new GameObject("rig");
            var cam = rig.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // 看 +Z
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 100f;
            var rt = new RenderTexture(128, 128, 16);

            var sbs = SbsTex();
            var ro = new GameObject("renderer");
            var r = ro.AddComponent<DsDomeRenderer>();
            r.leftCalibration = Cal(); r.rightCalibration = Cal();
            r.leftTex = sbs; r.rightTex = sbs;
            r.leftUVRect = new Vector4(0f, 0f, 0.5f, 1f);
            r.rightUVRect = new Vector4(0.5f, 0f, 0.5f, 1f);
            r.flipV = 0f; r.mirror = 0f;                 // 中心不受 flip 影响；显式 0 便于断言
            r.coverageDeg = 200; r.radius = 10; r.segments = 32;
            r.Initialize(); r.PushParameters();
            yield return null;

            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (!RenderPipeline.SupportsRenderRequest(cam, req))
            {
                Object.Destroy(rig); Object.Destroy(ro); rt.Release();
                Assert.Ignore("StandardRequest 不被当前管线支持，跳过 GPU 回读");
                yield break;
            }
            cam.SubmitRenderRequest(req);
            yield return new WaitForEndOfFrame();

            RenderTexture.active = rt;
            var read = new Texture2D(128, 128, TextureFormat.RGB24, false);
            read.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); read.Apply();
            RenderTexture.active = null;

            var center = read.GetPixel(64, 64);
            Assert.Greater(center.r + center.g + center.b, 0.1f, "DS 穹顶中心不应为黑（shader 采样失败）");
            Assert.Greater(center.r, center.g, "SBS 左眼应采左半(红)");

            Object.Destroy(rig); Object.Destroy(ro); Object.Destroy(read); rt.Release();
        }
    }
}
