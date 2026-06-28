// Assets/Main/Scripts/Rendering/FisheyeCalibration.cs
using UnityEngine;
using PicoTest.Core.Rendering;

namespace PicoTest.Rendering
{
    /// <summary>一只眼的鱼眼标定（决策 4：外参为 R(eye→robotHead)）。由真实标定填充。</summary>
    [CreateAssetMenu(menuName = "PicoTest/Fisheye Calibration", fileName = "FisheyeCalibration")]
    public sealed class FisheyeCalibration : ScriptableObject
    {
        [Header("内参 (像素)")] public float fx, fy, cx, cy;
        [Header("等距畸变 k1..k4")] public float k1, k2, k3, k4;
        [Header("图像尺寸")] public int width = 1600, height = 1600;
        [Header("外参：相机→机器人头 旋转")] public Quaternion extrinsicRotation = Quaternion.identity;

        /// <summary>转 Core 纯数学结构。R_eye 行主序由四元数矩阵展开（M.MultiplyVector(v)==q*v）。</summary>
        public FisheyeProjection ToProjection(double thetaMaxRad)
        {
            var m = Matrix4x4.Rotate(extrinsicRotation);
            // 行主序：row0=(m00,m01,m02)... 与 Core 的 cx=R[0]*dx+R[1]*dy+R[2]*dz 对应
            double[] r =
            {
                m.m00, m.m01, m.m02,
                m.m10, m.m11, m.m12,
                m.m20, m.m21, m.m22,
            };
            return new FisheyeProjection(fx, fy, cx, cy, k1, k2, k3, k4, width, height, thetaMaxRad, r);
        }

        /// <summary>shader 需要的 3x3（Matrix4x4，平移/缩放为单位）。</summary>
        public Matrix4x4 ExtrinsicMatrix() => Matrix4x4.Rotate(extrinsicRotation);
    }
}
