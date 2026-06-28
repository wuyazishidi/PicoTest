// Assets/Main/Core/Rendering/FisheyeProjection.cs
using System;

namespace PicoTest.Core.Rendering
{
    /// <summary>
    /// 鱼眼正投影（OpenCV fisheye 等距模型）：眼坐标系视线方向 → 像素(u,v) + FOV 判定。
    /// 纯 C#（Main.Core 禁 UnityEngine）；shader HLSL 必须逐行照抄此公式以保证角度 1:1。
    /// 约定：+Z 光轴前方、+X 右、+Y 上；R_eye = R(eye→robotHead) 行主序 3x3。
    /// </summary>
    public readonly struct FisheyeProjection
    {
        private readonly double _fx, _fy, _cx, _cy, _k1, _k2, _k3, _k4, _thetaMax;
        private readonly int _w, _h;
        private readonly double[] _r; // 9, 行主序

        public FisheyeProjection(double fx, double fy, double cx, double cy,
            double k1, double k2, double k3, double k4,
            int width, int height, double thetaMaxRad, double[] rEyeRowMajor)
        {
            _fx = fx; _fy = fy; _cx = cx; _cy = cy;
            _k1 = k1; _k2 = k2; _k3 = k3; _k4 = k4;
            _w = width; _h = height; _thetaMax = thetaMaxRad;
            _r = rEyeRowMajor ?? new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        }

        /// <summary>方向(dx,dy,dz) 不必归一化。返回是否落在图像内；inFov=是否在 thetaMax 内。</summary>
        public bool ProjectDirection(double dx, double dy, double dz,
            out double u, out double v, out bool inFov)
        {
            // 1) 外参旋转 d_cam = R_eye * d
            double cx = _r[0] * dx + _r[1] * dy + _r[2] * dz;
            double cy = _r[3] * dx + _r[4] * dy + _r[5] * dz;
            double cz = _r[6] * dx + _r[7] * dy + _r[8] * dz;

            // 2) 离轴角
            double rxy = Math.Sqrt(cx * cx + cy * cy);
            double theta = Math.Atan2(rxy, cz);
            inFov = theta <= _thetaMax;

            // 3) 等距畸变
            double t2 = theta * theta;
            double thetaD = theta * (1 + _k1 * t2 + _k2 * t2 * t2 + _k3 * t2 * t2 * t2 + _k4 * t2 * t2 * t2 * t2);

            // 4) 方位 → 像素（near-axis 守护）
            double cosPhi, sinPhi;
            if (rxy < 1e-12) { cosPhi = 0; sinPhi = 0; }
            else { cosPhi = cx / rxy; sinPhi = cy / rxy; }
            u = _fx * (thetaD * cosPhi) + _cx;
            v = _fy * (thetaD * sinPhi) + _cy;

            return u >= 0 && u <= _w && v >= 0 && v <= _h;
        }

        /// <summary>归一化 UV（v 翻转留给 shader 的 _FlipV 处理；此处不翻）。</summary>
        public bool ProjectToUV(double dx, double dy, double dz, out double uNorm, out double vNorm, out bool inFov)
        {
            bool inImg = ProjectDirection(dx, dy, dz, out double u, out double v, out inFov);
            uNorm = u / _w; vNorm = v / _h;
            return inImg;
        }
    }
}
