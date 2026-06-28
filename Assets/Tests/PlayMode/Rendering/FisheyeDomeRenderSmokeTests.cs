// Assets/Tests/PlayMode/Rendering/FisheyeDomeRenderSmokeTests.cs
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using PicoTest.Rendering;

namespace PicoTest.Tests.PlayMode.Rendering
{
    /// <summary>
    /// 整链渲染冒烟：把 shader 正确性变成可回归断言。两张可分辨样图喂入，
    /// 用 URP StandardRequest 同步渲染到 RT，回读像素断言中心非黑且取到对应眼颜色。
    /// </summary>
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
            c.fx = c.fy = 32f / (110f * Mathf.Deg2Rad); // 仅需中心采到，正前方 theta=0 → uv 中心
            c.cx = c.cy = 32; c.width = c.height = 64;
            return c;
        }

        [UnityTest]
        public IEnumerator CenterPixel_IsNonBlack_AndForwardLooksLeftEyeColor()
        {
            var rig = new GameObject("rig");
            var cam = rig.AddComponent<Camera>();
            cam.transform.position = Vector3.zero;
            cam.transform.rotation = Quaternion.identity; // 看 +Z
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 100f;
            var rt = new RenderTexture(128, 128, 16) { antiAliasing = 1 };

            var ro = new GameObject("renderer");
            var r = ro.AddComponent<FisheyeDomeRenderer>();
            r.leftCalibration = Cal(); r.rightCalibration = Cal();
            r.leftTex = SolidCenterTex(Color.red);
            r.rightTex = SolidCenterTex(Color.green);
            r.coverageDeg = 220; r.radius = 10; r.segments = 32;
            r.Initialize(); r.PushParameters();
            yield return null;

            // URP 正确的同步渲染路径（Camera.Render() 在 SRP 下不支持）
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
            Assert.Greater(center.r + center.g + center.b, 0.1f, "穹顶中心不应为黑（采样失败）");
            // 非立体（编辑器单视图）渲染 unity_StereoEyeIndex==0 → 走左眼(红)
            Assert.Greater(center.r, center.g, "正前方应取到左眼(红)");

            Object.Destroy(rig); Object.Destroy(ro);
            Object.Destroy(read); rt.Release();
        }

        // SBS 整图：左半中心红、右半中心绿（128x64）
        private static Texture2D SbsTex()
        {
            var t = new Texture2D(128, 64, TextureFormat.RGB24, false);
            var px = new Color[128 * 64];
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 128; x++)
                {
                    bool left = x < 64;
                    var c = new Vector2(left ? 32 : 96, 32);
                    float rr = Vector2.Distance(new Vector2(x, y), c) / 32f;
                    px[y * 128 + x] = rr < 0.5f ? (left ? Color.red : Color.green) : Color.black;
                }
            t.SetPixels(px); t.Apply(); return t;
        }

        [UnityTest]
        public IEnumerator SbsSplit_LeftEye_SamplesLeftHalf()
        {
            var rig = new GameObject("rig");
            var cam = rig.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            var rt = new RenderTexture(128, 128, 16);

            var sbs = SbsTex();
            var ro = new GameObject("renderer");
            var r = ro.AddComponent<FisheyeDomeRenderer>();
            r.leftCalibration = Cal(); r.rightCalibration = Cal(); // 每眼 64x64
            r.leftTex = sbs; r.rightTex = sbs;
            r.leftUVRect = new Vector4(0f, 0f, 0.5f, 1f);   // 左眼采左半
            r.rightUVRect = new Vector4(0.5f, 0f, 0.5f, 1f);
            r.coverageDeg = 220; r.radius = 10; r.segments = 32;
            r.Initialize(); r.PushParameters();
            yield return null;

            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (!RenderPipeline.SupportsRenderRequest(cam, req))
            {
                Object.Destroy(rig); Object.Destroy(ro); rt.Release();
                Assert.Ignore("StandardRequest 不被当前管线支持");
                yield break;
            }
            cam.SubmitRenderRequest(req);
            yield return new WaitForEndOfFrame();

            RenderTexture.active = rt;
            var read = new Texture2D(128, 128, TextureFormat.RGB24, false);
            read.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); read.Apply();
            RenderTexture.active = null;

            var center = read.GetPixel(64, 64);
            // 正前方左眼经 leftUVRect 应落在 SBS 左半中心 → 红，而非右半绿
            Assert.Greater(center.r, center.g, "SBS 左眼应采左半(红)，UV 子区分半错误会取到绿");

            Object.Destroy(rig); Object.Destroy(ro); Object.Destroy(read); rt.Release();
        }
    }
}
