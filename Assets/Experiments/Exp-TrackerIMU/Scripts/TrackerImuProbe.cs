using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.XR.PICO.TOBSupport;
using Unity.XR.PXR;
using UnityEngine;
using UnityEngine.XR;
using Debug = UnityEngine.Debug;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace PicoTest.Experiments.TrackerIMU
{
    /// <summary>
    /// 场景唯一入口：验证「不开体追时多个 Motion Tracker 能否同时持续输出 IMU」（P1~P3，见 README）。
    ///
    /// - 企业服务：InitEnterpriseService(true) → 防御性 UnBind → BindEnterpriseService。
    ///   bind 回调只置标志，一切后续（保活/体追/枚举）回 Update 主线程做 —— 规避
    ///   YC-Ego 的前科：进程级 ServiceConnection 已连接时第二次 bind 回调不触发，以及
    ///   Binder 线程禁 JNI。
    /// - 连接真值：1Hz 轮询 GetSwiftTrackerDevices 对账（MotionTrackerConnectionAction
    ///   会漏断开事件，只作触发器）。
    /// - 轮询策略：RoundRobin（1 SN/帧）↔ FullEveryFrame（全 SN/帧），手柄 A/X 键切换。
    /// - 体追对照：编译开关 ENABLE_BODY_TRACKING（默认关；Editor 菜单 PicoTest/Tracker IMU 切）。
    /// - 落盘：persistentDataPath/imu_test/&lt;UTC&gt;_bt{0|1}/samples.csv + events.csv。
    /// - 探针：每秒一行 logcat 可 grep 的 `TrackerImuProbe.probe ...` + 头显内 HUD。
    /// </summary>
    public class TrackerImuProbe : MonoBehaviour
    {
        public enum PollStrategy { RoundRobin, FullEveryFrame }

        [Tooltip("IMU 轮询策略。RoundRobin=每帧 1 个 SN（~3ms Binder 往返不拖帧率）；FullEveryFrame=每帧全量（P3 观察帧率代价）。运行时手柄 A/X 切换。")]
        public PollStrategy strategy = PollStrategy.RoundRobin;

        [Tooltip("GetSwiftIMUData 的 predictTime 参数（ns，0=不预测）。")]
        public long predictTime = 0;

        public static bool BodyTrackingEnabled =>
#if ENABLE_BODY_TRACKING
            true;
#else
            false;
#endif

        readonly Stopwatch _clock = Stopwatch.StartNew();
        double WallMs => _clock.Elapsed.TotalMilliseconds;

        // bind 回调可能来自非主线程：只写这两个 volatile，处理留给 Update。
        volatile int _bindResult;          // 0=等待回调 1=成功 -1=失败
        bool _bindHandled;
        bool _ready;                       // 主线程已完成 post-bind 初始化，可以开始轮询

        readonly List<string> _sns = new List<string>();              // 当前在线 SN（轮询顺序）
        readonly Dictionary<string, long> _snToId = new Dictionary<string, long>();
        int _rrIdx;
        readonly TrackerImuStats _stats = new TrackerImuStats();
        ImuCsvLogger _csv;
        int _disconnects;                  // P1：post-bind 后观测到的断开总数

        float _nextEnumAt;
        float _nextBatteryEventAt;
        float _nextProbeAt;
        int _framesSinceProbe;
        bool _csvErrorLogged;

        TextMesh _hud;
        bool _togglePrev;

        // 配对探针（B/Y 键）：绕过系统设置面板，直接调企业 API 尝试给新槽位配对 ——
        // 验证「5 个是不是系统级硬顶」。槽位轮转 6→7→8→0（0=SwiftDevice.ID_ALL，语义未文档化，最后试）。
        static readonly int[] PairSlots = { 6, 7, 8, 0 };
        int _pairSlotIdx;
        bool _pairPrev;
        string _lastPairInfo = "";

        void Start()
        {
            string dir = Path.Combine(Application.persistentDataPath, "imu_test",
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) +
                "_bt" + (BodyTrackingEnabled ? 1 : 0));
            _csv = new ImuCsvLogger(dir);
            Log($"start bt={(BodyTrackingEnabled ? 1 : 0)} strat={strategy} dir={dir}");
            _csv.WriteEvent(WallMs, "START", $"bt={(BodyTrackingEnabled ? 1 : 0)};strategy={strategy};os={SystemInfo.operatingSystem}");

            try { PXR_MotionTracking.MotionTrackerConnectionAction += OnConnectionAction; }
            catch (Exception e) { LogWarn($"subscribe MotionTrackerConnectionAction threw: {e.Message}"); }

            try
            {
                bool init = PXR_Enterprise.InitEnterpriseService(true);
                Log($"InitEnterpriseService(true) → {init}");
                _csv.WriteEvent(WallMs, "INIT", init ? "ok" : "FAILED");
                if (!init) { _bindResult = -1; return; }

                try { PXR_Enterprise.UnBindEnterpriseService(); Log("defensive pre-bind UnBind done"); }
                catch (Exception e) { LogWarn($"pre-bind unbind threw (ok on first launch): {e.Message}"); }

                PXR_Enterprise.BindEnterpriseService(bound => _bindResult = bound ? 1 : -1);
            }
            catch (Exception e)
            {
                LogError($"enterprise init/bind crashed: {e}");
                _bindResult = -1;
            }
        }

        void OnConnectionAction(long id, int state)
        {
            // 事件通道不可靠（会漏断开），只记录 + 触发立即对账；连接真值以 1Hz 枚举为准。
            Log($"MotionTrackerConnectionAction id={id} state={state}");
            _csv?.WriteEvent(WallMs, "CONN_ACTION", $"id={id};state={state}");
            _nextEnumAt = 0f;
        }

        void Update()
        {
            PumpBindResult();
            if (!_ready) return;

            if (Time.unscaledTime >= _nextEnumAt) ReconcileTrackers();
            PollImu();
            PumpStrategyToggle();
            PumpPairingProbe();
            if (Time.unscaledTime >= _nextProbeAt) EmitProbe();
            _framesSinceProbe++;
        }

        void PumpBindResult()
        {
            if (_bindHandled || _bindResult == 0) return;
            _bindHandled = true;

            if (_bindResult != 1)
            {
                LogError("enterprise service bind FAILED — 企业授权/系统版本？本轮测试无法进行。");
                _csv.WriteEvent(WallMs, "BIND", "FAILED");
                _csv.Flush();
                return;
            }
            Log("enterprise service bound");
            _csv.WriteEvent(WallMs, "BIND", "ok");

            // 灭屏保活（10min 轮次不许灭屏断 Unity 主循环）
            Try("SetScreenOffDelay(NEVER)", () => PXR_Enterprise.PropertySetScreenOffDelay(
                ScreenOffDelayTimeEnum.NEVER, r => Log($"PropertySetScreenOffDelay(NEVER) → {r}")));
            Try("AutoSleep off", () => PXR_Enterprise.SwitchSystemFunction(
                SystemFunctionSwitchEnum.SFS_AUTOSLEEP, SwitchEnum.S_OFF));
            Try("PSensor off", () => PXR_Enterprise.SwitchSystemFunction(
                SystemFunctionSwitchEnum.SFS_PSENSOR, SwitchEnum.S_OFF));

#if ENABLE_BODY_TRACKING
            Try("StartBodyTracking", () =>
            {
                bool supported = false;
                int rcSup = PXR_MotionTracking.GetBodyTrackingSupported(ref supported);
                int rcStart = supported
                    ? PXR_MotionTracking.StartBodyTracking(BodyJointSet.BODY_JOINT_SET_BODY_FULL_START, new BodyTrackingBoneLength())
                    : -1;
                Log($"GetBodyTrackingSupported rc={rcSup} supported={supported}; StartBodyTracking rc={rcStart}");
                _csv.WriteEvent(WallMs, "BT_START", $"supported={supported};rc={rcStart}");
            });
#endif
            _ready = true;
            _nextEnumAt = 0f;
            _nextProbeAt = Time.unscaledTime + 1f;
        }

        void Try(string what, Action a)
        {
            try { a(); }
            catch (Exception e) { LogWarn($"{what} threw: {e.Message}"); }
        }

        /// <summary>1Hz：GetSwiftTrackerDevices 对账在线 SN 集合，CONNECT/DISCONNECT 记事件（P1 判据源）。</summary>
        void ReconcileTrackers()
        {
            _nextEnumAt = Time.unscaledTime + 1f;
            List<SwiftDevice> devices;
            try { devices = PXR_Enterprise.GetSwiftTrackerDevices(); }
            catch (Exception e) { LogWarn($"GetSwiftTrackerDevices threw: {e.Message}"); return; }

            var online = new Dictionary<string, SwiftDevice>();
            if (devices != null)
                foreach (var d in devices)
                    if (d.connectState == SwiftDevice.STATUS_ONLINE && !string.IsNullOrEmpty(d.sn))
                        online[d.sn] = d;

            foreach (var kv in online)
            {
                if (!_snToId.ContainsKey(kv.Key))
                {
                    _snToId[kv.Key] = kv.Value.id;
                    _sns.Add(kv.Key);
                    Log($"tracker CONNECT sn={kv.Key} id={kv.Value.id} battery={kv.Value.battery}");
                    _csv.WriteEvent(WallMs, "CONNECT", $"sn={kv.Key};id={kv.Value.id};battery={kv.Value.battery}");
                }
            }
            for (int i = _sns.Count - 1; i >= 0; i--)
            {
                string sn = _sns[i];
                if (!online.ContainsKey(sn))
                {
                    _disconnects++;
                    Log($"tracker DISCONNECT sn={sn}（P1 关注：是否系统主动断开）");
                    _csv.WriteEvent(WallMs, "DISCONNECT", $"sn={sn}");
                    _sns.RemoveAt(i);
                    _snToId.Remove(sn);
                }
            }

            if (Time.unscaledTime >= _nextBatteryEventAt && online.Count > 0)
            {
                _nextBatteryEventAt = Time.unscaledTime + 5f;
                var sb = new StringBuilder();
                foreach (var kv in online)
                    sb.Append(kv.Key).Append(':').Append(kv.Value.battery).Append(';');
                _csv.WriteEvent(WallMs, "BATTERY", sb.ToString());
            }
        }

        void PollImu()
        {
            int n = _sns.Count;
            if (n == 0) return;
            if (strategy == PollStrategy.RoundRobin)
            {
                PollOne(_sns[_rrIdx % n]);
                _rrIdx++;
            }
            else
            {
                for (int i = 0; i < n; i++) PollOne(_sns[i]);
            }
        }

        void PollOne(string sn)
        {
            IMUData imu = null;
            try { imu = PXR_Enterprise.GetSwiftIMUData(sn, predictTime); }
            catch (Exception e) { LogWarn($"GetSwiftIMUData({sn}) crashed: {e.Message}"); }

            double wallMs = WallMs;
            bool ok = imu != null;
            bool isNew = _stats.Feed(sn, ok, ok ? imu.timestamp : 0, wallMs);
            if (ok)
                _csv.WriteSample(wallMs, Time.frameCount, StrategyTag, BodyTrackingEnabled, sn, true, isNew,
                    imu.timestamp, imu.ax, imu.ay, imu.az, imu.wx, imu.wy, imu.wz,
                    imu.vx, imu.vy, imu.vz, imu.w_ax, imu.w_ay, imu.w_az);
            else
                _csv.WriteSample(wallMs, Time.frameCount, StrategyTag, BodyTrackingEnabled, sn, false, false,
                    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        string StrategyTag => strategy == PollStrategy.RoundRobin ? "RR" : "FULL";

        /// <summary>手柄 A/X（任一手 primaryButton）按下沿：切换轮询策略并重开统计分段（P3）。</summary>
        void PumpStrategyToggle()
        {
            bool pressed = IsButtonPressed(secondary: false);
            if (pressed && !_togglePrev) ToggleStrategy();
            _togglePrev = pressed;
        }

        void ToggleStrategy()
        {
            strategy = strategy == PollStrategy.RoundRobin ? PollStrategy.FullEveryFrame : PollStrategy.RoundRobin;
            _stats.ResetSegment(WallMs);
            Log($"strategy → {StrategyTag}");
            _csv.WriteEvent(WallMs, "STRATEGY", StrategyTag);
        }

        /// <summary>
        /// 手柄 B/Y（任一手 secondaryButton）按下沿：对下一个候选槽位调 StartSwiftTrackerPairing。
        /// 用法：把第 6 个 Tracker 置于配对模式（长按电源键至 LED 闪烁）再按键，观察 conn 是否 +1、
        /// 新 SN 是否出现在枚举里。rc 语义未文档化，原样记录。不做 UnBond（不动现有配对）。
        /// </summary>
        void PumpPairingProbe()
        {
            bool pressed = IsButtonPressed(secondary: true);
            if (pressed && !_pairPrev)
            {
                int slot = PairSlots[_pairSlotIdx % PairSlots.Length];
                _pairSlotIdx++;
                TryPair(slot);
            }
            _pairPrev = pressed;
        }

        void TryPair(int slot)
        {
            int rc = int.MinValue;
            try { rc = PXR_Enterprise.StartSwiftTrackerPairing(slot); }
            catch (Exception e) { LogWarn($"StartSwiftTrackerPairing({slot}) crashed: {e.Message}"); }
            _lastPairInfo = $"pair slot={slot} rc={rc}";
            Log($"StartSwiftTrackerPairing({slot}) → rc={rc}（目标 Tracker 需在配对模式；盯 conn 是否 +1）");
            _csv.WriteEvent(WallMs, "PAIR_ATTEMPT", $"slot={slot};rc={rc}");
        }

        void TryUnbond(int slot)
        {
            int rc = int.MinValue;
            try { rc = PXR_Enterprise.UnBondSwiftTracker(slot); }
            catch (Exception e) { LogWarn($"UnBondSwiftTracker({slot}) crashed: {e.Message}"); }
            _lastPairInfo = $"unbond slot={slot} rc={rc}";
            Log($"UnBondSwiftTracker({slot}) → rc={rc}");
            _csv.WriteEvent(WallMs, "UNBOND_ATTEMPT", $"slot={slot};rc={rc}");
        }

        /// <summary>
        /// 按键探测走双通道（首轮真机 InputDevices 全程无响应，原因未明）：
        /// ① 传统 UnityEngine.XR.InputDevices；② 新 Input System 的 XRController 控件。任一按下即 true。
        /// </summary>
        static bool IsButtonPressed(bool secondary)
        {
            var usage = secondary ? CommonUsages.secondaryButton : CommonUsages.primaryButton;
            foreach (var node in new[] { XRNode.RightHand, XRNode.LeftHand })
            {
                var dev = InputDevices.GetDeviceAtXRNode(node);
                if (dev.isValid && dev.TryGetFeatureValue(usage, out bool b) && b) return true;
            }
            string ctlName = secondary ? "secondaryButton" : "primaryButton";
            foreach (var d in UnityEngine.InputSystem.InputSystem.devices)
            {
                if (!(d is UnityEngine.InputSystem.XR.XRController)) continue;
                var ctl = d.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>(ctlName);
                if (ctl != null && ctl.isPressed) return true;
            }
            return false;
        }

        /// <summary>手柄状态诊断（探针行用）：xr=传统通道有效手数，is=Input System XRController 数。</summary>
        static string ControllerDiag()
        {
            int xr = 0;
            if (InputDevices.GetDeviceAtXRNode(XRNode.LeftHand).isValid) xr++;
            if (InputDevices.GetDeviceAtXRNode(XRNode.RightHand).isValid) xr++;
            int isCount = 0;
            foreach (var d in UnityEngine.InputSystem.InputSystem.devices)
                if (d is UnityEngine.InputSystem.XR.XRController) isCount++;
            return $"xr={xr} is={isCount}";
        }

        /// <summary>
        /// adb 命令通道（不依赖手柄）：每秒检查 files/imu_test/cmd.txt，执行后删除。
        /// 支持：`pair <slot>`、`unbond <slot>`、`strat`。用法：
        /// adb shell "echo pair 6 > /sdcard/Android/data/<pkg>/files/imu_test/cmd.txt"
        /// </summary>
        void ProcessCommandFile()
        {
            string path = Path.Combine(Application.persistentDataPath, "imu_test", "cmd.txt");
            try
            {
                if (!File.Exists(path)) return;
                string raw = File.ReadAllText(path).Trim();
                File.Delete(path);
                Log($"cmd.txt → \"{raw}\"");
                var parts = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return;
                switch (parts[0].ToLowerInvariant())
                {
                    case "pair" when parts.Length > 1 && int.TryParse(parts[1], out int ps): TryPair(ps); break;
                    case "unbond" when parts.Length > 1 && int.TryParse(parts[1], out int us): TryUnbond(us); break;
                    case "strat": ToggleStrategy(); break;
                    default: LogWarn($"未知命令: {raw}（支持 pair <slot> / unbond <slot> / strat）"); break;
                }
            }
            catch (Exception e) { LogWarn($"cmd.txt 处理失败: {e.Message}"); }
        }

        void EmitProbe()
        {
            _nextProbeAt = Time.unscaledTime + 1f;
            double wallMs = WallMs;
            int fps = _framesSinceProbe;
            _framesSinceProbe = 0;

            ProcessCommandFile();

            string summary = _stats.Summary(wallMs);
            // logcat 可 grep 固定前缀；conn/断开数/帧率 是 P1、P3 的每秒快照；ctl=手柄输入通道诊断
            Debug.Log($"TrackerImuProbe.probe bt={(BodyTrackingEnabled ? 1 : 0)} strat={StrategyTag} " +
                      $"conn={_sns.Count} disc={_disconnects} fps={fps} rows={_csv.SampleRows} ctl[{ControllerDiag()}] | {summary}");

            if (_csv.WriteError != null && !_csvErrorLogged)
            {
                _csvErrorLogged = true;
                LogError($"CSV 写入失败（已停写，探针继续）: {_csv.WriteError.Message}");
            }
            _csv.Flush();
            UpdateHud(fps, summary);
        }

        void UpdateHud(int fps, string summary)
        {
            if (_hud == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                var go = new GameObject("TrackerImuHud");
                go.transform.SetParent(cam.transform, false);
                go.transform.localPosition = new Vector3(0f, -0.15f, 2f);
                _hud = go.AddComponent<TextMesh>();
                // 真机包会裁剪内置字体（首轮实测 GetBuiltinResource 抛异常→HUD 不可见），逐级回退到 OS 动态字体
                Font font = null;
                try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.fontsettings"); } catch { }
                if (font == null) { try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
                if (font == null) { try { font = Font.CreateDynamicFontFromOSFont("sans-serif", 48); } catch { } }
                if (font == null) { try { font = Font.CreateDynamicFontFromOSFont("Roboto", 48); } catch { } }
                if (font != null)
                {
                    _hud.font = font;
                    _hud.GetComponent<MeshRenderer>().material = font.material;
                }
                else LogWarn("HUD 字体全部回退失败，HUD 将不可见（探针日志不受影响）");
                _hud.characterSize = 0.02f;
                _hud.fontSize = 48;
                _hud.anchor = TextAnchor.MiddleCenter;
                _hud.alignment = TextAlignment.Left;
                _hud.color = Color.green;
            }
            _hud.text = $"bt={(BodyTrackingEnabled ? 1 : 0)}  strat={StrategyTag}  conn={_sns.Count}  " +
                        $"disc={_disconnects}  fps={fps}\n{summary.Replace(" | ", "\n")}" +
                        (_lastPairInfo.Length > 0 ? $"\n{_lastPairInfo}" : "") +
                        (_csv.WriteError != null ? "\n<CSV WRITE ERROR>" : "");
        }

        void OnDestroy()
        {
            try { PXR_MotionTracking.MotionTrackerConnectionAction -= OnConnectionAction; } catch { }
#if ENABLE_BODY_TRACKING
            Try("StopBodyTracking", () => PXR_MotionTracking.StopBodyTracking());
#endif
            if (_csv != null)
            {
                _csv.WriteEvent(WallMs, "STOP", $"disc={_disconnects};rows={_csv.SampleRows}");
                _csv.Dispose();
                Log($"stopped, csv rows={_csv.SampleRows} dir={_csv.Dir}");
            }
            // 企业服务保持绑定到进程结束（官方示例做法；退出用 killProcess，无需解绑）
        }

        static void Log(string m) => Debug.Log("[TrackerIMU] " + m);
        static void LogWarn(string m) => Debug.LogWarning("[TrackerIMU] " + m);
        static void LogError(string m) => Debug.LogError("[TrackerIMU] " + m);
    }
}
