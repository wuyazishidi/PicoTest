using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using PicoTest.Rendering;
using PicoTest.Experiments.WebRTC;

namespace PicoTest.Experiments.WebRTC.Tests
{
    /// <summary>
    /// 编辑器里用 VideoPlayer 播 camera.mp4（最接近运行时视频纹理方向）渲染穹顶，
    /// flipV=0/1 各出一张透视图到 Artifacts/，肉眼判断 WebRTC 视频路径的正确朝向/截取。
    /// </summary>
    public class WebRtcVideoDomeDumpTests
    {
        private static FisheyeCalibration Cal(float fx, float fy, float cx, float cy,
            float k1, float k2, float k3, float k4, float k5, float k6)
        {
            var c = ScriptableObject.CreateInstance<FisheyeCalibration>();
            c.fx = fx; c.fy = fy; c.cx = cx; c.cy = cy;
            c.k1 = k1; c.k2 = k2; c.k3 = k3; c.k4 = k4; c.k5 = k5; c.k6 = k6;
            c.width = 1280; c.height = 960;
            return c;
        }

        [UnityTest]
        public IEnumerator Dump_VideoDome_FlipVariants()
        {
            // 编辑器 VideoPlayer 放 H.264（camera.mp4 是 HEVC，Windows 编辑器解不了）
            string url = Application.dataPath + "/Experiments/Exp-WebRTC/Server/camera_h264.mp4";
            if (!File.Exists(url)) { Assert.Ignore("camera_h264.mp4 不存在: " + url); }

            var src = new VideoFileSource(url, 2560, 960);
            src.Start();
            float t = 0f;
            while (!src.IsReady && t < 10f) { yield return null; t += Time.deltaTime; }
            if (!src.IsReady) { src.Stop(); Assert.Ignore("VideoPlayer 未就绪(编辑器可能缺 mp4 解码器)"); }
            yield return new WaitForSeconds(0.4f);   // 多解几帧

            // PICO 出厂标定近似值（左/右）——orientation 测试用
            var left = Cal(585.61f, 579.22f, 631.05f, 482.05f, -0.1470f, 0.4471f, -1.2333f, 1.4042f, -0.7284f, 0.1433f);
            var right = Cal(582.20f, 575.61f, 634.43f, 485.29f, -0.1427f, 0.4120f, -1.1371f, 1.2843f, -0.6592f, 0.1282f);

            // 水平不动(146)，仅垂直仰角截断-42°：看不同俯角下底部形状
            yield return RenderDump(src.Frame, left, right, 0f, 100f, 0f, 146f, 12f, -42f, "webrtc_dome_vcut_p0.png");    // 平视
            yield return RenderDump(src.Frame, left, right, 0f, 100f, 30f, 146f, 12f, -42f, "webrtc_dome_vcut_p30.png");  // 下俯30°

            src.Stop();
            Assert.Pass("已出图 Artifacts/webrtc_dome_flip0.png 与 _flip1.png");
        }

        private IEnumerator RenderDump(Texture tex, FisheyeCalibration l, FisheyeCalibration r, float flipV, float fov, float pitchDeg, float coverageDeg, float featherDeg, float bottomCutDeg, string name)
        {
            var rig = new GameObject("rig");
            var cam = rig.AddComponent<Camera>();
            cam.transform.position = Vector3.zero; cam.transform.rotation = Quaternion.Euler(pitchDeg, 0f, 0f); // +pitch=下俯
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.1f, 0.1f, 0.12f, 1f); // 深灰底：区分穹顶外
            cam.fieldOfView = fov; cam.nearClipPlane = 0.05f; cam.farClipPlane = 100f;
            var rt = new RenderTexture(1024, 1024, 16);

            var ro = new GameObject("domeR");
            var dome = ro.AddComponent<FisheyeDomeRenderer>();
            dome.leftCalibration = l; dome.rightCalibration = r;
            dome.leftTex = tex; dome.rightTex = tex;
            dome.leftUVRect = new Vector4(0f, 0f, 0.5f, 1f);
            dome.rightUVRect = new Vector4(0.5f, 0f, 0.5f, 1f);
            dome.flipV = flipV;
            dome.coverageDeg = coverageDeg; dome.radius = 10f; dome.segments = 64;
            dome.edgeFeatherDeg = featherDeg;
            dome.bottomCutoffDeg = bottomCutDeg; dome.bottomFeatherDeg = 10f;
            dome.Initialize(); dome.PushParameters();
            yield return null;

            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req))
            {
                cam.SubmitRenderRequest(req);
                yield return new WaitForEndOfFrame();
                RenderTexture.active = rt;
                var read = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0); read.Apply();
                RenderTexture.active = null;
                var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Artifacts"));
                Directory.CreateDirectory(outDir);
                File.WriteAllBytes(Path.Combine(outDir, name), read.EncodeToPNG());
                // alpha 灰度图：白=不透明(有画面盖透视)，黑=透明(渐隐到透视)——验证竖直边界/角度羽化
                var px = read.GetPixels32();
                for (int p = 0; p < px.Length; p++) { byte a = px[p].a; px[p] = new Color32(a, a, a, 255); }
                var av = new Texture2D(1024, 1024, TextureFormat.RGBA32, false); av.SetPixels32(px); av.Apply();
                File.WriteAllBytes(Path.Combine(outDir, name.Replace(".png", "_alpha.png")), av.EncodeToPNG());
                Debug.Log("[VideoDome] dumped Artifacts/" + name + " (+_alpha)");
                Object.Destroy(read); Object.Destroy(av);
            }
            Object.Destroy(rig); Object.Destroy(ro); rt.Release();
        }
    }
}
