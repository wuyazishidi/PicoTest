// Assets/Experiments/Exp-RobotStreamLeftPreview/Scripts/RobotStreamLeftPreviewFeeder.cs
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using PicoTest.Experiments.RobotDsDome;
using PicoTest.Experiments.WebRTC;
using UnityEngine;
using UnityEngine.XR;

namespace PicoTest.Experiments.RobotStreamLeftPreview
{
    /// <summary>
    /// Robot Stream Left Preview Demo：直连 Tools/run_stereo_left_viewer.py 的 aiortc HTTP
    /// offer/answer 端点，用 VstPassthroughDemo 同款穹顶显示方案呈现机器人左目单目画面
    /// （无立体对——左右眼贴同一张纹理）。结构照抄 Exp-RobotStream 的 RobotStreamFeeder
    /// （capture 位姿补偿 + cmd 调参 + A 键对比原生透视 + B 退出 + HUD），只换视频源
    /// （HttpOfferVideoSource 而非 WebSocketSignaling 中继）与 UV（整图而非 SBS 分半）。
    /// 标定用 Exp-RobotDsDome 的 Double Sphere 模型（机器人真实相机模型，见该实验 journal 的
    /// 判定）——不再用 Pico cam_calib.json 当占位：那是等距鱼眼模型，跟真机器人光学对不上。
    /// 另起炉灶：不改 Exp-WebRTC / Exp-RobotStream / Exp-RobotDsDome / Exp-VstPassthrough /
    /// Main，仅引用复用。
    /// </summary>
    public sealed class RobotStreamLeftPreviewFeeder : MonoBehaviour
    {
        [Header("DS 标定（左目；跑 PicoTest/Robot DS Dome/Import Camchain 生成）")]
        public DsEyeCalibration calibration;
        [Header("视频源")]
        public bool useRealWebRtc = true;                                // false=假帧源（编辑器冒烟）
        public string serverUrl = "http://127.0.0.1:8888";               // run_stereo_left_viewer.py 地址
        [Header("穹顶（DS 是宽鱼眼，coverageDeg 起点比等距鱼眼大；真机 cmd 调）")]
        public float coverageDeg = 190f;
        public float radius = 2.0f;
        public float edgeFeatherDeg = 8f;
        [Header("画面朝向（真机验证：com.unity.webrtc 解码纹理是倒的→flipV=1，同源自 StereoPreview 2026-07-17 实测，未在本场景单独复测）")]
        [Range(0, 1)] public float flipV = 1f;
        [Range(0, 1)] public float mirror = 0f;
        [Header("颜色修正（源端 R/B 天生互换，见 run_stereo_left_viewer.py 文档；默认开）")]
        public bool swapRB = true;
        [Header("位姿模式：worldlocked=静止机器人最优（默认）；captureproxy=用头位姿演练 capture 补偿")]
        public PoseMode poseMode = PoseMode.WorldLocked;
        [Tooltip("capture 回溯延迟(ms)：采集→显示估计延迟，真机 cmd 调")]
        public float latencyMs = 120f;
        [Header("退出（按 B：停源 → 5s → 退程序）")]
        public bool quitOnButtonB = true;
        public float quitDelaySec = 5f;
        [Header("透视（VST passthrough）")]
        public bool enableSeeThrough = true;

        public enum PoseMode { WorldLocked, CaptureProxy, External }

        /// <summary>外部注入的视频源；为空则按 useRealWebRtc 自建。</summary>
        public IWebRtcVideoSource Source { get; set; }
        public Texture Frame => _source?.Frame;

        private DsDomeRenderer _dome;
        private Transform _anchor;
        private Camera _xrCam;
        private Quaternion _frontRot = Quaternion.identity;   // WorldLocked：开机头显朝向（仅 yaw）
        private IWebRtcVideoSource _source;
        private Texture _lastFrame;
        private TextMesh _hud;
        private bool _hudOn = true, _domeOn = true;
        private bool _aPrev, _bPrev, _quitting, _pumpStarted;
        private float _nextCmdPoll, _nextHudRefresh;
        private int _framesThisSec; private float _fpsWindowStart, _measuredFps;

        // 颜色修正：把源纹理 Blit 到本地 RT（R/B 互换），穹顶采样这张 RT
        private Material _swapMat;
        private RenderTexture _correctedRT;

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

        private void Start()
        {
            if (calibration == null)
            {
                Debug.LogError("[RobotLeft] 未连 DS 标定（calibration）。先跑 PicoTest/Robot DS Dome/Import Camchain。");
                return;
            }
            if (enableSeeThrough) EnableVideoSeeThrough();

            _anchor = new GameObject("RobotLeftDomeAnchor").transform;
            _anchor.SetParent(transform, false);
            _dome = gameObject.AddComponent<DsDomeRenderer>();
            _dome.frame = DsDomeRenderer.RenderFrame.WorldLocked; // 锚点姿态我们自己驱动
            _dome.robotHeadAnchor = _anchor;
            _dome.leftCalibration = calibration;
            _dome.rightCalibration = calibration;             // 单目：左右眼共用同一份真实标定
            _dome.leftUVRect = new Vector4(0f, 0f, 1f, 1f);   // 整图 → 单眼（无 SBS 分半）
            _dome.rightUVRect = new Vector4(0f, 0f, 1f, 1f);
            _dome.flipV = flipV;
            _dome.mirror = mirror;
            _dome.coverageDeg = coverageDeg; _dome.radius = radius; _dome.segments = 64;
            _dome.edgeFeatherDeg = edgeFeatherDeg;
            _dome.Initialize();
            _dome.PushParameters();

            var swapShader = Resources.Load<Shader>("SwapRB");
            if (swapShader != null) _swapMat = new Material(swapShader);
            else Debug.LogWarning("[RobotLeft] 未找到 SwapRB shader（Resources/SwapRB.shader），颜色修正已禁用。");

            CreateHud();

            ResolveServerUrl();   // 运行时覆盖：files/robotleftpreview/server.txt 优先于场景默认（免重打包换 IP）
            StartSource(Source);

            Debug.Log($"[RobotLeft] 启动：src={(useRealWebRtc ? "webrtc" : "fake")} server={serverUrl} mode={poseMode} " +
                      $"radius={radius} latency={latencyMs}ms swapRB={swapRB}");
        }

        /// <summary>启动/切换视频源；WebRTC 渲染泵（全局 WebRTC.Update()）只驱动一次。</summary>
        private void StartSource(IWebRtcVideoSource injected)
        {
            _source = injected ?? (useRealWebRtc
                ? (IWebRtcVideoSource)new HttpOfferVideoSource(serverUrl, this)
                : new FakeStereoVideoSource());
            _source.Start();
            var pump = _source.GetRenderPump();
            if (pump != null && !_pumpStarted) { StartCoroutine(pump); _pumpStarted = true; }
        }

        /// <summary>
        /// 服务地址运行时覆盖：读 persistentDataPath/robotleftpreview/server.txt（一行
        /// http://ip:port），有内容即覆盖场景里编译进来的默认值——真机换 PC/换 IP 只需
        /// adb push，不用重打包。
        /// </summary>
        private void ResolveServerUrl()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "robotleftpreview", "server.txt");
                if (!File.Exists(path)) return;
                string u = File.ReadAllText(path).Trim();
                if (!string.IsNullOrEmpty(u)) { serverUrl = u; Debug.Log($"[RobotLeft] 服务地址覆盖自 server.txt → {u}"); }
            }
            catch (Exception e) { Debug.LogWarning($"[RobotLeft] 读 server.txt 失败: {e.Message}"); }
        }

        /// <summary>cmd `server <url>`：停旧源、以新地址重连（免重启应用）。</summary>
        private void ReconnectServer(string url)
        {
            serverUrl = url;
            useRealWebRtc = true;
            try { _source?.Stop(); } catch { }
            _lastFrame = null;
            Source = null;
            StartSource(null);
            Debug.Log($"[RobotLeft] 重连服务 → {url}");
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
            if (_dome == null) return; // Start() 因缺标定提前退出

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

            // com.unity.webrtc 的 VideoStreamTrack.OnVideoReceived 只在首帧触发一次，之后同一张
            // Texture 原地更新内容（不换引用）——不能靠引用比较判断"是否有新帧"（frame != _lastFrame
            // 只会真一次，之后永远假，画面停第一帧不动）。每帧都要重新颜色修正+喂穹顶。
            var frame = _source?.Frame;
            bool haveFrame = frame != null;
            if (haveFrame)
            {
                var shown = ApplyColorFix(frame);
                _dome.leftTex = shown; _dome.rightTex = shown;
                _dome.PushParameters();
                _lastFrame = frame;
                _framesThisSec++;
            }
            if (Time.unscaledTime - _fpsWindowStart >= 1f)
            {
                _measuredFps = _framesThisSec / Mathf.Max(1e-3f, Time.unscaledTime - _fpsWindowStart);
                _framesThisSec = 0; _fpsWindowStart = Time.unscaledTime;
            }

            UpdateAnchor(haveFrame);
            UpdateHud();
        }

        /// <summary>swapRB=true 且 shader 可用时把 R/B 互换后的纹理喂穹顶；否则原样返回。</summary>
        private Texture ApplyColorFix(Texture src)
        {
            if (!swapRB || _swapMat == null) return src;
            if (_correctedRT == null || _correctedRT.width != src.width || _correctedRT.height != src.height)
            {
                if (_correctedRT != null) _correctedRT.Release();
                _correctedRT = new RenderTexture(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            }
            Graphics.Blit(src, _correctedRT, _swapMat);
            return _correctedRT;
        }

        private void EnsureXrCam()
        {
            if (_xrCam != null) return;
            _xrCam = Camera.main;
            if (_xrCam != null)
            {
                _xrCam.clearFlags = CameraClearFlags.SolidColor;
                _xrCam.backgroundColor = enableSeeThrough ? new Color(0f, 0f, 0f, 0f) : Color.black;
                // WorldLocked 的"前方"＝开机时头显朝向（只取 yaw，忽略 pitch/roll，穹顶不歪）。
                // 否则锚点用世界坐标系原点朝向，画面可能出现在玩家开机时身后/侧面。
                _frontRot = Quaternion.Euler(0f, _xrCam.transform.eulerAngles.y, 0f);
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
                    _anchor.SetPositionAndRotation(_xrCam.transform.position, _frontRot);
                    break;
                case PoseMode.CaptureProxy:
                    if (newFrameArrived || !_anchorInitialized)
                        if (LookupPose(Time.unscaledTime - latencyMs * 0.001f, out var p, out var r))
                        { _anchor.SetPositionAndRotation(p, r); _anchorInitialized = true; }
                    break;
                case PoseMode.External:
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

        // ── adb 调参通道（照搬 RobotStream cmd.txt 模式）─────────────────
        // adb shell "echo mode captureproxy > /sdcard/Android/data/<pkg>/files/robotleftpreview/cmd.txt"
        // 命令：server <http://ip:port>（热重连）| radius <m> | latency <ms> | mode worldlocked|captureproxy
        //       | dome on|off | flip 0|1 | colorfix on|off | cover <deg> | feather <deg> | hud on|off | dump

        private void PollCmdFile()
        {
            if (Time.unscaledTime < _nextCmdPoll) return;
            _nextCmdPoll = Time.unscaledTime + 1f;
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "robotleftpreview", "cmd.txt");
                if (!File.Exists(path)) return;
                string raw = File.ReadAllText(path).Trim();
                File.Delete(path);
                if (raw.Length == 0) return;
                Debug.Log($"[RobotLeft] cmd.txt → \"{raw}\"");
                foreach (var line in raw.Split('\n')) ExecCmd(line.Trim());
            }
            catch (Exception e) { Debug.LogWarning($"[RobotLeft] cmd.txt 处理失败: {e.Message}"); }
        }

        private void ExecCmd(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return;
            var parts = cmd.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string arg = parts.Length > 1 ? parts[1] : null;   // 原样保留大小写（URL 用）
            switch (parts[0].ToLowerInvariant())
            {
                case "server" when !string.IsNullOrEmpty(arg):
                    ReconnectServer(arg); break;
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
                case "dome":
                    SetDomeVisible(arg != "off"); break;
                case "flip" when TryF(arg, out float fv):
                    flipV = Mathf.Clamp01(fv); _dome.flipV = flipV; _dome.PushParameters(); break;
                case "colorfix":
                    swapRB = arg != "off";
                    Debug.Log($"[RobotLeft] 颜色修正 → {(swapRB ? "on" : "off")}");
                    break;
                case "cover" when TryF(arg, out float cov):
                    coverageDeg = Mathf.Clamp(cov, 60f, 220f);
                    _dome.coverageDeg = coverageDeg; _dome.PushParameters();
                    Debug.Log("[RobotLeft] cover 只更新羽化阈值（裁剪），网格弧度下次启动生效");
                    break;
                case "feather" when TryF(arg, out float f):
                    edgeFeatherDeg = Mathf.Clamp(f, 0f, 45f);
                    _dome.edgeFeatherDeg = edgeFeatherDeg; _dome.PushParameters(); break;
                case "hud":
                    _hudOn = arg != "off";
                    if (_hud != null) _hud.gameObject.SetActive(_hudOn); break;
                case "dump":
                    Debug.Log($"[RobotLeft] dump: src={(useRealWebRtc ? "webrtc" : "fake")} server={serverUrl} mode={poseMode} " +
                              $"radius={radius} latency={latencyMs} " +
                              $"dome={_domeOn} flip={flipV} colorfix={swapRB} cover={coverageDeg} fps={_measuredFps:F1} frame={(_lastFrame != null)}");
                    break;
                default:
                    Debug.LogWarning($"[RobotLeft] 未知命令：{cmd}"); break;
            }
        }

        private static bool TryF(string s, out float v) =>
            float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v);

        private void SetDomeVisible(bool on)
        {
            _domeOn = on;
            if (_dome != null && _dome.DomeRenderer != null) _dome.DomeRenderer.enabled = on;
            Debug.Log($"[RobotLeft] 穹顶 {(on ? "显示" : "隐藏 → 原生透视对比")}");
        }

        // ── HUD ───────────────────────────────────────────────────────

        private void CreateHud()
        {
            var go = new GameObject("RobotLeftPreviewHud");
            go.transform.SetParent(transform, false);
            _hud = go.AddComponent<TextMesh>();
            _hud.fontSize = 48;
            _hud.characterSize = 0.01f;
            _hud.anchor = TextAnchor.UpperLeft;
            _hud.color = new Color(1f, 0.7f, 0.3f, 0.9f);
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
            _hud.text = $"RobotLeftPreview (DS)  src={(useRealWebRtc ? "webrtc" : "fake")} {_measuredFps:F0}fps frame={(_lastFrame != null ? "yes" : "--")}\n" +
                        $"mode={poseMode}  latency={latencyMs:F0}ms  radius={radius:F2}m  colorfix={(swapRB ? "on" : "off")}\n" +
                        $"cover={coverageDeg:F0}  dome={(_domeOn ? "on" : "OFF(native)")}\n" +
                        $"A=对比原生透视  B=退出  cmd: files/robotleftpreview/cmd.txt";
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
                    if (prop != null && prop.CanWrite) { prop.SetValue(null, true); any = true; Debug.Log($"[RobotLeft] 透视已开启 via {tn}"); continue; }
                    var field = ty.GetField("EnableVideoSeeThrough", BindingFlags.Public | BindingFlags.Static);
                    if (field != null) { field.SetValue(null, true); any = true; Debug.Log($"[RobotLeft] 透视已开启 via {tn}"); }
                }
                catch (Exception e) { Debug.LogWarning($"[RobotLeft] 设 {tn} 透视失败: {e.Message}"); }
            }
            if (!any) Debug.LogWarning("[RobotLeft] 未找到透视 API，透视未开启");
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
            Debug.Log($"[RobotLeft] B 键退出：停源，{quitDelaySec:F0}s 后退出程序");
            StartCoroutine(QuitAfter());
        }

        private IEnumerator QuitAfter()
        {
            try { _source?.Stop(); } catch { }
            yield return new WaitForSeconds(quitDelaySec);
            Debug.Log("[RobotLeft] 退出程序（killProcess）");
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var proc = new AndroidJavaClass("android.os.Process");
                proc.CallStatic("killProcess", proc.CallStatic<int>("myPid"));
            }
            catch (Exception e) { Debug.LogWarning($"[RobotLeft] killProcess 失败: {e.Message}"); }
#elif UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnDestroy()
        {
            try { _source?.Stop(); } catch { }
            if (_correctedRT != null) _correctedRT.Release();
            if (_swapMat != null) Destroy(_swapMat);
        }
    }
}
