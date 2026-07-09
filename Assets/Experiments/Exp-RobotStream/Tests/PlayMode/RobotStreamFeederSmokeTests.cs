// Assets/Experiments/Exp-RobotStream/Tests/PlayMode/RobotStreamFeederSmokeTests.cs
using System.Collections;
using NUnit.Framework;
using PicoTest.Experiments.WebRTC;
using PicoTest.Rendering;
using UnityEngine;
using UnityEngine.TestTools;

namespace PicoTest.Experiments.RobotStream.Tests
{
    /// <summary>
    /// PlayMode 冒烟：RobotStreamFeeder 用假帧源（Texture 交付），验证 WebRTC 源→穹顶纹理链路打通
    /// （feeder.Frame 中心像素非黑）。透视/退出/真实 WebRTC 关掉以免依赖 XR/网络。
    /// </summary>
    public class RobotStreamFeederSmokeTests
    {
        private static FisheyeCalibration MakeCalib()
        {
            var c = ScriptableObject.CreateInstance<FisheyeCalibration>();
            c.fx = 366f; c.fy = 366f; c.cx = 640f; c.cy = 480f;
            c.width = 1280; c.height = 960;
            return c;
        }

        [UnityTest]
        public IEnumerator Feeder_ShowsNonBlackFrame_FromFakeSource()
        {
            var go = new GameObject("RobotStreamFeederTest");
            go.SetActive(false);
            var f = go.AddComponent<RobotStreamFeeder>();
            f.fallbackLeft = MakeCalib();
            f.fallbackRight = MakeCalib();
            f.enableSeeThrough = false;
            f.quitOnButtonB = false;
            f.useRealWebRtc = false;                       // 假帧源
            f.Source = new FakeStereoVideoSource(2560, 960);
            go.SetActive(true);                            // 触发 Start()

            bool ok = false; float t = 0f;
            while (t < 5f && !ok)
            {
                yield return null;
                t += Time.deltaTime;
                var tex = f.Frame as Texture2D;
                if (tex != null)
                {
                    var c = tex.GetPixel(tex.width / 4, tex.height / 2); // 左半中心
                    if (c.r + c.g + c.b > 0.01f) ok = true;
                }
            }

            Assert.IsTrue(ok, "5s 内 feeder 未产出非黑帧（WebRTC 源→穹顶链路未打通）");
            Object.Destroy(go);
        }
    }
}
