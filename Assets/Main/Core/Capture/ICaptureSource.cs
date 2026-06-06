// Assets/Main/Core/Capture/ICaptureSource.cs
using System;
using PicoTest.Core.Schema;

namespace PicoTest.Core.Capture
{
    /// <summary>
    /// 采集源。帧由 Tick(nowNs) 显式泵送（薄壳层在 Update 里泵，测试手动泵 —— 确定性）。
    /// M4 真实源（BodyTracking/相机）实现同一接口，链路不变。
    /// </summary>
    public interface ICaptureSource
    {
        string StreamId { get; }
        StreamType Type { get; }
        int NominalHz { get; }
        /// <summary>(timestampNs, 序列化帧字节)。订阅方负责 Enqueue；帧数组所有权随事件转移，源不复用。</summary>
        event Action<long, byte[]> FrameProduced;
        void Start();
        void Tick(long nowNs);
        void Stop();
    }
}
