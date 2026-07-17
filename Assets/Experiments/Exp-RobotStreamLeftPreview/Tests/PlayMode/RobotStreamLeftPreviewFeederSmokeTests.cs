// Assets/Experiments/Exp-RobotStreamLeftPreview/Tests/PlayMode/RobotStreamLeftPreviewFeederSmokeTests.cs
using System.Collections;
using NUnit.Framework;
using PicoTest.Experiments.RobotDsDome;
using PicoTest.Experiments.WebRTC;
using UnityEngine;
using UnityEngine.TestTools;

namespace PicoTest.Experiments.RobotStreamLeftPreview.Tests
{
    /// <summary>
    /// PlayMode 冒烟：RobotStreamLeftPreviewFeeder 用假帧源（Texture 交付），验证 WebRTC 源→穹顶
    /// 纹理链路打通（feeder.Frame 中心像素非黑）。透视/退出/真实 WebRTC 关掉以免依赖 XR/网络。
    /// </summary>
    public class RobotStreamLeftPreviewFeederSmokeTests
    {
        // 同 DsDomeRenderSmokeTests.Cal()：前向光线不受 fx/cx 影响，数值只求让 DsDomeRenderer 初始化不报错。
        private static DsEyeCalibration MakeCalib()
        {
            var c = ScriptableObject.CreateInstance<DsEyeCalibration>();
            c.xi = 0f; c.alpha = 0.5f; c.fx = c.fy = 366f; c.cx = 640f; c.cy = 480f;
            c.width = 1280; c.height = 960;
            return c;
        }

        [UnityTest]
        public IEnumerator Feeder_ShowsNonBlackFrame_FromFakeSource()
        {
            var go = new GameObject("RobotLeftPreviewFeederTest");
            go.SetActive(false);
            var f = go.AddComponent<RobotStreamLeftPreviewFeeder>();
            f.calibration = MakeCalib();
            f.enableSeeThrough = false;
            f.quitOnButtonB = false;
            f.useRealWebRtc = false;                       // 假帧源
            f.Source = new FakeStereoVideoSource(1280, 960);
            go.SetActive(true);                            // 触发 Start()

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

            Assert.IsTrue(ok, "5s 内 feeder 未产出非黑帧（WebRTC 源→穹顶链路未打通）");
            Object.Destroy(go);
        }
    }
}
