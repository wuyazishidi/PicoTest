// Assets/Main/Vst/RawReprojectionDiagnostics.cs
using UnityEngine;
using UnityEngine.XR;
using PicoTest.Rendering;

namespace PicoTest.Vst
{
    /// <summary>
    /// 纯 raw 重投影 Demo 的真机验证/调参工具。挂到 RawReprojectionFeeder 同物体（或场景任意物体）。
    /// 用途：真机 bring-up 时把"只能上机定"的量（外参手性、翻转、镜像、深度）做成**手柄实时切换**，
    /// 免去每猜一次就重打包；同时定期把 VST/外参/标定运行时事实打到日志（logcat/adb 可读）。
    ///
    /// 手柄映射（PICO 控制器）：
    ///   左 X：切 flipV（画面上下颠倒时按）
    ///   左 Y：切 mirror（左右镜像 / 文字反了时按）
    ///   右 A：翻转左右外参平移符号（视点重投影方向反了 = 视差朝反 时按 → 定手性）
    ///   右 B：常量深度在 近(1.5m)/中(5m)/远(20m) 循环（看视差量级是否合理）
    /// 全部改动即时 PushParameters 生效，日志打印新状态。纯运行时/设备门，编辑器仅编译。
    /// </summary>
    public sealed class RawReprojectionDiagnostics : MonoBehaviour
    {
        [Tooltip("状态日志间隔(秒)")] public float logIntervalSec = 2f;
        [Tooltip("右 B 循环的常量深度候选(m)")] public float[] depthCycle = { 1.5f, 5f, 20f };

        private ReprojectionDomeRenderer _dome;
        private RawReprojectionFeeder _feeder;
        private float _nextLog;
        private int _depthIdx;
        private bool _xPrev, _yPrev, _aPrev, _bPrev;
        private float _extrinSign = 1f;

        private void Update()
        {
            if (_dome == null) _dome = FindObjectOfType<ReprojectionDomeRenderer>();
            if (_feeder == null) _feeder = FindObjectOfType<RawReprojectionFeeder>();

            HandleToggles();
            PeriodicLog();
        }

        private void HandleToggles()
        {
            if (_dome == null) return;
            var lh = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            var rh = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

            if (Rising(lh, CommonUsages.primaryButton, ref _xPrev))
            {
                _dome.flipV = _dome.flipV > 0.5f ? 0f : 1f;
                _dome.PushParameters();
                Debug.Log($"[Diag] flipV → {_dome.flipV}");
            }
            if (Rising(lh, CommonUsages.secondaryButton, ref _yPrev))
            {
                _dome.mirror = _dome.mirror > 0.5f ? 0f : 1f;
                _dome.PushParameters();
                Debug.Log($"[Diag] mirror → {_dome.mirror}");
            }
            if (Rising(rh, CommonUsages.primaryButton, ref _aPrev))
            {
                _extrinSign = -_extrinSign;
                if (_feeder != null && _feeder.leftCalibration != null && _feeder.rightCalibration != null)
                {
                    _feeder.leftCalibration.extrinsicTranslation *= -1f;
                    _feeder.rightCalibration.extrinsicTranslation *= -1f;
                    _dome.PushParameters();
                    Debug.Log($"[Diag] 外参平移符号翻转 → sign={_extrinSign} L.t={_feeder.leftCalibration.extrinsicTranslation}");
                }
            }
            if (Rising(rh, CommonUsages.secondaryButton, ref _bPrev))
            {
                if (depthCycle != null && depthCycle.Length > 0)
                {
                    _depthIdx = (_depthIdx + 1) % depthCycle.Length;
                    _dome.DepthSurface = new ConstantDepthSurface(depthCycle[_depthIdx]);
                    _dome.ApplyDepth();
                    Debug.Log($"[Diag] 常量深度 → {depthCycle[_depthIdx]}m");
                }
            }
        }

        private static bool Rising(InputDevice dev, InputFeatureUsage<bool> btn, ref bool prev)
        {
            bool now = dev.TryGetFeatureValue(btn, out bool v) && v;
            bool rising = now && !prev;
            prev = now;
            return rising;
        }

        private void PeriodicLog()
        {
            if (Time.unscaledTime < _nextLog) return;
            _nextLog = Time.unscaledTime + Mathf.Max(0.5f, logIntervalSec);

            Debug.Log($"[Diag] frames={VstCamera.FramesReceived} datasize={VstCamera.LastFrameDatasize} bpp={VstCamera.LastFrameBpp} " +
                      $"open={VstCamera.IsOpen} stream={VstCamera.IsStreaming} " +
                      $"paramsValid={VstCamera.ParamsValid} extrinValid={VstCamera.ExtrinsicsValid}");
            if (VstCamera.ExtrinsicsValid)
                Debug.Log($"[Diag] extrin L.t={(Vector3)VstCamera.LeftExtrinsics.GetColumn(3)} R.t={(Vector3)VstCamera.RightExtrinsics.GetColumn(3)}");
        }
    }
}
