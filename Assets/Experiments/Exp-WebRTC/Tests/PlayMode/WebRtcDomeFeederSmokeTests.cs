using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PicoTest.Rendering;
using PicoTest.Experiments.WebRTC;

namespace PicoTest.Experiments.WebRTC.Tests
{
    /// <summary>
    /// PlayMode 冒烟：WebRtcDomeFeeder 用假帧源，验证 源(生产者线程)→双缓冲→Texture2D 上传 打通
    /// （纹理中心像素非黑）。标定在代码里造（不依赖 AssetDatabase）；透视/退出关掉以免依赖 XR。
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
        public IEnumerator Feeder_UploadsNonBlackFrame()
        {
            var go = new GameObject("WebRtcFeederTest");
            go.SetActive(false);
            var f = go.AddComponent<WebRtcDomeFeeder>();
            f.leftCalibration = MakeCalib();
            f.rightCalibration = MakeCalib();
            f.width = 2560; f.height = 720;
            f.enableSeeThrough = false;   // 测试环境无 PICO 透视
            f.quitOnButtonB = false;      // 不依赖手柄
            go.SetActive(true);           // 触发 Start()（PlayMode 下 MonoBehaviour 正常 tick）

            bool nonBlack = false;
            float t = 0f;
            while (t < 5f && !nonBlack)
            {
                yield return null;
                t += Time.deltaTime;
                var tex = f.Texture;
                if (tex != null)
                {
                    var col = tex.GetPixel(tex.width / 2, tex.height / 2);
                    if (col.r + col.g + col.b > 0.01f) nonBlack = true;
                }
            }

            Assert.IsTrue(nonBlack, "5s 内未把非黑帧上传到纹理（源→双缓冲→纹理链路未打通）");
            Object.Destroy(go);
        }
    }
}
