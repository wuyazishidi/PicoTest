// Assets/Main/Vst/RawReprojectionFeeder.cs
using System;
using System.Runtime.InteropServices;
using Unity.XR.PICO.TOBSupport;
using UnityEngine;
using PicoTest.Rendering;

namespace PicoTest.Vst
{
    /// <summary>
    /// 纯 raw 视点重投影 Demo 的实时馈送器（独立于 VstCameraDomeFeeder，不碰其云台/透视逻辑）。
    /// PICO VST raw 鱼眼流 → ReprojectionDomeRenderer；HeadLocked；用真实外参做视点重投影。
    /// 深度面：Constant(M0，退化无穷远) / SpatialMesh(M1，近景视差)。纯 raw：超 FOV=黑，不开系统透视。
    /// 仅真机有效（Enterprise 相机需 PICO 4U + 激活）。编辑器无相机 → 黑屏 + 日志。
    /// </summary>
    public sealed class RawReprojectionFeeder : MonoBehaviour
    {
        public enum DepthMode { Constant, SpatialMesh }

        [Header("标定（左右各一，本机 = RealLeft/RealRight）")]
        public FisheyeCalibration leftCalibration, rightCalibration;
        [Header("分辨率 / fps")]
        public int width = 2560, height = 960, fps = 30;
        [Header("穹顶")]
        public float coverageDeg = 146f;   // 匹配相机真实水平 FOV
        [Header("深度面")]
        public DepthMode depthMode = DepthMode.Constant;
        public float constantDepth = 20f;      // Constant 模式深度
        public float spatialFallbackDepth = 20f; // SpatialMesh 未命中回退
        public LayerMask spatialMeshLayers = ~0;

        private ReprojectionDomeRenderer _dome;
        private Transform _anchor;
        private Camera _xrCam;
        private Texture2D _tex;
        private bool _extrinsicsApplied;
        private bool _dynamicDepth;

        // 双缓冲：原生线程写 _back，主线程读 _front
        private byte[] _front, _back;
        private bool _newFrame;
        private readonly object _swapLock = new object();

        private void Start()
        {
            if (leftCalibration == null || rightCalibration == null)
            {
                Debug.LogError("[RawReproj] 未连标定（RealLeft/RealRight）。");
                return;
            }

            _front = new byte[width * height * 4];
            _back = new byte[width * height * 4];
            _tex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

            // HeadLocked 锚点（穹顶跟头位置+朝向）
            _anchor = new GameObject("ReprojDomeAnchor").transform;
            _anchor.SetParent(transform, false);

            _dome = gameObject.AddComponent<ReprojectionDomeRenderer>();
            _dome.headAnchor = _anchor;
            _dome.leftCalibration = leftCalibration;
            _dome.rightCalibration = rightCalibration;
            _dome.leftTex = _tex; _dome.rightTex = _tex;
            _dome.leftUVRect = new Vector4(0f, 0f, 0.5f, 1f);    // SBS 左半 → 左眼
            _dome.rightUVRect = new Vector4(0.5f, 0f, 0.5f, 1f); // SBS 右半 → 右眼
            _dome.flipV = 1f;   // 相机缓冲 top-down → 翻 v
            _dome.coverageDeg = coverageDeg; _dome.segments = 64;

            if (depthMode == DepthMode.SpatialMesh)
            {
                _dome.DepthSurface = new SpatialMeshDepthSurface
                { meshLayers = spatialMeshLayers, fallbackDepth = spatialFallbackDepth };
                _dynamicDepth = true; // 每帧刷新深度
            }
            else
            {
                _dome.DepthSurface = new ConstantDepthSurface(constantDepth);
                _dynamicDepth = false;
            }

            _dome.Initialize();

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
            VstCamera.PumpFromMain(); // 必须每帧泵（PICO 崩溃规避）

            if (_newFrame && _tex != null)
            {
                lock (_swapLock)
                {
                    _tex.LoadRawTextureData(_front);
                    _newFrame = false;
                }
                _tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            }

            // 拿到真实相机外参后喂入标定（替换 identity），只做一次并重推参数
            if (!_extrinsicsApplied && VstCamera.ExtrinsicsValid)
            {
                leftCalibration.SetFromSdkExtrinsics(VstCamera.LeftExtrinsics, Vector3.zero);
                rightCalibration.SetFromSdkExtrinsics(VstCamera.RightExtrinsics, Vector3.zero);
                _dome.PushParameters();
                _extrinsicsApplied = true;
                Debug.Log($"[RawReproj] 外参已应用 L.t={leftCalibration.extrinsicTranslation} R.t={rightCalibration.extrinsicTranslation}");
            }

            // HeadLocked：穹顶跟头位置+朝向
            if (_xrCam == null)
            {
                _xrCam = Camera.main;
                if (_xrCam != null)
                {
                    _xrCam.clearFlags = CameraClearFlags.SolidColor; // 纯 raw：背景黑（不开系统透视）
                    _xrCam.backgroundColor = Color.black;
                }
            }
            if (_xrCam != null && _anchor != null)
            {
                _anchor.position = _xrCam.transform.position;
                _anchor.rotation = _xrCam.transform.rotation;
            }

            // M1 动态深度：每帧按 spatial mesh 刷新顶点位移
            if (_dynamicDepth && _dome != null) _dome.ApplyDepth();
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
