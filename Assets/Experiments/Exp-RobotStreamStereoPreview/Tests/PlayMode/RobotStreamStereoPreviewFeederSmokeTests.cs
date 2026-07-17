// Assets/Experiments/Exp-RobotStreamStereoPreview/Tests/PlayMode/RobotStreamStereoPreviewFeederSmokeTests.cs
using System.Collections;
using NUnit.Framework;
using PicoTest.Experiments.RobotDsDome;
using PicoTest.Experiments.WebRTC;
using UnityEngine;
using UnityEngine.TestTools;

namespace PicoTest.Experiments.RobotStreamStereoPreview.Tests
{
    /// <summary>
    /// PlayMode 冒烟：RobotStreamStereoPreviewFeeder 用假帧源（SBS 左红/右蓝测试图），验证
    /// WebRTC 源→穹顶纹理链路打通，且左右半边确实是两块不同的画面（真双目，非左目版的整图复用）。
    /// 透视/退出/真实 WebRTC 关掉以免依赖 XR/网络。
    /// </summary>
    public class RobotStreamStereoPreviewFeederSmokeTests
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
        public IEnumerator Feeder_ShowsDistinctLeftRightHalves_FromFakeSource()
        {
            var go = new GameObject("RobotStereoPreviewFeederTest");
            go.SetActive(false);
            var f = go.AddComponent<RobotStreamStereoPreviewFeeder>();
            f.leftCalibration = MakeCalib();
            f.rightCalibration = MakeCalib();
            f.enableSeeThrough = false;
            f.quitOnButtonB = false;
            f.useRealWebRtc = false;                       // 假帧源（SBS：左红/右蓝）
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
                    var left = tex.GetPixel(tex.width / 4, tex.height / 2);
                    var right = tex.GetPixel(tex.width * 3 / 4, tex.height / 2);
                    bool leftNonBlack = left.r + left.g + left.b > 0.01f;
                    bool rightNonBlack = right.r + right.g + right.b > 0.01f;
                    bool distinct = Mathf.Abs(left.r - right.r) > 0.1f || Mathf.Abs(left.b - right.b) > 0.1f;
                    if (leftNonBlack && rightNonBlack && distinct) ok = true;
                }
            }

            Assert.IsTrue(ok, "5s 内 feeder 未产出左右两块不同的非黑画面（双目 SBS 链路未打通）");
            Object.Destroy(go);
        }
    }
}
