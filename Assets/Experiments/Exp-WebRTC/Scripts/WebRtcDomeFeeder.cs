using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;
using PicoTest.Rendering;
using PicoTest.Experiments.WebRTC.Signaling;

namespace PicoTest.Experiments.WebRTC
{
    /// <summary>
    /// 把双目鱼眼视频源（com.unity.webrtc 的 <see cref="UnityWebRtcVideoSource"/>，或编辑器测试用
    /// <see cref="FakeStereoVideoSource"/>）投到鱼眼穹顶。复用 FisheyeDomeRenderer + RobotHeadPoseDriver +
    /// see-through + B 键退出（同 FisheyeDomeXRLive）。源以 Texture 交付 → 直接作 leftTex/rightTex（SBS 分半）。
    /// </summary>
    public sealed class WebRtcDomeFeeder : MonoBehaviour
    {
        [Header("标定（左右各一）")]
        public FisheyeCalibration leftCalibration, rightCalibration;
        [Header("穹顶覆盖角 / 半径")]
        public float coverageDeg = 150f;
        public float radius = 20f;
        [Header("低速云台伺服（两级迟滞，同 FisheyeDomeXRLive）")]
        public bool enableGazeServo = true;
        public float servoRateDegPerSec = 30f;
        public float servoDeadzoneDeg = 50f;
        public float servoReturnDeg = 20f;
        [Header("透视 / 退出")]
        public bool enableSeeThrough = true;
        public bool quitOnButtonB = true;
        public float quitDelaySec = 5f;
        [Header("视频源")]
        public bool useRealWebRtc = false;                 // false=假帧源(编辑器冒烟); true=com.unity.webrtc
        public string signalingUrl = "ws://127.0.0.1:8765";

        /// <summary>外部注入的视频源；为空则按 useRealWebRtc 自建。</summary>
        public IWebRtcVideoSource Source { get; set; }
        public Texture Frame => _source?.Frame;

        private FisheyeDomeRenderer _dome;
        private Transform _anchor;
        private Camera _xrCam;
        private RobotHeadPoseDriver _servo;
        private IWebRtcVideoSource _source;
        private Texture _lastFrame;
        private bool _bPrev, _quitting;

        private void Start()
        {
            if (leftCalibration == null || rightCalibration == null)
            {
                Debug.LogError("[WebRtcFeeder] 未连标定（left/right Calibration）。");
                return;
            }

            if (enableSeeThrough) EnableVideoSeeThrough();

            _anchor = new GameObject("DomeAnchor").transform;
            _anchor.SetParent(transform, false);
            _dome = gameObject.AddComponent<FisheyeDomeRenderer>();
            _dome.frame = FisheyeDomeRenderer.RenderFrame.WorldLocked;
            _dome.robotHeadAnchor = _anchor;
            _dome.leftCalibration = leftCalibration;
            _dome.rightCalibration = rightCalibration;
            _dome.leftUVRect = new Vector4(0f, 0f, 0.5f, 1f);    // SBS 左半 → 左眼
            _dome.rightUVRect = new Vector4(0.5f, 0f, 0.5f, 1f); // SBS 右半 → 右眼
            _dome.flipV = 1f;
            _dome.coverageDeg = coverageDeg; _dome.radius = radius; _dome.segments = 64;
            _dome.Initialize();
            _dome.PushParameters();   // 纹理到来后再 push 一次

            _servo = gameObject.AddComponent<RobotHeadPoseDriver>();
            _servo.robotHeadAnchor = _anchor;
            _servo.followLocalHead = enableGazeServo;
            _servo.rateDegPerSec = servoRateDegPerSec;
            _servo.deadzoneDeg = servoDeadzoneDeg;
            _servo.returnDeg = servoReturnDeg;

            _source = Source ?? (useRealWebRtc
                ? (IWebRtcVideoSource)new UnityWebRtcVideoSource(new WebSocketSignaling(), signalingUrl, this)
                : new FakeStereoVideoSource());
            _source.Start();
            var pump = _source.GetRenderPump();
            if (pump != null) StartCoroutine(pump);   // com.unity.webrtc 的 WebRTC.Update()
        }

        private void Update()
        {
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

            _source?.Tick();

            // 帧纹理到来/更换 → 绑到穹顶（同一 SBS 纹理喂左右眼，UV 分半）
            var frame = _source?.Frame;
            if (frame != null && frame != _lastFrame && _dome != null)
            {
                _dome.leftTex = frame; _dome.rightTex = frame;
                _dome.PushParameters();
                _lastFrame = frame;
            }

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
            try { _source?.Stop(); } catch { }
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
            try { _source?.Stop(); } catch { }
        }
    }
}
