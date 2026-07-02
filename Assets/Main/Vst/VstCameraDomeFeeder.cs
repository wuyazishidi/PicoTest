// Assets/Main/Vst/VstCameraDomeFeeder.cs
using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using Unity.XR.PICO.TOBSupport;
using UnityEngine;
using UnityEngine.XR;
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
        public int width = 2560, height = 960, fps = 30;
        [Header("穹顶覆盖角 / 半径")]
        public float coverageDeg = 150f;
        public float radius = 20f;
        [Tooltip("边缘羽化角(度)：低头等越过穹顶边缘时，硬边圆弧柔化渐隐到透视")]
        public float edgeFeatherDeg = 12f;
        [Header("低速云台伺服（混合转向慢分量：转头超死区才低速插值回中）")]
        public bool enableGazeServo = true;
        public float servoRateDegPerSec = 30f;   // 跟随速度（度/秒）
        public float servoDeadzoneDeg = 50f;      // 触发死区半角：±50 内自由环顾，超出才跟随
        public float servoReturnDeg = 20f;        // 停靠残留半角：触发后穹顶把画面带到离正前 20° 才停（黑边更少、看得更全）
        [Header("透视（VST passthrough）")]
        public bool enableSeeThrough = true;      // 启动即开系统透视；穹顶外的区域显示真实环境而非黑边
        [Header("退出（按 B：关相机服务 → 5s → 退程序）")]
        public bool quitOnButtonB = true;
        public float quitCameraCloseDelaySec = 5f;

        private FisheyeDomeRenderer _dome;
        private Transform _anchor;
        private Camera _xrCam;
        private Texture2D _tex;
        private RobotHeadPoseDriver _servo;
        private bool _bPrev;       // B 键上升沿检测
        private bool _quitting;    // 退出流程进行中

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

            // 启动即开系统透视（VST passthrough）：穹顶覆盖角外显示真实环境而非黑
            if (enableSeeThrough) EnableVideoSeeThrough();

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
            _dome.edgeFeatherDeg = edgeFeatherDeg;
            _dome.Initialize();
            _dome.PushParameters();

            // 低速云台伺服：转头超死区才把穹顶锚点偏航低速插值跟到头朝向（保居中、可看 FOV 外）
            _servo = gameObject.AddComponent<RobotHeadPoseDriver>();
            _servo.robotHeadAnchor = _anchor;
            _servo.followLocalHead = enableGazeServo;
            _servo.rateDegPerSec = servoRateDegPerSec;
            _servo.deadzoneDeg = servoDeadzoneDeg;
            _servo.returnDeg = servoReturnDeg;

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
            // 退出：按右手柄 B（secondaryButton）→ 关相机服务 → 5s → 退程序
            if (quitOnButtonB && !_quitting)
            {
                var rh = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                if (rh.TryGetFeatureValue(CommonUsages.secondaryButton, out bool bNow))
                {
                    if (bNow && !_bPrev) StartQuit();
                    _bPrev = bNow;
                }
            }
            if (_quitting) return; // 退出流程中：停止相机泵与纹理上传

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
                    // 透视开启时清成透明（alpha=0）→ 合成器在穹顶未覆盖处显示真实环境；否则黑底
                    _xrCam.backgroundColor = enableSeeThrough ? new Color(0f, 0f, 0f, 0f) : Color.black;
                }
            }
            if (_xrCam != null && _anchor != null)
                _anchor.position = _xrCam.transform.position;
        }

        /// <summary>
        /// 反射开启系统透视。本机为 OpenXR 后端 → PassthroughFeature 才有效；PXR_Manager 仅传统 PXR 后端有效。
        /// 两套都试一遍（哪个后端就哪个生效），避免对 XR 程序集的编译期依赖。
        /// </summary>
        private static void EnableVideoSeeThrough()
        {
            string[] typeNames =
            {
                "Unity.XR.OpenXR.Features.PICOSupport.PassthroughFeature", // OpenXR 后端（本机）
                "Unity.XR.PXR.PXR_Manager",                                // 传统 PXR 后端
            };
            bool any = false;
            foreach (var tn in typeNames)
            {
                try
                {
                    var t = FindType(tn);
                    if (t == null) continue;
                    var prop = t.GetProperty("EnableVideoSeeThrough", BindingFlags.Public | BindingFlags.Static);
                    if (prop != null && prop.CanWrite) { prop.SetValue(null, true); any = true; Debug.Log($"[VstFeeder] 透视已开启 via {tn} (property)"); continue; }
                    var field = t.GetField("EnableVideoSeeThrough", BindingFlags.Public | BindingFlags.Static);
                    if (field != null) { field.SetValue(null, true); any = true; Debug.Log($"[VstFeeder] 透视已开启 via {tn} (field)"); }
                }
                catch (Exception e) { Debug.LogWarning($"[VstFeeder] 设 {tn} 透视失败: {e.Message}"); }
            }
            if (!any) Debug.LogWarning("[VstFeeder] 未找到透视 API（PassthroughFeature / PXR_Manager），透视未开启");
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
            Debug.Log($"[VstFeeder] B 键退出：关闭相机服务，{quitCameraCloseDelaySec:F0}s 后退出程序");
            StartCoroutine(QuitAfterCameraClose());
        }

        // 退出路径参考 YC-Ego（ControllerInput.cs）：关相机+解绑 → 等待 → killProcess。
        // 不用 Application.Quit()：它会让 OpenXR Shutdown SIGSEGV 并残留 pxrcaptureservice Binder
        // → 下次启动崩溃。killProcess(myPid) 是 YC-Ego 验证过的安全退出路径。
        private IEnumerator QuitAfterCameraClose()
        {
            VstCamera.OnFrame -= OnFrame;
            VstCamera.Shutdown();                                        // 关相机 + 解绑 Enterprise（CloseCamerafor4U + UnBind）
            yield return new WaitForSeconds(quitCameraCloseDelaySec);    // 等 close+unbind 往返完成，避免残留 Binder
            Debug.Log("[VstFeeder] 退出程序（killProcess）");
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var proc = new AndroidJavaClass("android.os.Process");
                proc.CallStatic("killProcess", proc.CallStatic<int>("myPid"));
            }
            catch (Exception e) { Debug.LogWarning($"[VstFeeder] killProcess 失败: {e.Message}"); }
#elif UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
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
