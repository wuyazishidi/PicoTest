// Assets/Main/Core/Rendering/EyeReprojection.cs
using System;

namespace PicoTest.Core.Rendering
{
    /// <summary>
    /// 视点重投影核心（把"眼睛的视线"换算成"相机怎么拍到那个点"的方向）——seethrough 复刻的心脏。
    /// 纯 C#（Main.Core 禁 UnityEngine）；shader HLSL 必须逐行照抄以保证几何 1:1。
    ///
    /// 几何：屏幕像素 = 眼睛光心 E 出发的一条视线 eHat；沿它走深度 D 命中真实世界点
    ///   P = E + D·eHat。相机光心 C = E + t（t = 相机相对眼睛的平移）。相机要采样 P，
    ///   用的方向是 (P − C) = D·eHat − t。把这个方向喂给 <see cref="FisheyeProjection"/> 即得 UV。
    ///
    /// 退化：D→∞ 时 (D·eHat − t)/D → eHat，平移 t 被淹没 → 纯旋转（= 固定无穷远穹顶，只有远景对）。
    /// 这正是"给穹顶每像素真实深度即得 seethrough 效果"的数学表达。
    /// </summary>
    public readonly struct EyeReprojection
    {
        /// <summary>
        /// 眼视线方向(ex,ey,ez，不必归一化) + 深度 depth + 相机相对眼睛平移(tx,ty,tz)
        /// → 相机采样方向(dx,dy,dz，未归一化，供 FisheyeProjection 使用)。
        /// 全部在"外参 R 所处的同一参考系"里表达（约定 = 眼/头坐标系）。
        /// </summary>
        public static void CameraRayForEyeRay(
            double ex, double ey, double ez,
            double depth,
            double tx, double ty, double tz,
            out double dx, out double dy, out double dz)
        {
            // 眼视线单位化（depth 沿单位视线度量）
            double len = Math.Sqrt(ex * ex + ey * ey + ez * ez);
            double inv = len > 1e-12 ? 1.0 / len : 0.0;
            double hx = ex * inv, hy = ey * inv, hz = ez * inv;

            // P − C = depth·eHat − t
            dx = depth * hx - tx;
            dy = depth * hy - ty;
            dz = depth * hz - tz;
        }
    }
}
