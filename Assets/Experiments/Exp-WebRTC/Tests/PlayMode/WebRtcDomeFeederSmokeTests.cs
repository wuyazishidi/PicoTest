using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PicoTest.Rendering;
using PicoTest.Experiments.WebRTC;

namespace PicoTest.Experiments.WebRTC.Tests
{
    /// <summary>
    /// PlayMode 冒烟：WebRtcDomeFeeder 用假帧源（Texture 交付），验证 源→穹顶纹理绑定 打通
    /// （feeder.Frame 中心像素非黑）。透视/退出/真实 WebRTC 关掉以免依赖 XR/网络。
    /// </summary>
    public class WebRtcDomeFeederSmokeTests
    {
        private static FisheyeCalibration MakeCalib()
        {
            var c = ScriptableObject.CreateInstance<FisheyeCalibration>();
            c.fx = 366f; c.fy = 366f; c.cx = 640f; c.cy = 360f;
            c.width = 1280; c.height = 720;
            return c;
        }

        [UnityTest]
        public IEnumerator Feeder_ShowsNonBlackFrame()
        {
            var go = new GameObject("WebRtcFeederTest");
            go.SetActive(false);
            var f = go.AddComponent<WebRtcDomeFeeder>();
            f.leftCalibration = MakeCalib();
            f.rightCalibration = MakeCalib();
            f.enableSeeThrough = false;
            f.quitOnButtonB = false;
            f.useRealWebRtc = false;   // 用假帧源
            go.SetActive(true);        // 触发 Start()

            bool ok = false; float t = 0f;
            while (t < 5f && !ok)
            {
                yield return null;
                t += Time.deltaTime;
                var tex = f.Frame as Texture2D;
                if (tex != null)
                {
                    var c = tex.GetPixel(tex.width / 2, tex.height / 2);
                    if (c.r + c.g + c.b > 0.01f) ok = true;
                }
            }

            Assert.IsTrue(ok, "5s 内 feeder 未产出非黑帧（源→穹顶链路未打通）");
            Object.Destroy(go);
        }
    }
}
