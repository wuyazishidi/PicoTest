using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace PicoTest.Experiments.WebRTC
{
    /// <summary>
    /// M0 假帧源：后台线程按 fps 生成 SBS RGBA 测试图（左半偏红 / 右半偏蓝 + 随帧移动的绿渐变），
    /// 经固定的 pinned 缓冲区通过 OnFrame(IntPtr,...) 同步回调 —— 模拟真实 WebRTC 原生解码线程投帧，
    /// 用于在无原生库/无网络下验证 WebRtcDomeFeeder 的双缓冲→纹理→穹顶复用。
    /// 回调是同步的：订阅者在回调内完成 Marshal.Copy 后本线程才继续覆写缓冲，无数据竞争。
    /// </summary>
    public sealed class FakeStereoVideoSource : IWebRtcVideoSource
    {
        public event Action<IntPtr, int, int, int> OnFrame;

        public int Width { get; }
        public int Height { get; }
        public int Fps { get; }

        private readonly byte[] _buf;
        private GCHandle _pin;
        private IntPtr _ptr;
        private Thread _thread;
        private volatile bool _run;

        public FakeStereoVideoSource(int width = 2560, int height = 720, int fps = 30)
        {
            Width = width; Height = height; Fps = fps;
            _buf = new byte[width * height * 4];
            _pin = GCHandle.Alloc(_buf, GCHandleType.Pinned);
            _ptr = _pin.AddrOfPinnedObject();
        }

        public void Start()
        {
            if (_thread != null) return;
            _run = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "FakeStereoVideoSource" };
            _thread.Start();
        }

        public void Stop()
        {
            _run = false;
            var t = _thread; _thread = null;
            t?.Join(500);
            if (_pin.IsAllocated) _pin.Free();
            _ptr = IntPtr.Zero;
        }

        private void Loop()
        {
            int frame = 0;
            int periodMs = Math.Max(1, 1000 / Math.Max(1, Fps));
            int halfW = Width / 2;
            while (_run)
            {
                int phase = (frame * 4) & 0xFF;
                for (int y = 0; y < Height; y++)
                {
                    int row = y * Width * 4;
                    for (int x = 0; x < Width; x++)
                    {
                        int i = row + x * 4;
                        byte g = (byte)((x + phase) & 0xFF);
                        if (x < halfW) { _buf[i] = 200; _buf[i + 1] = g; _buf[i + 2] = 40; }   // 左眼：红底
                        else           { _buf[i] = 40;  _buf[i + 1] = g; _buf[i + 2] = 200; }   // 右眼：蓝底
                        _buf[i + 3] = 255;
                    }
                }
                var cb = OnFrame;
                if (cb != null && _ptr != IntPtr.Zero)
                {
                    try { cb(_ptr, _buf.Length, Width, Height); }
                    catch { /* 生产者线程禁日志/Unity 调用 */ }
                }
                frame++;
                Thread.Sleep(periodMs);
            }
        }
    }
}
