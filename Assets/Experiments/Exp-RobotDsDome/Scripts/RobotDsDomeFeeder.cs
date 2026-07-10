// Assets/Experiments/Exp-RobotDsDome/Scripts/RobotDsDomeFeeder.cs
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.XR;

namespace PicoTest.Experiments.RobotDsDome
{
    /// <summary>
    /// Robot DS Dome Demo：机器人双目（Double Sphere 模型）SBS 视频 → DS 穹顶。参考 VstPassthroughDemo，
    /// 另起炉灶只引用 Main，不改其他 demo。视频用 VideoPlayer 播 StreamingAssets 里的 episode_*.mp4
    /// （h264，编辑器可解码肉眼验证）；显示复用 VstPassthrough 思路：capture 位姿、cmd 调参、A 键对比原生透视、
    /// B 键退出、HUD。DS 去畸变在 RobotDsDome.shader（照抄 DoubleSphereProjection）。
    /// </summary>
    public sealed class RobotDsDomeFeeder : MonoBehaviour
    {
        [Header("DS 标定（左右各一；跑 Import Camchain 生成）")]
        public DsEyeCalibration leftCalibration, rightCalibration;
        [Header("视频（StreamingAssets 下的 SBS mp4）")]
        public string videoFileName = "episode_000000.mp4";
        public int sbsWidth = 1920, sbsHeight = 540;   // SBS 整幅；每眼半幅
        [Header("穹顶")]
        public float coverageDeg = 190f;
        public float radius = 2.0f;
        public float edgeFeatherDeg = 8f;
        [Header("画面朝向（真实视频上经验校正；默认 flipV=1）")]
        [Range(0, 1)] public float flipV = 1f;
        [Range(0, 1)] public float mirror = 0f;
        [Header("位姿模式")]
        public PoseMode poseMode = PoseMode.WorldLocked;
        public float latencyMs = 100f;
        [Header("透视 / 退出")]
        public bool enableSeeThrough = true;
        public bool quitOnButtonB = true;
        public float quitDelaySec = 5f;

        public enum PoseMode { WorldLocked, CaptureProxy }

        /// <summary>测试可注入纹理（跳过 VideoPlayer）；为空则播 videoFileName。</summary>
        public Texture InjectedTexture { get; set; }
        public Texture Frame => _tex;

        private DsDomeRenderer _dome;
        private Transform _anchor;
        private Camera _xrCam;
        private VideoPlayer _vp;
        private RenderTexture _rt;
        private Texture _tex;
        private TextMesh _hud;
        private bool _hudOn = true, _domeOn = true;
        private bool _aPrev, _bPrev, _quitting;
        private float _nextCmdPoll, _nextHudRefresh;

        private const int PoseCap = 256;
        private readonly float[] _poseT = new float[PoseCap];
        private readonly Vector3[] _posePos = new Vector3[PoseCap];
        private readonly Quaternion[] _poseRot = new Quaternion[PoseCap];
        private int _poseHead, _poseCount; private bool _anchorInit;

        private void Start()
        {
            if (leftCalibration == null || rightCalibration == null)
            {
                Debug.LogError("[RobotDs] 未连 DS 标定（RobotDsLeft/Right）。先跑 PicoTest/Robot DS Dome/Import Camchain。");
                return;
            }
            if (enableSeeThrough) EnableVideoSeeThrough();

            // 数据源：注入纹理优先，否则起 VideoPlayer 播 SBS 视频
            if (InjectedTexture != null) _tex = InjectedTexture;
            else StartVideo();

            _anchor = new GameObject("DsDomeAnchor").transform;
            _anchor.SetParent(transform, false);
            _dome = gameObject.AddComponent<DsDomeRenderer>();
            _dome.frame = DsDomeRenderer.RenderFrame.WorldLocked;
            _dome.robotHeadAnchor = _anchor;
            _dome.leftCalibration = leftCalibration;
            _dome.rightCalibration = rightCalibration;
            _dome.leftTex = _tex; _dome.rightTex = _tex;
            _dome.leftUVRect = new Vector4(0f, 0f, 0.5f, 1f);    // SBS 左半 → 左眼（cam0）
            _dome.rightUVRect = new Vector4(0.5f, 0f, 0.5f, 1f); // SBS 右半 → 右眼（cam1）
            _dome.flipV = flipV; _dome.mirror = mirror;
            _dome.coverageDeg = coverageDeg; _dome.radius = radius; _dome.segments = 64;
            _dome.edgeFeatherDeg = edgeFeatherDeg;
            _dome.Initialize();
            _dome.PushParameters();

            CreateHud();
            Debug.Log($"[RobotDs] 启动：video={videoFileName} mode={poseMode} radius={radius} " +
                      $"cover={coverageDeg} flipV={flipV} mirror={mirror}");
        }

        private void StartVideo()
        {
            _rt = new RenderTexture(sbsWidth, sbsHeight, 0, RenderTextureFormat.ARGB32) { wrapMode = TextureWrapMode.Clamp };
            _tex = _rt;
            var go = new GameObject("DsVideoPlayer");
            go.transform.SetParent(transform, false);
            _vp = go.AddComponent<VideoPlayer>();
            _vp.source = VideoSource.Url;
            _vp.url = Path.Combine(Application.streamingAssetsPath, videoFileName);
            _vp.isLooping = true;
            _vp.renderMode = VideoRenderMode.RenderTexture;
            _vp.targetTexture = _rt;
            _vp.audioOutputMode = VideoAudioOutputMode.None;
            _vp.playOnAwake = true;
            _vp.errorReceived += (vp, msg) => Debug.LogWarning($"[RobotDs] VideoPlayer 错误：{msg}（{_vp.url}）");
            _vp.Play();
        }

        private void Update()
        {
            if (quitOnButtonB && !_quitting)
            {
                var rh = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                if (rh.TryGetFeatureValue(CommonUsages.secondaryButton, out bool bNow))
                { if (bNow && !_bPrev) StartQuit(); _bPrev = bNow; }
                if (rh.TryGetFeatureValue(CommonUsages.primaryButton, out bool aNow))
                { if (aNow && !_aPrev) SetDomeVisible(!_domeOn); _aPrev = aNow; }
            }
            if (_quitting) return;

            EnsureXrCam();
            RecordHeadPose();
            PollCmdFile();
            UpdateAnchor();
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

        private void UpdateAnchor()
        {
            if (_xrCam == null || _anchor == null) return;
            if (poseMode == PoseMode.WorldLocked)
            {
                _anchor.SetPositionAndRotation(_xrCam.transform.position, Quaternion.identity);
                return;
            }
            if (!_anchorInit && LookupPose(Time.unscaledTime - latencyMs * 0.001f, out var p, out var r))
            { _anchor.SetPositionAndRotation(p, r); _anchorInit = true; }
            else if (_anchorInit)
                _anchor.position = _xrCam.transform.position; // 位置跟眼，朝向保持捕获时
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
            if (best < 0) best = (_poseHead - _poseCount + PoseCap) % PoseCap;
            pos = _posePos[best]; rot = _poseRot[best];
            return true;
        }

        // ── adb 调参（照搬 VstPassthrough cmd.txt 模式）─────────────────
        // adb shell "echo flip 0 > /sdcard/Android/data/<pkg>/files/robotds/cmd.txt"
        // radius <m> | latency <ms> | mode worldlocked|captureproxy | flip 0|1 | mirror 0|1
        //   | cover <deg> | feather <deg> | dome on|off | hud on|off | dump

        private void PollCmdFile()
        {
            if (Time.unscaledTime < _nextCmdPoll) return;
            _nextCmdPoll = Time.unscaledTime + 1f;
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "robotds", "cmd.txt");
                if (!File.Exists(path)) return;
                string raw = File.ReadAllText(path).Trim();
                File.Delete(path);
                if (raw.Length == 0) return;
                Debug.Log($"[RobotDs] cmd.txt → \"{raw}\"");
                foreach (var line in raw.Split('\n')) ExecCmd(line.Trim());
            }
            catch (Exception e) { Debug.LogWarning($"[RobotDs] cmd.txt 处理失败: {e.Message}"); }
        }

        private void ExecCmd(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return;
            var parts = cmd.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string arg = parts.Length > 1 ? parts[1] : null;
            switch (parts[0].ToLowerInvariant())
            {
                case "radius" when TryF(arg, out float r):
                    radius = Mathf.Clamp(r, 0.3f, 50f); _dome.radius = radius;
                    if (_dome.DomeTransform != null) _dome.DomeTransform.localScale = Vector3.one * radius; break;
                case "latency" when TryF(arg, out float ms): latencyMs = Mathf.Clamp(ms, 0f, 500f); break;
                case "mode": poseMode = arg == "captureproxy" ? PoseMode.CaptureProxy : PoseMode.WorldLocked; _anchorInit = false; break;
                case "flip" when TryF(arg, out float fv): flipV = Mathf.Clamp01(fv); _dome.flipV = flipV; _dome.PushParameters(); break;
                case "mirror" when TryF(arg, out float mv): mirror = Mathf.Clamp01(mv); _dome.mirror = mirror; _dome.PushParameters(); break;
                case "cover" when TryF(arg, out float cov):
                    coverageDeg = Mathf.Clamp(cov, 60f, 220f); _dome.coverageDeg = coverageDeg; _dome.PushParameters();
                    Debug.Log("[RobotDs] cover 只更新羽化阈值；网格弧度下次启动生效"); break;
                case "feather" when TryF(arg, out float f): edgeFeatherDeg = Mathf.Clamp(f, 0f, 45f); _dome.edgeFeatherDeg = edgeFeatherDeg; _dome.PushParameters(); break;
                case "dome": SetDomeVisible(arg != "off"); break;
                case "hud": _hudOn = arg != "off"; if (_hud != null) _hud.gameObject.SetActive(_hudOn); break;
                case "dump":
                    Debug.Log($"[RobotDs] dump: mode={poseMode} radius={radius} latency={latencyMs} " +
                              $"flip={flipV} mirror={mirror} cover={coverageDeg} dome={_domeOn} frame={(_tex != null)}"); break;
                default: Debug.LogWarning($"[RobotDs] 未知命令：{cmd}"); break;
            }
        }

        private static bool TryF(string s, out float v) =>
            float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);

        private void SetDomeVisible(bool on)
        {
            _domeOn = on;
            if (_dome != null && _dome.DomeRenderer != null) _dome.DomeRenderer.enabled = on;
            Debug.Log($"[RobotDs] 穹顶 {(on ? "显示" : "隐藏 → 原生透视对比")}");
        }

        private void CreateHud()
        {
            var go = new GameObject("RobotDsHud");
            go.transform.SetParent(transform, false);
            _hud = go.AddComponent<TextMesh>();
            _hud.fontSize = 48; _hud.characterSize = 0.01f; _hud.anchor = TextAnchor.UpperLeft;
            _hud.color = new Color(1f, 0.85f, 0.3f, 0.9f);
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
            _hud.text = $"RobotDsDome (Double Sphere)\n" +
                        $"mode={poseMode}  radius={radius:F2}m  cover={coverageDeg:F0}\n" +
                        $"flip={flipV:F0} mirror={mirror:F0}  dome={(_domeOn ? "on" : "OFF(native)")}\n" +
                        $"A=对比原生透视  B=退出  cmd: files/robotds/cmd.txt";
        }

        private static void EnableVideoSeeThrough()
        {
            string[] typeNames =
            {
                "Unity.XR.PXR.PXR_Manager",
                "Unity.XR.OpenXR.Features.PICOSupport.PassthroughFeature",
            };
            foreach (var tn in typeNames)
            {
                try
                {
                    var ty = FindType(tn); if (ty == null) continue;
                    var prop = ty.GetProperty("EnableVideoSeeThrough", BindingFlags.Public | BindingFlags.Static);
                    if (prop != null && prop.CanWrite) { prop.SetValue(null, true); Debug.Log($"[RobotDs] 透视已开启 via {tn}"); return; }
                    var field = ty.GetField("EnableVideoSeeThrough", BindingFlags.Public | BindingFlags.Static);
                    if (field != null) { field.SetValue(null, true); Debug.Log($"[RobotDs] 透视已开启 via {tn}"); return; }
                }
                catch (Exception e) { Debug.LogWarning($"[RobotDs] 设 {tn} 透视失败: {e.Message}"); }
            }
            Debug.LogWarning("[RobotDs] 未找到透视 API，透视未开启");
        }

        private static Type FindType(string fullName)
        {
            var t = Type.GetType(fullName);
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            { t = asm.GetType(fullName); if (t != null) return t; }
            return null;
        }

        private void StartQuit()
        {
            if (_quitting) return;
            _quitting = true;
            Debug.Log($"[RobotDs] B 键退出，{quitDelaySec:F0}s 后退出程序");
            StartCoroutine(QuitAfter());
        }

        private IEnumerator QuitAfter()
        {
            try { if (_vp != null) _vp.Stop(); } catch { }
            yield return new WaitForSeconds(quitDelaySec);
#if UNITY_ANDROID && !UNITY_EDITOR
            try { using var proc = new AndroidJavaClass("android.os.Process"); proc.CallStatic("killProcess", proc.CallStatic<int>("myPid")); }
            catch (Exception e) { Debug.LogWarning($"[RobotDs] killProcess 失败: {e.Message}"); }
#elif UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnDestroy()
        {
            if (_vp != null) _vp.Stop();
            if (_rt != null) _rt.Release();
        }
    }
}
