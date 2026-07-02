using System;

namespace PicoTest.Experiments.WebRTC
{
    /// <summary>
    /// 双目鱼眼视频源抽象。帧在“生产者线程”回调（真实为 WebRTC 原生解码线程；M0 为后台线程）。
    /// 订阅者在回调里只许做纯 C#（Marshal.Copy / 双缓冲），禁任何 Unity/JNI 调用（照 VstCamera 纪律）。
    /// 约定：data 指向连续 RGBA32 内存；size 为字节数；width×height 为整帧(SBS)像素尺寸。
    /// </summary>
    public interface IWebRtcVideoSource
    {
        /// <summary>(data, byteSize, width, height) —— 生产者线程调用。</summary>
        event Action<IntPtr, int, int, int> OnFrame;

        void Start();
        void Stop();
    }
}
