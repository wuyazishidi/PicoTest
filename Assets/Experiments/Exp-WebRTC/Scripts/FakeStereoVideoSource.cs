using System.Collections;
using UnityEngine;

namespace PicoTest.Experiments.WebRTC
{
    /// <summary>
    /// 假帧源（编辑器/无对端测试）：主线程生成 SBS RGBA 测试图（左半偏红/右半偏蓝 + 随帧移动的绿渐变），
    /// 以 Texture 交付 —— 与 com.unity.webrtc 的 Texture 交付一致，用于验证 feeder→穹顶复用。
    /// </summary>
    public sealed class FakeStereoVideoSource : IWebRtcVideoSource
    {
        public Texture Frame => _tex;
        public int Width { get; }
        public int Height { get; }

        private Texture2D _tex;
        private readonly Color32[] _px;
        private int _frame;

        public FakeStereoVideoSource(int width = 2560, int height = 720)
        {
            Width = width; Height = height;
            _px = new Color32[width * height];
        }

        public IEnumerator GetRenderPump() => null;

        public void Start()
        {
            _tex = new Texture2D(Width, Height, TextureFormat.RGBA32, mipChain: false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Tick();
        }

        public void Tick()
        {
            if (_tex == null) return;
            int phase = (_frame * 4) & 0xFF;
            int half = Width / 2;
            for (int y = 0; y < Height; y++)
            {
                int row = y * Width;
                for (int x = 0; x < Width; x++)
                {
                    byte g = (byte)((x + phase) & 0xFF);
                    _px[row + x] = x < half ? new Color32(200, g, 40, 255) : new Color32(40, g, 200, 255);
                }
            }
            _tex.SetPixels32(_px);
            _tex.Apply(updateMipmaps: false);
            _frame++;
        }

        public void Stop()
        {
            if (_tex == null) return;
            if (Application.isPlaying) Object.Destroy(_tex);
            else Object.DestroyImmediate(_tex);
            _tex = null;
        }
    }
}
