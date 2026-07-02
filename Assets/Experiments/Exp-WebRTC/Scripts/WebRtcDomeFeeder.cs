using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR;
using PicoTest.Rendering;

namespace PicoTest.Experiments.WebRTC
{
    /// <summary>
    /// 把双目鱼眼视频源（<see cref="IWebRtcVideoSource"/>；M0 为 <see cref="FakeStereoVideoSource"/>）
    /// 投到鱼眼穹顶。镜像 Main.Vst 的 VstCameraDomeFeeder：双缓冲(生产者线程 Marshal.Copy) →
    /// 主线程 LoadRawTextureData → 复用 FisheyeDomeRenderer + RobotHeadPoseDriver + see-through + B键退出。
    /// 只把“源”从 PICO VST 相机换成 WebRTC 视频；显示/云台/透视/退出与 FisheyeDomeXRLive 一致。
    /// </summary>
    public sealed class WebRtcDomeFeeder : MonoBehaviour
    {
        [Header("标定（左右各一）")]
        public FisheyeCalibration leftCalibration, rightCalibration;
        [Header("整帧分辨率（SBS）")]
        public int width = 2560, height = 720;
        [Header("穹顶覆盖角 / 半径")]
        public float coverageDeg = 150f;
        public float radius = 20f;
        [Header("低速云台伺服（两级迟滞，同 FisheyeDomeXRLive）")]
        public bool enableGazeServo = true;
        public float servoRateDegPerSec = 30f;
        public float servoDeadzoneDeg = 50f;
        public float servoReturnDeg = 20f;
        [Header("透视（VST passthrough）")]
        public bool enableSeeThrough = true;
        [Header("退出（B 键：停源 → 等待 → killProcess）")]
        public bool quitOnButtonB = true;
        public float quitDelaySec = 5f;

        /// <summary>外部注入的视频源；为空则自建假帧源（M0 冒烟）。</summary>
        public IWebRtcVideoSource Source { get; set; }

        /// <summary>当前上传的纹理（供测试读取）。</summary>
        public Texture2D Texture => _tex;

        private FisheyeDomeRenderer _dome;
        private Transform _anchor;
        private Camera _xrCam;
        private Texture2D _tex;
        private RobotHeadPoseDriver _servo;
        private IWebRtcVideoSource _source;

        private byte[] _front, _back;
        private bool _newFrame;
        private readonly object _swapLock = new object();
        private bool _bPrev, _quitting;

        private void Start()
        {
            if (leftCalibration == null || rightCalibration == null)
            {
                Debug.LogError("[WebRtcFeeder] 未连标定（left/right Calibration）。");
                return;
            }

            if (enableSeeThrough) EnableVideoSeeThrough();

            int bytes = width * height * 4;
            _front = new byte[bytes];
            _back = new byte[bytes];
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
            _dome.flipV = 1f;   // 视源朝向而定（真实 WebRTC 帧行序确认后再调）
            _dome.coverageDeg = coverageDeg; _dome.radius = radius; _dome.segments = 64;
            _dome.Initialize();
            _dome.PushParameters();

            // 低速云台伺服（死区/回停/速率沿用 FisheyeDomeXRLive）
            _servo = gameObject.AddComponent<RobotHeadPoseDriver>();
            _servo.robotHeadAnchor = _anchor;
            _servo.followLocalHead = enableGazeServo;
            _servo.rateDegPerSec = servoRateDegPerSec;
            _servo.deadzoneDeg = servoDeadzoneDeg;
            _servo.returnDeg = servoReturnDeg;

            // 视频源
            _source = Source ?? new FakeStereoVideoSource(width, height, 30);
            _source.OnFrame += OnFrame;
            _source.Start();
        }

        // 生产者线程：仅纯 C#（双缓冲拷贝），禁任何 Unity/JNI 调用
        private void OnFrame(IntPtr data, int size, int w, int h)
        {
            if (data == IntPtr.Zero || _back == null || size <= 0 || size > _back.Length) return;
            lock (_swapLock)
            {
                Marshal.Copy(data, _back, 0, size);
                var tmp = _front; _front = _back; _back = tmp;
                _newFrame = true;
            }
        }

        private void Update()
        {
            // 退出：右手柄 B → 停源 → 等待 → killProcess（照 VstCameraDomeFeeder）
            if (quitOnButtonB && !_quitting)
            {
                var rh = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                if (rh.TryGetFeatureValue(CommonUsages.secondaryButton, out bool bNow))
                {
                    if (bNow && !_bPrev) StartQuit();
                    _bPrev = bNow;
                }
            }
            if (_quitting) return;

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
                    _xrCam.clearFlags = CameraClearFlags.SolidColor;
                    _xrCam.backgroundColor = enableSeeThrough ? new Color(0f, 0f, 0f, 0f) : Color.black;
                }
            }
            if (_xrCam != null && _anchor != null)
                _anchor.position = _xrCam.transform.position;
        }

        /// <summary>反射开启系统透视（OpenXR PassthroughFeature 优先，回退传统 PXR_Manager）。</summary>
        private static void EnableVideoSeeThrough()
        {
            string[] typeNames =
            {
                "Unity.XR.OpenXR.Features.PICOSupport.PassthroughFeature",
                "Unity.XR.PXR.PXR_Manager",
            };
            bool any = false;
            foreach (var tn in typeNames)
            {
                try
                {
                    var t = FindType(tn);
                    if (t == null) continue;
                    var prop = t.GetProperty("EnableVideoSeeThrough", BindingFlags.Public | BindingFlags.Static);
                    if (prop != null && prop.CanWrite) { prop.SetValue(null, true); any = true; Debug.Log($"[WebRtcFeeder] 透视已开启 via {tn}"); continue; }
                    var field = t.GetField("EnableVideoSeeThrough", BindingFlags.Public | BindingFlags.Static);
                    if (field != null) { field.SetValue(null, true); any = true; Debug.Log($"[WebRtcFeeder] 透视已开启 via {tn} (field)"); }
                }
                catch (Exception e) { Debug.LogWarning($"[WebRtcFeeder] 设 {tn} 透视失败: {e.Message}"); }
            }
            if (!any) Debug.LogWarning("[WebRtcFeeder] 未找到透视 API，透视未开启");
        }

        private static Type FindType(string fullName)
        {
            var t = Type.GetType(fullName);
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        private void StartQuit()
        {
            if (_quitting) return;
            _quitting = true;
            Debug.Log($"[WebRtcFeeder] B 键退出：停源，{quitDelaySec:F0}s 后退出程序");
            StartCoroutine(QuitAfter());
        }

        private IEnumerator QuitAfter()
        {
            try { if (_source != null) { _source.OnFrame -= OnFrame; _source.Stop(); } } catch { }
            yield return new WaitForSeconds(quitDelaySec);
            Debug.Log("[WebRtcFeeder] 退出程序");
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var proc = new AndroidJavaClass("android.os.Process");
                proc.CallStatic("killProcess", proc.CallStatic<int>("myPid"));
            }
            catch (Exception e) { Debug.LogWarning($"[WebRtcFeeder] killProcess 失败: {e.Message}"); }
#elif UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnDestroy()
        {
            if (_source != null) { _source.OnFrame -= OnFrame; try { _source.Stop(); } catch { } }
            if (_tex != null) Destroy(_tex);
        }
    }
}
