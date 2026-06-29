// Assets/Main/Vst/VstCameraDomeFeeder.cs
using System;
using System.Runtime.InteropServices;
using Unity.XR.PICO.TOBSupport;
using UnityEngine;
using PicoTest.Rendering;

namespace PicoTest.Vst
{
    /// <summary>
    /// 把 PICO VST 实时 raw 鱼眼流喂进鱼眼穹顶（去畸变在 shader 用出厂标定 k1-k6 做）。
    /// 帧回调（原生线程）双缓冲 Marshal.Copy → Update（UnityMain）LoadRawTextureData 上传。
    /// 穹顶 WorldLocked 跟头位置；单通道立体实例化 → 左右眼各采 SBS 半幅 = 真立体。
    /// 仅真机有效（Enterprise 相机需 PICO 4U + 激活）。编辑器无相机 → 黑屏 + 日志提示。
    /// </summary>
    public sealed class VstCameraDomeFeeder : MonoBehaviour
    {
        [Header("出厂标定（左右各一，本机 A9410 = RealLeft/RealRight）")]
        public FisheyeCalibration leftCalibration, rightCalibration;
        [Header("分辨率 / fps")]
        public int width = 2560, height = 960, fps = 60;
        [Header("穹顶覆盖角 / 半径")]
        public float coverageDeg = 160f;
        public float radius = 20f;

        private FisheyeDomeRenderer _dome;
        private Transform _anchor;
        private Camera _xrCam;
        private Texture2D _tex;

        // 双缓冲：原生线程写 _back，主线程读 _front
        private byte[] _front, _back;
        private bool _newFrame;
        private readonly object _swapLock = new object();

        private void Start()
        {
            if (leftCalibration == null || rightCalibration == null)
            {
                Debug.LogError("[VstFeeder] 未连标定（RealLeft/RealRight）。");
                return;
            }

            _front = new byte[width * height * 4];
            _back = new byte[width * height * 4];
            _tex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

            // 穹顶（跟头位置、世界锁朝向）
            _anchor = new GameObject("DomeAnchor").transform;
            _anchor.SetParent(transform, false);
            _dome = gameObject.AddComponent<FisheyeDomeRenderer>();
            _dome.frame = FisheyeDomeRenderer.RenderFrame.WorldLocked;
            _dome.robotHeadAnchor = _anchor;
            _dome.leftCalibration = leftCalibration;
            _dome.rightCalibration = rightCalibration;
            _dome.leftTex = _tex; _dome.rightTex = _tex;
            _dome.leftUVRect = new Vector4(0f, 0f, 0.5f, 1f);    // SBS 左半 → 左眼
            _dome.rightUVRect = new Vector4(0.5f, 0f, 0.5f, 1f); // SBS 右半 → 右眼
            _dome.flipV = 1f;   // 相机缓冲 top-down，Unity 纹理 bottom-left → 翻 v
            _dome.coverageDeg = coverageDeg; _dome.radius = radius; _dome.segments = 64;
            _dome.Initialize();
            _dome.PushParameters();

            // 开相机
            VstCamera.OnFrame += OnFrame;
            VstCamera.Configure(width, height, fps);
            VstCamera.Initialize();
        }

        // 原生线程：仅纯 C#（双缓冲拷贝），禁任何 Unity/JNI 调用
        private void OnFrame(Frame frame)
        {
            int size = (int)frame.datasize;
            if (size <= 0 || frame.data == IntPtr.Zero || _back == null || _back.Length < size) return;
            lock (_swapLock)
            {
                Marshal.Copy(frame.data, _back, 0, size);
                var tmp = _front; _front = _back; _back = tmp;
                _newFrame = true;
            }
        }

        private void Update()
        {
            VstCamera.PumpFromMain(); // 必须每帧泵（PICO 崩溃规避：延迟执行原生线程捕获的 JNI 活）

            if (_newFrame && _tex != null)
            {
                lock (_swapLock)
                {
                    _tex.LoadRawTextureData(_front);
                    _newFrame = false;
                }
                _tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            }

            // 穹顶跟头位置（罩住眼点），不跟头转 → 转头在静止穹顶内环顾
            if (_xrCam == null)
            {
                _xrCam = Camera.main;
                if (_xrCam != null)
                {
                    _xrCam.clearFlags = CameraClearFlags.SolidColor; // 穹顶是背景，关天空盒
                    _xrCam.backgroundColor = Color.black;
                }
            }
            if (_xrCam != null && _anchor != null)
                _anchor.position = _xrCam.transform.position;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) VstCamera.PauseForScreenOff();
            else VstCamera.ResumeAfterScreenOn();
        }

        private void OnDestroy()
        {
            VstCamera.OnFrame -= OnFrame;
            VstCamera.Shutdown();
            if (_tex != null) Destroy(_tex);
        }
    }
}
