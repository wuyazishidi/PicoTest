// Assets/Tests/PlayMode/Rendering/RealFisheyeFrameOnDomeTests.cs
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using PicoTest.Rendering;

namespace PicoTest.Tests.PlayMode.Rendering
{
    /// <summary>
    /// 用真实 PICO 采集帧 (StreamingAssets/sbs_frame.png, 2560x960 SBS raw 鱼眼) + 真实标定
    /// (RealLeft, k1-k6) 渲染穹顶，把单眼透视视图存为 Artifacts/dome_real.png 供肉眼审。
    /// 正确的去畸变 → 视图应近似直线为直的自然画面（非鱼眼）。
    /// </summary>
    public class RealFisheyeFrameOnDomeTests
    {
        [UnityTest]
        public IEnumerator RealFrame_OnDome_DumpsUndistortedView()
        {
            var framePath = Path.Combine(Application.streamingAssetsPath, "sbs_frame.png");
            if (!File.Exists(framePath))
            {
                Assert.Ignore($"测试帧不存在（真人采集数据不入库）: {framePath}。" +
                              "用 Tools/extract-fisheye-frame.ps1 从 camera.mp4 抽帧后本测试自动生效。");
            }

            // StreamingAssets 是原始文件，用 LoadImage 读入纹理（非 Unity 导入管线）
            var sbs = new Texture2D(2, 2, TextureFormat.RGB24, false);
            sbs.LoadImage(File.ReadAllBytes(framePath));
            sbs.wrapMode = TextureWrapMode.Clamp;

            var leftCal = LoadReal("RealLeft");
            var rightCal = LoadReal("RealRight");
            Assert.IsNotNull(leftCal, "RealLeft 标定缺失（先跑 PicoTest/Import Factory Calibration）");

            var rig = new GameObject("rig");
            var cam = rig.AddComponent<Camera>();
            cam.transform.position = Vector3.zero;
            cam.transform.rotation = Quaternion.identity;       // 看 +Z（相机光轴）
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            cam.fieldOfView = 70f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 100f;
            var rt = new RenderTexture(1024, 1024, 16);

            var ro = new GameObject("renderer");
            var r = ro.AddComponent<FisheyeDomeRenderer>();
            r.leftCalibration = leftCal; r.rightCalibration = rightCal;
            r.leftTex = sbs; r.rightTex = sbs;
            r.leftUVRect = new Vector4(0f, 0f, 0.5f, 1f);     // SBS 左半
            r.rightUVRect = new Vector4(0.5f, 0f, 0.5f, 1f);  // SBS 右半
            r.flipV = 0f;                                      // 决策3：sRGB 不翻转（LoadImage 行序与采样 v 一致）
            r.coverageDeg = 150f; r.radius = 10f; r.segments = 64;
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
            var read = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
            read.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0); read.Apply();
            RenderTexture.active = null;

            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Artifacts"));
            Directory.CreateDirectory(outDir);
            File.WriteAllBytes(Path.Combine(outDir, "dome_real.png"), read.EncodeToPNG());
            Debug.Log("[RealFrame] dumped Artifacts/dome_real.png");

            // 中心非黑（采到画面）
            var c = read.GetPixel(512, 512);
            Assert.Greater(c.r + c.g + c.b, 0.05f, "穹顶中心为黑——采样/朝向有误");

            Object.Destroy(rig); Object.Destroy(ro); Object.Destroy(read); rt.Release();
        }

        private static FisheyeCalibration LoadReal(string name)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<FisheyeCalibration>(
                $"Assets/Main/Settings/Calibration/{name}.asset");
#else
            return Resources.Load<FisheyeCalibration>(name);
#endif
        }
    }
}
