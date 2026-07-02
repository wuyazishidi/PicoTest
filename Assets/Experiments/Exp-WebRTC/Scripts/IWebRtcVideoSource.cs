using System.Collections;
using UnityEngine;

namespace PicoTest.Experiments.WebRTC
{
    /// <summary>
    /// 双目鱼眼视频源抽象（com.unity.webrtc 版）。帧以 GPU <see cref="Texture"/> 交付
    /// （官方包解码+上传，主/渲染线程），feeder 直接把它作穹顶 shader 的 leftTex/rightTex（SBS 分半）。
    /// 无 byte 双缓冲/原生线程 —— 官方包已封装。
    /// </summary>
    public interface IWebRtcVideoSource
    {
        /// <summary>当前帧纹理；null=尚无。可能随时间原地更新或被替换。</summary>
        Texture Frame { get; }

        /// <summary>com.unity.webrtc 需每帧驱动的渲染协程（WebRTC.Update()）；无需驱动的源返回 null。</summary>
        IEnumerator GetRenderPump();

        void Start();
        void Tick();   // 主线程每帧调用（假源刷新图案；真实源可空操作）
        void Stop();
    }
}
