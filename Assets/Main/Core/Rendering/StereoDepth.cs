// Assets/Main/Core/Rendering/StereoDepth.cs
using System;

namespace PicoTest.Core.Rendering
{
    /// <summary>
    /// 立体深度核心（M2 的确定性部分）：校正(rectified pinhole)空间下 视差 → 深度 → 3D 点。
    /// 纯 C#（Main.Core 禁 UnityEngine），可 EditMode 断言。
    ///
    /// 管线定位（完整 M2）：raw 鱼眼 → 用 K_rectified + 鱼眼模型去畸变到 pinhole（GPU remap）
    ///   → 极线对齐后左右图立体匹配得逐像素视差 d（GPU，重活/设备门）
    ///   → 本类把 (u,v,d) 换成 rectified-left 相机系的 3D 点 / 深度（确定性，可测）
    ///   → 调用方用外参把点转到头坐标系 → 喂 <see cref="EyeReprojection"/> / 深度面。
    ///
    /// 约定：校正针孔模型，左右相机共面、沿 +X 基线 B 分离、同焦距。
    /// 视差 d = u_left − u_right（像素，正值，越大越近）。深度 Z = fx·B / d。
    /// </summary>
    public static class StereoDepth
    {
        /// <summary>视差(px) → 深度(m)。d ≤ eps 视为无效 → 返回 +∞（远/无匹配）。</summary>
        public static double DepthFromDisparity(double disparityPx, double fxRect, double baselineM, double eps = 1e-6)
        {
            if (disparityPx <= eps) return double.PositiveInfinity;
            return fxRect * baselineM / disparityPx;
        }

        /// <summary>有效性显式版：d &gt; eps 且有限时 valid=true。</summary>
        public static bool TryDepthFromDisparity(double disparityPx, double fxRect, double baselineM,
            out double depthM, double eps = 1e-6)
        {
            depthM = DepthFromDisparity(disparityPx, fxRect, baselineM, eps);
            return disparityPx > eps && !double.IsInfinity(depthM);
        }

        /// <summary>
        /// 校正像素 (u,v) + 视差 d → rectified-left 相机系 3D 点 (X,Y,Z)（米，+Z 前、+X 右、+Y 下按图像约定）。
        /// 无效视差 → 返回 false，点置零。
        /// </summary>
        public static bool PointFromDisparity(
            double u, double v, double disparityPx,
            double fx, double fy, double cx, double cy, double baselineM,
            out double x, out double y, out double z, double eps = 1e-6)
        {
            if (!TryDepthFromDisparity(disparityPx, fx, baselineM, out z, eps))
            {
                x = y = z = 0;
                return false;
            }
            x = (u - cx) * z / fx;
            y = (v - cy) * z / fy;
            return true;
        }
    }
}
