// Assets/Experiments/Exp-RobotDsDome/Scripts/DsEyeCalibration.cs
using UnityEngine;

namespace PicoTest.Experiments.RobotDsDome
{
    /// <summary>
    /// 一只眼的 Double Sphere 标定（来自 3-camchain.yaml 的 cam0/cam1）。
    /// 内参 [xi, alpha, fx, fy, cx, cy] 存标定分辨率下的原值（不缩放；shader 用归一化 UV 天然对齐降采样视频）。
    /// </summary>
    [CreateAssetMenu(menuName = "PicoTest/DS Eye Calibration", fileName = "DsEyeCalibration")]
    public sealed class DsEyeCalibration : ScriptableObject
    {
        [Header("DS 内参")] public float xi, alpha, fx, fy, cx, cy;
        [Header("标定分辨率")] public int width = 1920, height = 1080;
        [Header("外参：相机→机器人头 旋转（v1 单位阵）")] public Quaternion extrinsicRotation = Quaternion.identity;

        /// <summary>背后可见半角边界系数 w2（valid: Z &gt; -w2·|d|）。与 DoubleSphereProjection.ComputeW2 一致。</summary>
        public float ComputeW2()
        {
            float w1 = alpha <= 0.5f ? alpha / (1f - alpha) : (1f - alpha) / alpha;
            return (w1 + xi) / Mathf.Sqrt(2f * w1 * xi + xi * xi + 1f);
        }

        public Matrix4x4 ExtrinsicMatrix() => Matrix4x4.Rotate(extrinsicRotation);
    }
}
