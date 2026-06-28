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

        [Tooltip("目标注视偏航角（度）。外部每帧设置；首版可接 XR 头部平均朝向。")]
        public float targetYawDeg;

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
            _currentYaw = GazeServo.Step(_currentYaw, targetYawDeg, Time.deltaTime, rateDegPerSec, deadzoneDeg);
            var e = robotHeadAnchor.localEulerAngles;
            robotHeadAnchor.localEulerAngles = new Vector3(e.x, (float)_currentYaw, e.z);
        }
    }
}
