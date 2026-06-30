// Assets/Main/Scripts/Rendering/RobotHeadPoseDriver.cs
using UnityEngine;
using PicoTest.Core.Rendering;

namespace PicoTest.Rendering
{
    /// <summary>
    /// 混合转向的"慢"分量（设计 §6）：以低速率把 robotHeadAnchor 的偏航伺服到目标注视角，
    /// 保持画面居中并能看向相机视场外。目标角由外部喂入（首版可用 XR 头部偏航的平滑均值；
    /// 真实场景由机器人云台遥测/IMU 提供——传输层另案）。GazeServo 纯数学已 EditMode 单测。
    /// </summary>
    public sealed class RobotHeadPoseDriver : MonoBehaviour
    {
        [Tooltip("被驱动的穹顶锚点（WorldLocked 下挂穹顶）")]
        public Transform robotHeadAnchor;

        [Tooltip("目标注视偏航角（度）。外部每帧设置；首版可接 XR 头部平均朝向。followLocalHead=true 时由本组件自动算。")]
        public float targetYawDeg;

        [Header("本地头部跟随（首版；真实场景改由机器人云台遥测/IMU 喂 targetYawDeg）")]
        [Tooltip("勾选则每帧用头显偏航作为目标，实现“转头超死区才低速插值回中”")]
        public bool followLocalHead = false;
        [Tooltip("头显 Transform；留空自动取 Camera.main")]
        public Transform headTransform;

        [Header("伺服参数（低速率→不给转头引入延迟）")]
        public float rateDegPerSec = 20f;   // 慢
        public float deadzoneDeg = 8f;       // 死区：小幅头动不驱动云台

        private double _currentYaw;

        private void Start()
        {
            if (robotHeadAnchor != null)
                _currentYaw = robotHeadAnchor.localEulerAngles.y;
        }

        private void Update()
        {
            if (robotHeadAnchor == null) return;

            if (followLocalHead)
            {
                var head = headTransform != null ? headTransform
                         : (Camera.main != null ? Camera.main.transform : null);
                if (head != null)
                {
                    // 头朝向投到锚点父空间求偏航（锚点用 localEulerAngles 驱动，故须同坐标系）
                    var parent = robotHeadAnchor.parent;
                    Vector3 f = parent != null ? parent.InverseTransformDirection(head.forward) : head.forward;
                    f.y = 0f;
                    if (f.sqrMagnitude > 1e-6f)
                        targetYawDeg = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                }
            }

            _currentYaw = GazeServo.Step(_currentYaw, targetYawDeg, Time.deltaTime, rateDegPerSec, deadzoneDeg);
            var e = robotHeadAnchor.localEulerAngles;
            robotHeadAnchor.localEulerAngles = new Vector3(e.x, (float)_currentYaw, e.z);
        }
    }
}
