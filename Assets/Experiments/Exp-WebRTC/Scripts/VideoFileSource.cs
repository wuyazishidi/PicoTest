using System.Collections;
using UnityEngine;
using UnityEngine.Video;

namespace PicoTest.Experiments.WebRTC
{
    /// <summary>
    /// 视频文件源（编辑器测试/本地回放）：用 VideoPlayer 循环解码 mp4 → RenderTexture，作 Frame。
    /// 用于在编辑器里以“最接近运行时视频纹理方向”的方式验证穹顶截取/朝向（flipV），
    /// 免走 WebRTC/信令/浏览器。url 指向 mp4 绝对路径。
    /// </summary>
    public sealed class VideoFileSource : IWebRtcVideoSource
    {
        public Texture Frame => _rt;
        public bool IsReady => _vp != null && _vp.isPrepared && _vp.frame > 0;

        private readonly string _url;
        private readonly int _w, _h;
        private VideoPlayer _vp;
        private RenderTexture _rt;
        private GameObject _go;

        public VideoFileSource(string url, int width = 2560, int height = 960)
        {
            _url = url; _w = width; _h = height;
        }

        public IEnumerator GetRenderPump() => null;

        public void Start()
        {
            _rt = new RenderTexture(_w, _h, 0, RenderTextureFormat.ARGB32) { wrapMode = TextureWrapMode.Clamp };
            _go = new GameObject("VideoFileSource");
            _vp = _go.AddComponent<VideoPlayer>();
            _vp.source = VideoSource.Url;
            _vp.url = _url;
            _vp.isLooping = true;
            _vp.renderMode = VideoRenderMode.RenderTexture;
            _vp.targetTexture = _rt;
            _vp.audioOutputMode = VideoAudioOutputMode.None;
            _vp.playOnAwake = true;
            _vp.Play();
        }

        public void Tick() { }

        public void Stop()
        {
            if (_vp != null) _vp.Stop();
            if (_go != null) { if (Application.isPlaying) Object.Destroy(_go); else Object.DestroyImmediate(_go); }
            if (_rt != null) _rt.Release();
            _vp = null; _go = null; _rt = null;
        }
    }
}
