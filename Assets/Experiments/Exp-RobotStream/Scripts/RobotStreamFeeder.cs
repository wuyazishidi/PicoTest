// Assets/Experiments/Exp-RobotStream/Scripts/RobotStreamFeeder.cs
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using PicoTest.Core.Rendering;
using PicoTest.Experiments.WebRTC;
using PicoTest.Experiments.WebRTC.Signaling;
using PicoTest.Rendering;
using UnityEngine;
using UnityEngine.XR;

namespace PicoTest.Experiments.RobotStream
{
    /// <summary>
    /// Robot Stream Demo：用 VstPassthroughDemo 的显示方案跑 WebRTC 传输，机器人画面预演。
    /// 帧源 = Exp-WebRTC 的 IWebRtcVideoSource（真机器人 = 换实现，feeder 不改）；
    /// 标定 = Pico cam_calib.json 经 RobotCalib/ImuCamRig 分离出正常参数；
    /// 显示 = 穹顶 + capture 位姿补偿 + cmd 调参 + A 键对比原生透视 + B 退出（照搬 VstPassthrough）。
    /// 面向真机器人的位姿 seam：PushRobotPose(ts,pos,rot)（将来 WebRTC DataChannel 每帧位姿即调它）。
    /// 另起炉灶：不改 Exp-WebRTC / Exp-VstPassthrough / Main，仅引用复用。
    /// </summary>
    public sealed class RobotStreamFeeder : MonoBehaviour
    {
        [Header("回退标定（读不到 StreamingAssets/cam_calib.json 时用，外参单位阵）")]
        public FisheyeCalibration fallbackLeft, fallbackRight;
        [Header("视频源")]
        public bool useRealWebRtc = true;                       // false=假帧源（编辑器冒烟）
        public string signalingUrl = "ws://127.0.0.1:8765";    // PC 环回默认；PICO 走 LAN 改成 PC 的 IP
        [Header("穹顶（对齐优先：radius≈作业距离量级，真机 cmd 调）")]
        public float coverageDeg = 146f;
        public float radius = 2.0f;
        public float edgeFeatherDeg = 8f;
        [Header("画面朝向（WebRTC 视频纹理正立→flipV=0；Pico raw 缓冲才需 1）")]
        [Range(0, 1)] public float flipV = 0f;
        [Range(0, 1)] public float mirror = 0f;
        [Header("位姿模式：worldlocked=静止机器人最优（默认）；captureproxy=用头位姿演练 capture 补偿")]
        public PoseMode poseMode = PoseMode.WorldLocked;
        [Tooltip("capture 回溯延迟(ms)：采集→显示估计延迟，真机 cmd 调")]
        public float latencyMs = 120f;
        [Header("外参：false=单位阵（对照）")]
        public bool useCalibExtrinsics = true;
        [Header("退出（按 B：停源 → 5s → 退程序）")]
        public bool quitOnButtonB = true;
        public float quitDelaySec = 5f;
        [Header("透视（VST passthrough）")]
        public bool enableSeeThrough = true;

        public enum PoseMode { WorldLocked, CaptureProxy, External }

        /// <summary>外部注入的视频源；为空则按 useRealWebRtc 自建。</summary>
        public IWebRtcVideoSource Source { get; set; }
        public Texture Frame => _source?.Frame;

        private FisheyeDomeRenderer _dome;
        private Transform _anchor;
        private Camera _xrCam;
        private IWebRtcVideoSource _source;
        private Texture _lastFrame;
        private FisheyeCalibration _calibL, _calibR;
        private Quaternion _extL = Quaternion.identity, _extR = Quaternion.identity;
        private bool _hasRigExtrinsics;
        private TextMesh _hud;
        private bool _hudOn = true, _domeOn = true;
        private bool _aPrev, _bPrev, _quitting, _pumpStarted;
        private float _nextCmdPoll, _nextHudRefresh;
        private int _framesThisSec; private float _fpsWindowStart, _measuredFps;

        // 头位姿环形缓冲（captureproxy 回溯用），~2s @ 72Hz
        private const int PoseCap = 256;
        private readonly float[] _poseT = new float[PoseCap];
        private readonly Vector3[] _posePos = new Vector3[PoseCap];
        private readonly Quaternion[] _poseRot = new Quaternion[PoseCap];
        private int _poseHead, _poseCount;
        private bool _anchorInitialized;

        // 外部机器人位姿最新值（PushRobotPose 写；External 模式用）
        private Vector3 _extPos; private Quaternion _extRot = Quaternion.identity;
        private bool _hasExtPose;

        private IEnumerator Start()
        {
            if (enableSeeThrough) EnableVideoSeeThrough();

            yield return LoadCalibration();
            if (_calibL == null || _calibR == null)
            {
                Debug.LogError("[RobotStream] 无标定可用（json 读取失败且未指回退资产），停止。");
                yield break;
            }

            _anchor = new GameObject("RobotDomeAnchor").transform;
            _anchor.SetParent(transform, false);
            _dome = gameObject.AddComponent<FisheyeDomeRenderer>();
            _dome.frame = FisheyeDomeRenderer.RenderFrame.WorldLocked; // 锚点姿态我们自己驱动
            _dome.robotHeadAnchor = _anchor;
            _dome.leftCalibration = _calibL;
            _dome.rightCalibration = _calibR;
            _dome.leftUVRect = new Vector4(0f, 0f, 0.5f, 1f);    // SBS 左半 → 左眼
            _dome.rightUVRect = new Vector4(0.5f, 0f, 0.5f, 1f); // SBS 右半 → 右眼
            _dome.flipV = flipV;
            _dome.mirror = mirror;
            _dome.coverageDeg = coverageDeg; _dome.radius = radius; _dome.segments = 64;
            _dome.edgeFeatherDeg = edgeFeatherDeg;
            _dome.Initialize();
            _dome.PushParameters();

            CreateHud();

            ResolveSignalingUrl();   // 运行时覆盖：files/robotstream/signaling.txt 优先于场景默认（免重打包换 IP）
            StartSource(Source);

            Debug.Log($"[RobotStream] 启动：src={(useRealWebRtc ? "webrtc" : "fake")} signaling={signalingUrl} mode={poseMode} " +
                      $"radius={radius} latency={latencyMs}ms ext={(useCalibExtrinsics && _hasRigExtrinsics ? "calib" : "id")}");
        }

        /// <summary>启动/切换视频源；WebRTC 渲染泵（全局 WebRTC.Update()）只驱动一次。</summary>
        private void StartSource(IWebRtcVideoSource injected)
        {
            _source = injected ?? (useRealWebRtc
                ? (IWebRtcVideoSource)new UnityWebRtcVideoSource(new WebSocketSignaling(), signalingUrl, this)
                : new FakeStereoVideoSource());
            _source.Start();
            var pump = _source.GetRenderPump();
            if (pump != null && !_pumpStarted) { StartCoroutine(pump); _pumpStarted = true; }
        }

        /// <summary>
        /// 信令地址运行时覆盖：读 persistentDataPath/robotstream/signaling.txt（一行 ws://ip:port），
        /// 有内容即覆盖场景里编译进来的默认值——真机换 PC/换 IP 只需 adb push，不用重打包。
        /// </summary>
        private void ResolveSignalingUrl()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "robotstream", "signaling.txt");
                if (!File.Exists(path)) return;
                string u = File.ReadAllText(path).Trim();
                if (!string.IsNullOrEmpty(u)) { signalingUrl = u; Debug.Log($"[RobotStream] 信令地址覆盖自 signaling.txt → {u}"); }
            }
            catch (Exception e) { Debug.LogWarning($"[RobotStream] 读 signaling.txt 失败: {e.Message}"); }
        }

        /// <summary>cmd `signaling <url>`：停旧源、以新地址重连（免重启应用）。</summary>
        private void ReconnectSignaling(string url)
        {
            signalingUrl = url;
            useRealWebRtc = true;
            try { _source?.Stop(); } catch { }
            _lastFrame = null;
            Source = null;
            StartSource(null);
            Debug.Log($"[RobotStream] 重连信令 → {url}");
        }

        /// <summary>
        /// 标定加载：优先 StreamingAssets/cam_calib.json（Pico 参数当机器人相机，经 RobotCalib/ImuCamRig）；
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
                else Debug.LogWarning($"[RobotStream] cam_calib.json 读取失败：{req.error}");
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
                    var (l, r) = RobotCalib.BuildEyeCalibrations(calib, useExtrinsics: true);
                    _extL = l.extrinsicRotation; _extR = r.extrinsicRotation;
                    _hasRigExtrinsics = true;
                    if (!useCalibExtrinsics) { l.extrinsicRotation = Quaternion.identity; r.extrinsicRotation = Quaternion.identity; }
                    _calibL = l; _calibR = r;
                    Debug.Log($"[RobotStream] 标定就绪（Pico 参数当机器人相机）：{calib.Width}x{calib.Height} " +
                              $"baseline={calib.StereoBaselineM * 1000:F1}mm ext={(useCalibExtrinsics ? "calib" : "id")}");
                    yield break;
                }
                catch (Exception e) { Debug.LogWarning($"[RobotStream] cam_calib.json 解析/换算失败：{e.Message}"); }
            }
            _calibL = fallbackLeft; _calibR = fallbackRight;
            _hasRigExtrinsics = false;
            Debug.LogWarning("[RobotStream] 回退 Inspector 标定资产（外参单位阵）");
        }

        /// <summary>
        /// 面向真机器人的位姿 seam：将来 WebRTC DataChannel 收到机器人每帧位姿即调它
        /// （tsSec 用 Time.unscaledTime 同一时钟，或经换算）。一旦被调用即切 External capture 模式。
        /// </summary>
        public void PushRobotPose(float tsSec, Vector3 pos, Quaternion rot)
        {
            _extPos = pos; _extRot = rot; _hasExtPose = true;
            if (poseMode != PoseMode.External) poseMode = PoseMode.External;
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

            _source?.Tick();
            EnsureXrCam();
            RecordHeadPose();
            PollCmdFile();

            // 帧纹理到来/更换 → 绑穹顶（同一 SBS 纹理喂左右眼，UV 分半）
            var frame = _source?.Frame;
            bool newFrame = frame != null && frame != _lastFrame;
            if (newFrame && _dome != null)
            {
                _dome.leftTex = frame; _dome.rightTex = frame;
                _dome.PushParameters();
                _lastFrame = frame;
                _framesThisSec++;
            }
            if (Time.unscaledTime - _fpsWindowStart >= 1f)
            {
                _measuredFps = _framesThisSec / Mathf.Max(1e-3f, Time.unscaledTime - _fpsWindowStart);
                _framesThisSec = 0; _fpsWindowStart = Time.unscaledTime;
            }

            UpdateAnchor(newFrame);
            UpdateHud();
        }

        private void EnsureXrCam()
        {
            if (_xrCam != null) return;
            _xrCam = Camera.main;
            if (_xrCam != null)
            {
                _xrCam.clearFlags = CameraClearFlags.SolidColor;
                _xrCam.backgroundColor = enableSeeThrough ? new Color(0f, 0f, 0f, 0f) : Color.black;
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
        /// 穹顶锚点姿态：
        /// - WorldLocked：位置跟眼、朝向世界锁（静止机器人相机最优，转头零延迟环顾已收画面）。
        /// - CaptureProxy：新帧到达时锚到 (now−latency) 头位姿（演练 VstPassthrough capture 补偿）。
        /// - External：锚到 PushRobotPose 的最新机器人位姿（真机器人路径）。
        /// </summary>
        private void UpdateAnchor(bool newFrameArrived)
        {
            if (_xrCam == null || _anchor == null) return;
            switch (poseMode)
            {
                case PoseMode.WorldLocked:
                    _anchor.SetPositionAndRotation(_xrCam.transform.position, Quaternion.identity);
                    break;
                case PoseMode.CaptureProxy:
                    if (newFrameArrived || !_anchorInitialized)
                        if (LookupPose(Time.unscaledTime - latencyMs * 0.001f, out var p, out var r))
                        { _anchor.SetPositionAndRotation(p, r); _anchorInitialized = true; }
                    break;
                case PoseMode.External:
                    // 位置仍跟眼（罩住观看者），朝向用机器人位姿；有真平移数据时可换成 _extPos
                    if (_hasExtPose) _anchor.SetPositionAndRotation(_xrCam.transform.position, _extRot);
                    break;
            }
        }

        private bool LookupPose(float t, out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = Quaternion.identity;
            if (_poseCount == 0) return false;
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

        // ── adb 调参通道（照搬 VstPassthrough cmd.txt 模式）─────────────────
        // adb shell "echo mode captureproxy > /sdcard/Android/data/<pkg>/files/robotstream/cmd.txt"
        // 命令：signaling <ws://ip:port>（热重连）| radius <m> | latency <ms> | mode worldlocked|captureproxy
        //       | ext calib|id | dome on|off | flip 0|1 | cover <deg> | feather <deg> | hud on|off | dump

        private void PollCmdFile()
        {
            if (Time.unscaledTime < _nextCmdPoll) return;
            _nextCmdPoll = Time.unscaledTime + 1f;
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "robotstream", "cmd.txt");
                if (!File.Exists(path)) return;
                string raw = File.ReadAllText(path).Trim();
                File.Delete(path);
                if (raw.Length == 0) return;
                Debug.Log($"[RobotStream] cmd.txt → \"{raw}\"");
                foreach (var line in raw.Split('\n')) ExecCmd(line.Trim());
            }
            catch (Exception e) { Debug.LogWarning($"[RobotStream] cmd.txt 处理失败: {e.Message}"); }
        }

        private void ExecCmd(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return;
            var parts = cmd.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string arg = parts.Length > 1 ? parts[1] : null;   // 原样保留大小写（URL 用）
            switch (parts[0].ToLowerInvariant())
            {
                case "signaling" when !string.IsNullOrEmpty(arg):
                    ReconnectSignaling(arg); break;
                case "radius" when TryF(arg, out float r):
                    radius = Mathf.Clamp(r, 0.3f, 50f);
                    _dome.radius = radius;
                    if (_dome.DomeTransform != null) _dome.DomeTransform.localScale = Vector3.one * radius;
                    break;
                case "latency" when TryF(arg, out float ms):
                    latencyMs = Mathf.Clamp(ms, 0f, 500f); break;
                case "mode":
                    poseMode = arg == "captureproxy" ? PoseMode.CaptureProxy
                             : arg == "external" ? PoseMode.External : PoseMode.WorldLocked;
                    _anchorInitialized = false;
                    break;
                case "ext":
                    useCalibExtrinsics = arg != "id";
                    ApplyExtrinsics();
                    break;
                case "dome":
                    SetDomeVisible(arg != "off"); break;
                case "flip" when TryF(arg, out float fv):
                    flipV = Mathf.Clamp01(fv); _dome.flipV = flipV; _dome.PushParameters(); break;
                case "cover" when TryF(arg, out float cov):
                    coverageDeg = Mathf.Clamp(cov, 60f, 220f);
                    _dome.coverageDeg = coverageDeg; _dome.PushParameters();
                    Debug.Log("[RobotStream] cover 只更新 thetaMax（裁剪），网格弧度下次启动生效");
                    break;
                case "feather" when TryF(arg, out float f):
                    edgeFeatherDeg = Mathf.Clamp(f, 0f, 45f);
                    _dome.edgeFeatherDeg = edgeFeatherDeg; _dome.PushParameters(); break;
                case "hud":
                    _hudOn = arg != "off";
                    if (_hud != null) _hud.gameObject.SetActive(_hudOn); break;
                case "dump":
                    Debug.Log($"[RobotStream] dump: src={(useRealWebRtc ? "webrtc" : "fake")} signaling={signalingUrl} mode={poseMode} " +
                              $"radius={radius} latency={latencyMs} ext={(useCalibExtrinsics && _hasRigExtrinsics ? "calib" : "id")} " +
                              $"dome={_domeOn} flip={flipV} cover={coverageDeg} fps={_measuredFps:F1} frame={(_lastFrame != null)}");
                    break;
                default:
                    Debug.LogWarning($"[RobotStream] 未知命令：{cmd}"); break;
            }
        }

        private static bool TryF(string s, out float v) =>
            float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v);

        private void ApplyExtrinsics()
        {
            if (_calibL == null || _calibR == null) return;
            bool calib = useCalibExtrinsics && _hasRigExtrinsics;
            _calibL.extrinsicRotation = calib ? _extL : Quaternion.identity;
            _calibR.extrinsicRotation = calib ? _extR : Quaternion.identity;
            _dome.PushParameters();
            Debug.Log($"[RobotStream] 外参 → {(calib ? "calib" : "id")}");
        }

        private void SetDomeVisible(bool on)
        {
            _domeOn = on;
            if (_dome != null && _dome.DomeRenderer != null) _dome.DomeRenderer.enabled = on;
            Debug.Log($"[RobotStream] 穹顶 {(on ? "显示" : "隐藏 → 原生透视对比")}");
        }

        // ── HUD ───────────────────────────────────────────────────────

        private void CreateHud()
        {
            var go = new GameObject("RobotStreamHud");
            go.transform.SetParent(transform, false);
            _hud = go.AddComponent<TextMesh>();
            _hud.fontSize = 48;
            _hud.characterSize = 0.01f;
            _hud.anchor = TextAnchor.UpperLeft;
            _hud.color = new Color(0.4f, 0.9f, 1f, 0.9f);
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
            _hud.text = $"RobotStream  src={(useRealWebRtc ? "webrtc" : "fake")} {_measuredFps:F0}fps frame={(_lastFrame != null ? "yes" : "--")}\n" +
                        $"mode={poseMode}  latency={latencyMs:F0}ms  radius={radius:F2}m\n" +
                        $"ext={(useCalibExtrinsics && _hasRigExtrinsics ? "calib" : "id")}  dome={(_domeOn ? "on" : "OFF(native)")}\n" +
                        $"A=对比原生透视  B=退出  cmd: files/robotstream/cmd.txt";
        }

        // ── 透视 / 退出（照搬 VstPassthrough 经验证路径）──────────────────

        private static void EnableVideoSeeThrough()
        {
            string[] typeNames =
            {
                "Unity.XR.PXR.PXR_Manager",
                "Unity.XR.OpenXR.Features.PICOSupport.PassthroughFeature",
            };
            bool any = false;
            foreach (var tn in typeNames)
            {
                try
                {
                    var ty = FindType(tn);
                    if (ty == null) continue;
                    var prop = ty.GetProperty("EnableVideoSeeThrough", BindingFlags.Public | BindingFlags.Static);
                    if (prop != null && prop.CanWrite) { prop.SetValue(null, true); any = true; Debug.Log($"[RobotStream] 透视已开启 via {tn}"); continue; }
                    var field = ty.GetField("EnableVideoSeeThrough", BindingFlags.Public | BindingFlags.Static);
                    if (field != null) { field.SetValue(null, true); any = true; Debug.Log($"[RobotStream] 透视已开启 via {tn}"); }
                }
                catch (Exception e) { Debug.LogWarning($"[RobotStream] 设 {tn} 透视失败: {e.Message}"); }
            }
            if (!any) Debug.LogWarning("[RobotStream] 未找到透视 API，透视未开启");
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
            Debug.Log($"[RobotStream] B 键退出：停源，{quitDelaySec:F0}s 后退出程序");
            StartCoroutine(QuitAfter());
        }

        private IEnumerator QuitAfter()
        {
            try { _source?.Stop(); } catch { }
            yield return new WaitForSeconds(quitDelaySec);
            Debug.Log("[RobotStream] 退出程序（killProcess）");
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var proc = new AndroidJavaClass("android.os.Process");
                proc.CallStatic("killProcess", proc.CallStatic<int>("myPid"));
            }
            catch (Exception e) { Debug.LogWarning($"[RobotStream] killProcess 失败: {e.Message}"); }
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
