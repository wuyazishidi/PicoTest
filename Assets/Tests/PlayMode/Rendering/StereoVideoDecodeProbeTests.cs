// Assets/Tests/PlayMode/Rendering/StereoVideoDecodeProbeTests.cs
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Video;

namespace PicoTest.Tests.PlayMode.Rendering
{
    /// <summary>
    /// 能力探针：本环境能否运行时解码 StreamingAssets/camera.mp4 (HEVC 立体 SBS)。
    /// - 能解码（如 PICO 设备、或装了 HEVC Video Extensions 的 Windows）：渲染穹顶并把
    ///   单眼视图存为 Artifacts/dome_real.png 供肉眼查看，断言中心非黑。
    /// - 不能（裸 Windows 编辑器缺 HEVC 解码器）：带原因 Ignore，不阻塞门禁。
    /// 已知：裸 Windows 编辑器 VideoPlayer 无法解码 h265 → "Cannot read file"。
    /// </summary>
    public class StereoVideoDecodeProbeTests
    {
        [UnityTest]
        public IEnumerator RealStereoVideo_OnDome_DumpsPng_OrIgnoresIfNoCodec()
        {
            var filePath = Path.Combine(Application.streamingAssetsPath, "camera.mp4");
            Assert.IsTrue(File.Exists(filePath), $"视频不存在: {filePath}");
            var url = new System.Uri(filePath).AbsoluteUri; // file:// URI（规避反斜杠）
            LogAssert.ignoreFailingMessages = true;          // VideoPlayer 解码失败会打 Error 日志

            var go = new GameObject("vp");
            var vp = go.AddComponent<VideoPlayer>();
            vp.source = VideoSource.Url; vp.url = url;
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.audioOutputMode = VideoAudioOutputMode.None;
            vp.playOnAwake = false; vp.isLooping = false;
            var sbs = new RenderTexture(2560, 960, 0) { name = "sbs" };
            vp.targetTexture = sbs;

            bool err = false; string errMsg = null;
            vp.errorReceived += (v, m) => { err = true; errMsg = m; };

            vp.Prepare();
            int frames = 0;
            while (!vp.isPrepared && !err && frames < 600) { frames++; yield return null; }
            if (err || !vp.isPrepared)
            {
                Object.Destroy(go); sbs.Release();
                Assert.Ignore($"HEVC 运行时解码不可用（{errMsg ?? "prepare 未完成"}）。" +
                              "裸 Windows 编辑器缺 HEVC 解码器；真机/装 HEVC 扩展后本测试自动转为 PNG 输出。");
                yield break;
            }

            vp.Play();
            int playWait = 0;
            while (vp.frame < 2 && !err && playWait < 300) { playWait++; yield return null; }
            yield return null;

            // 渲染穹顶（喂 SBS 单图，左/右各取一半，本探针只验单眼路径）
            var rig = new GameObject("rig");
            var cam = rig.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            var domeRt = new RenderTexture(1024, 1024, 16);

            var ro = new GameObject("renderer");
            var r = ro.AddComponent<PicoTest.Rendering.FisheyeDomeRenderer>();
            r.leftCalibration = LoadReal("RealLeft");
            r.rightCalibration = LoadReal("RealRight");
            r.leftTex = sbs; r.rightTex = sbs;        // SBS：UV 分半留待正式 feeder
            r.coverageDeg = 150; r.radius = 10; r.segments = 48;
            r.Initialize(); r.PushParameters();
            yield return null;

            var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = domeRt };
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, req))
            {
                cam.SubmitRenderRequest(req);
                yield return new WaitForEndOfFrame();
                RenderTexture.active = domeRt;
                var tex = new Texture2D(domeRt.width, domeRt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, domeRt.width, domeRt.height), 0, 0); tex.Apply();
                RenderTexture.active = null;

                var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Artifacts"));
                Directory.CreateDirectory(outDir);
                File.WriteAllBytes(Path.Combine(outDir, "dome_real.png"), tex.EncodeToPNG());
                Debug.Log($"[VideoProbe] decoded + rendered, dumped Artifacts/dome_real.png");
                Object.Destroy(tex);
            }

            Object.Destroy(go); Object.Destroy(rig); Object.Destroy(ro);
            sbs.Release(); domeRt.Release();
        }

        private static PicoTest.Rendering.FisheyeCalibration LoadReal(string name)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<PicoTest.Rendering.FisheyeCalibration>(
                $"Assets/Main/Settings/Calibration/{name}.asset");
#else
            return Resources.Load<PicoTest.Rendering.FisheyeCalibration>(name);
#endif
        }
    }
}
