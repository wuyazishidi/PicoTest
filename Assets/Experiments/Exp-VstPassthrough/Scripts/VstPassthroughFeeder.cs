// Assets/Experiments/Exp-VstPassthrough/Scripts/VstPassthroughFeeder.cs
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using PicoTest.Core.Rendering;
using PicoTest.Rendering;
using PicoTest.Vst;
using Unity.XR.PICO.TOBSupport;
using UnityEngine;
using UnityEngine.XR;

namespace PicoTest.Experiments.VstPassthrough
{
    /// <summary>
    /// VST Passthrough Demo：用自有"VST raw 鱼眼 → 穹顶"管线复现 PICO 原生透视的对齐效果。
    /// 与 FisheyeDomeXRLive（XRLiveDemo）的差异（另起炉灶，不改动它）：
    /// ① 头锁定（无云台伺服）；② T_imu_to_cam 外参进 shader（ImuCamRig 自标定换算）；
    /// ③ capture 模式：穹顶锚到"捕获时刻头位姿"（环形缓冲回溯 latencyMs），转头画面世界稳定；
    /// ④ adb cmd.txt 运行时调参 + A 键隐藏穹顶 ⇄ 原生透视 A/B 对比。
    /// 帧泵/双缓冲/透视开启/B 键退出照抄 VstCameraDomeFeeder（Main 不动，实验内复制）。
    /// 仅真机有效（Enterprise 相机需 PICO 4U + 激活）。
    /// </summary>
    public sealed class VstPassthroughFeeder : MonoBehaviour
    {
        [Header("回退标定（读不到 StreamingAssets/cam_calib.json 时用，外参单位阵）")]
        public FisheyeCalibration fallbackLeft, fallbackRight;
        [Header("分辨率 / fps")]
        public int width = 2560, height = 960, fps = 30;
        [Header("穹顶（对齐优先：radius≈原生透视重投影距离量级，真机 cmd 调）")]
        public float coverageDeg = 146f;
        public float radius = 1.5f;
        public float edgeFeatherDeg = 8f;
        [Header("锚定模式：capture=捕获时刻头位姿（默认）；false=朴素头锁定")]
        public bool capturePoseMode = true;
        [Tooltip("采集→显示估计延迟(ms)，capture 模式回溯头位姿用；真机 cmd 调")]
        public float latencyMs = 80f;
        [Header("外参：false=单位阵（对照）")]
        public bool useCalibExtrinsics = true;
        [Header("退出（按 B：关相机服务 → 5s → 退程序）")]
        public bool quitOnButtonB = true;
        public float quitCameraCloseDelaySec = 5f;

        private FisheyeDomeRenderer _dome;
        private Transform _anchor;
        private Camera _xrCam;
        private Texture2D _tex;
        private FisheyeCalibration _calibL, _calibR;   // 运行时实例（含外参）
        private Quaternion _extL, _extR;               // ImuCamRig 换算的外参（useCalibExtrinsics 切换用）
        private bool _hasRigExtrinsics;
        private TextMesh _hud;
        private bool _hudOn = true;
        private bool _domeOn = true;
        private bool _aPrev, _bPrev, _quitting;
        private float _nextCmdPoll, _nextHudRefresh;
        private int _framesThisSec; private float _fpsWindowStart; private float _measuredFps;

        // 双缓冲：原生线程写 _back，主线程读 _front
        private byte[] _front, _back;
        private bool _newFrame;
        private readonly object _swapLock = new object();

        // 头位姿环形缓冲（capture 模式回溯用），~2s @ 72Hz
        private const int PoseCap = 256;
        private readonly float[] _poseT = new float[PoseCap];
        private readonly Vector3[] _posePos = new Vector3[PoseCap];
        private readonly Quaternion[] _poseRot = new Quaternion[PoseCap];
        private int _poseHead, _poseCount;

        private IEnumerator Start()
        {
            EnableVideoSeeThrough();

            yield return LoadCalibration();
            if (_calibL == null || _calibR == null)
            {
                Debug.LogError("[VstPT] 无标定可用（json 读取失败且未指回退资产），停止。");
                yield break;
            }

            _front = new byte[width * height * 4];
            _back = new byte[width * height * 4];
            _tex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

            // 穹顶挂在锚点下；锚点每帧由 UpdateAnchor 按模式设为头位姿（当前或捕获时刻）
            _anchor = new GameObject("PassthroughAnchor").transform;
            _anchor.SetParent(transform, false);
            _dome = gameObject.AddComponent<FisheyeDomeRenderer>();
            _dome.frame = FisheyeDomeRenderer.RenderFrame.WorldLocked; // 挂锚点下，锚点姿态我们自己驱动
            _dome.robotHeadAnchor = _anchor;
            _dome.leftCalibration = _calibL;
            _dome.rightCalibration = _calibR;
            _dome.leftTex = _tex; _dome.rightTex = _tex;
            _dome.leftUVRect = new Vector4(0f, 0f, 0.5f, 1f);
            _dome.rightUVRect = new Vector4(0.5f, 0f, 0.5f, 1f);
            _dome.flipV = 1f;   // 相机缓冲 top-down
            _dome.coverageDeg = coverageDeg; _dome.radius = radius; _dome.segments = 64;
            _dome.edgeFeatherDeg = edgeFeatherDeg;
            _dome.Initialize();
            _dome.PushParameters();

            CreateHud();

            VstCamera.OnFrame += OnFrame;
            VstCamera.Configure(width, height, fps);
            VstCamera.Initialize();
            Debug.Log($"[VstPT] 启动：mode={(capturePoseMode ? "capture" : "head")} radius={radius} latency={latencyMs}ms ext={(useCalibExtrinsics && _hasRigExtrinsics ? "calib" : "id")}");
        }

        /// <summary>
        /// 标定加载：优先 StreamingAssets/cam_calib.json（含 T_imu_to_cam → ImuCamRig 换算外参）；
        /// 失败回退 Inspector 资产 + 单位阵外参。Android 的 StreamingAssets 在 apk 内，须走 UnityWebRequest。
        /// </summary>
        private IEnumerator LoadCalibration()
        {
            string url = Path.Combine(Application.streamingAssetsPath, "cam_calib.json");
            string json = null;
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var req = UnityEngine.Networking.UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                    json = req.downloadHandler.text;
                else Debug.LogWarning($"[VstPT] cam_calib.json 读取失败：{req.error}");
            }
#else
            if (File.Exists(url)) json = File.ReadAllText(url);
            yield return null;
#endif
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var calib = CamCalib.Parse(json);
                    var rig = ImuCamRig.FromCalib(calib);
                    _extL = ToQuaternion(rig.LeftREye);
                    _extR = ToQuaternion(rig.RightREye);
                    _hasRigExtrinsics = true;
                    _calibL = MakeCalib("RigLeft", calib, Eye.Left, useCalibExtrinsics ? _extL : Quaternion.identity);
                    _calibR = MakeCalib("RigRight", calib, Eye.Right, useCalibExtrinsics ? _extR : Quaternion.identity);
                    Debug.Log($"[VstPT] 标定就绪：T 判读={rig.TReading} 共线度={rig.ConsistencyScore:F4} " +
                              $"基线={rig.BaselineM * 1000:F1}mm 左相机头系位置=({rig.LeftCamPosHead[0]:F3},{rig.LeftCamPosHead[1]:F3},{rig.LeftCamPosHead[2]:F3})");
                    yield break;
                }
                catch (Exception e) { Debug.LogWarning($"[VstPT] cam_calib.json 解析/换算失败：{e.Message}"); }
            }
            _calibL = fallbackLeft; _calibR = fallbackRight;
            _hasRigExtrinsics = false;
            Debug.LogWarning("[VstPT] 回退 Inspector 标定资产（外参单位阵）");
        }

        private static FisheyeCalibration MakeCalib(string name, CamCalib c, Eye eye, Quaternion ext)
        {
            var e = c.Get(eye);
            var so = ScriptableObject.CreateInstance<FisheyeCalibration>();
            so.name = name;
            so.fx = (float)e.Fx; so.fy = (float)e.Fy; so.cx = (float)e.Cx; so.cy = (float)e.Cy;
            so.k1 = (float)e.D[0]; so.k2 = (float)e.D[1]; so.k3 = (float)e.D[2];
            so.k4 = (float)e.D[3]; so.k5 = (float)e.D[4]; so.k6 = (float)e.D[5];
            so.width = c.Width; so.height = c.Height;
            so.extrinsicRotation = ext;
            return so;
        }

        /// <summary>行主序 3x3（列 = 基向量像）→ Quaternion；R_eye 为真旋转（det=+1，ImuCamRig 保证）。</summary>
        private static Quaternion ToQuaternion(double[] r)
        {
            var fwd = new Vector3((float)r[2], (float)r[5], (float)r[8]); // R·ẑ
            var up = new Vector3((float)r[1], (float)r[4], (float)r[7]);  // R·ŷ
            return Quaternion.LookRotation(fwd, up);
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
            if (quitOnButtonB && !_quitting)
            {
                var rh = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                if (rh.TryGetFeatureValue(CommonUsages.secondaryButton, out bool bNow))
                {
                    if (bNow && !_bPrev) StartQuit();
                    _bPrev = bNow;
                }
                if (rh.TryGetFeatureValue(CommonUsages.primaryButton, out bool aNow))
                {
                    if (aNow && !_aPrev) SetDomeVisible(!_domeOn);   // A 键：A/B 对比原生透视
                    _aPrev = aNow;
                }
            }
            if (_quitting) return;

            VstCamera.PumpFromMain(); // 必须每帧泵（PICO 崩溃规避）

            EnsureXrCam();
            RecordHeadPose();
            PollCmdFile();

            bool uploaded = false;
            if (_newFrame && _tex != null)
            {
                lock (_swapLock)
                {
                    _tex.LoadRawTextureData(_front);
                    _newFrame = false;
                }
                _tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                uploaded = true;
                _framesThisSec++;
            }
            if (Time.unscaledTime - _fpsWindowStart >= 1f)
            {
                _measuredFps = _framesThisSec / (Time.unscaledTime - _fpsWindowStart);
                _framesThisSec = 0; _fpsWindowStart = Time.unscaledTime;
            }

            UpdateAnchor(uploaded);
            UpdateHud();
        }

        private void EnsureXrCam()
        {
            if (_xrCam != null) return;
            _xrCam = Camera.main;
            if (_xrCam != null)
            {
                _xrCam.clearFlags = CameraClearFlags.SolidColor;
                _xrCam.backgroundColor = new Color(0f, 0f, 0f, 0f); // 透明 → 穹顶外露原生透视
            }
        }

        private void RecordHeadPose()
        {
            if (_xrCam == null) return;
            _poseT[_poseHead] = Time.unscaledTime;
            _posePos[_poseHead] = _xrCam.transform.position;
            _poseRot[_poseHead] = _xrCam.transform.rotation;
            _poseHead = (_poseHead + 1) % PoseCap;
            if (_poseCount < PoseCap) _poseCount++;
        }

        /// <summary>
        /// capture 模式：新帧到达时把锚点设为 (now − latency) 时刻的头位姿——捕获后头的旋转
        /// 被世界锁穹顶自动补偿（近似原生透视的 late-stage reprojection，转头不拖影）。
        /// head 模式：每帧锚点=当前头位姿（朴素头锁定，作对照基线）。
        /// </summary>
        private void UpdateAnchor(bool newFrameArrived)
        {
            if (_xrCam == null || _anchor == null) return;
            if (!capturePoseMode)
            {
                _anchor.SetPositionAndRotation(_xrCam.transform.position, _xrCam.transform.rotation);
                return;
            }
            if (!newFrameArrived && _poseCount > 0 && _anchorInitialized) return; // 帧间保持世界锁
            if (LookupPose(Time.unscaledTime - latencyMs * 0.001f, out var pos, out var rot))
            {
                _anchor.SetPositionAndRotation(pos, rot);
                _anchorInitialized = true;
            }
        }
        private bool _anchorInitialized;

        private bool LookupPose(float t, out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = default;
            if (_poseCount == 0) return false;
            // 从最新往回找第一个 <= t 的样本；找不到用最老的
            int best = -1;
            for (int i = 0; i < _poseCount; i++)
            {
                int idx = (_poseHead - 1 - i + PoseCap * 2) % PoseCap;
                if (_poseT[idx] <= t) { best = idx; break; }
            }
            if (best < 0) best = (_poseHead - _poseCount + PoseCap) % PoseCap; // 最老
            pos = _posePos[best]; rot = _poseRot[best];
            return true;
        }

        // ── adb 调参通道（照抄 Exp-TrackerIMU cmd.txt 模式）────────────────
        // adb shell "echo radius 1.2 > /sdcard/Android/data/<pkg>/files/passthrough/cmd.txt"
        // 命令：radius <m> | latency <ms> | mode head|capture | ext calib|id | dome on|off
        //       | cover <deg> | feather <deg> | hud on|off | dump

        private void PollCmdFile()
        {
            if (Time.unscaledTime < _nextCmdPoll) return;
            _nextCmdPoll = Time.unscaledTime + 1f;
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "passthrough", "cmd.txt");
                if (!File.Exists(path)) return;
                string raw = File.ReadAllText(path).Trim();
                File.Delete(path);
                if (raw.Length == 0) return;
                Debug.Log($"[VstPT] cmd.txt → \"{raw}\"");
                foreach (var line in raw.Split('\n'))
                    ExecCmd(line.Trim());
            }
            catch (Exception e) { Debug.LogWarning($"[VstPT] cmd.txt 处理失败: {e.Message}"); }
        }

        private void ExecCmd(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return;
            var parts = cmd.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string arg = parts.Length > 1 ? parts[1] : null;
            switch (parts[0].ToLowerInvariant())
            {
                case "radius" when TryF(arg, out float r):
                    radius = Mathf.Clamp(r, 0.3f, 50f);
                    RebuildDomeScale();
                    break;
                case "latency" when TryF(arg, out float ms):
                    latencyMs = Mathf.Clamp(ms, 0f, 500f); break;
                case "mode":
                    capturePoseMode = arg != "head"; break;
                case "ext":
                    useCalibExtrinsics = arg != "id";
                    ApplyExtrinsics();
                    break;
                case "dome":
                    SetDomeVisible(arg != "off"); break;
                case "cover" when TryF(arg, out float cov):
                    coverageDeg = Mathf.Clamp(cov, 60f, 220f);
                    Debug.Log("[VstPT] cover 需重建穹顶网格：仅更新 thetaMax（画面裁剪），网格弧度下次启动生效");
                    _dome.coverageDeg = coverageDeg; _dome.PushParameters();
                    break;
                case "feather" when TryF(arg, out float f):
                    edgeFeatherDeg = Mathf.Clamp(f, 0f, 45f);
                    _dome.edgeFeatherDeg = edgeFeatherDeg; _dome.PushParameters();
                    break;
                case "hud":
                    _hudOn = arg != "off";
                    if (_hud != null) _hud.gameObject.SetActive(_hudOn);
                    break;
                case "dump":
                    Debug.Log($"[VstPT] dump: mode={(capturePoseMode ? "capture" : "head")} radius={radius} " +
                              $"latency={latencyMs} ext={(useCalibExtrinsics && _hasRigExtrinsics ? "calib" : "id")} " +
                              $"dome={_domeOn} cover={coverageDeg} feather={edgeFeatherDeg} fps={_measuredFps:F1}");
                    break;
                default:
                    Debug.LogWarning($"[VstPT] 未知命令：{cmd}");
                    break;
            }
        }

        private static bool TryF(string s, out float v) =>
            float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v);

        private void RebuildDomeScale()
        {
            _dome.radius = radius;
            if (_dome.DomeTransform != null) _dome.DomeTransform.localScale = Vector3.one * radius;
        }

        private void ApplyExtrinsics()
        {
            if (_calibL == null || _calibR == null) return;
            bool calib = useCalibExtrinsics && _hasRigExtrinsics;
            _calibL.extrinsicRotation = calib ? _extL : Quaternion.identity;
            _calibR.extrinsicRotation = calib ? _extR : Quaternion.identity;
            _dome.PushParameters();
            Debug.Log($"[VstPT] 外参 → {(calib ? "calib" : "id")}");
        }

        private void SetDomeVisible(bool on)
        {
            _domeOn = on;
            if (_dome != null && _dome.DomeRenderer != null) _dome.DomeRenderer.enabled = on;
            Debug.Log($"[VstPT] 穹顶 {(on ? "显示" : "隐藏 → 原生透视对比")}");
        }

        // ── HUD（跟头 2m 小字状态板；hud off 关）────────────────────────

        private void CreateHud()
        {
            var go = new GameObject("PassthroughHud");
            go.transform.SetParent(transform, false);
            _hud = go.AddComponent<TextMesh>();
            _hud.fontSize = 48;
            _hud.characterSize = 0.01f;
            _hud.anchor = TextAnchor.UpperLeft;
            _hud.color = new Color(0.3f, 1f, 0.3f, 0.9f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) { _hud.font = font; _hud.GetComponent<MeshRenderer>().material = font.material; }
        }

        private void UpdateHud()
        {
            if (_hud == null || !_hudOn || _xrCam == null) return;
            if (Time.unscaledTime < _nextHudRefresh) return;
            _nextHudRefresh = Time.unscaledTime + 0.5f;
            var t = _xrCam.transform;
            _hud.transform.position = t.position + t.forward * 2f + t.up * 0.5f - t.right * 0.4f;
            _hud.transform.rotation = Quaternion.LookRotation(_hud.transform.position - t.position);
            _hud.text = $"VstPassthrough  cam {_measuredFps:F0}fps\n" +
                        $"mode={(capturePoseMode ? "capture" : "head")}  latency={latencyMs:F0}ms\n" +
                        $"radius={radius:F2}m  ext={(useCalibExtrinsics && _hasRigExtrinsics ? "calib" : "id")}  dome={(_domeOn ? "on" : "OFF(native)")}\n" +
                        $"A=对比原生透视  B=退出  cmd: files/passthrough/cmd.txt";
        }

        // ── 透视开启 / 退出（照抄 VstCameraDomeFeeder，经验证路径）───────

        private static void EnableVideoSeeThrough()
        {
            string[] typeNames =
            {
                "Unity.XR.PXR.PXR_Manager",                                // PXR 后端（本机实测生效）
                "Unity.XR.OpenXR.Features.PICOSupport.PassthroughFeature", // OpenXR 后端（保险）
            };
            bool any = false;
            foreach (var tn in typeNames)
            {
                try
                {
                    var t = FindType(tn);
                    if (t == null) continue;
                    var prop = t.GetProperty("EnableVideoSeeThrough", BindingFlags.Public | BindingFlags.Static);
                    if (prop != null && prop.CanWrite) { prop.SetValue(null, true); any = true; Debug.Log($"[VstPT] 透视已开启 via {tn}"); continue; }
                    var field = t.GetField("EnableVideoSeeThrough", BindingFlags.Public | BindingFlags.Static);
                    if (field != null) { field.SetValue(null, true); any = true; Debug.Log($"[VstPT] 透视已开启 via {tn}"); }
                }
                catch (Exception e) { Debug.LogWarning($"[VstPT] 设 {tn} 透视失败: {e.Message}"); }
            }
            if (!any) Debug.LogWarning("[VstPT] 未找到透视 API，透视未开启");
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
            Debug.Log($"[VstPT] B 键退出：关闭相机服务，{quitCameraCloseDelaySec:F0}s 后退出程序");
            StartCoroutine(QuitAfterCameraClose());
        }

        // 不用 Application.Quit()（OpenXR Shutdown SIGSEGV + 残留 pxrcaptureservice Binder）。
        private IEnumerator QuitAfterCameraClose()
        {
            VstCamera.OnFrame -= OnFrame;
            VstCamera.Shutdown();
            yield return new WaitForSeconds(quitCameraCloseDelaySec);
            Debug.Log("[VstPT] 退出程序（killProcess）");
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var proc = new AndroidJavaClass("android.os.Process");
                proc.CallStatic("killProcess", proc.CallStatic<int>("myPid"));
            }
            catch (Exception e) { Debug.LogWarning($"[VstPT] killProcess 失败: {e.Message}"); }
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
