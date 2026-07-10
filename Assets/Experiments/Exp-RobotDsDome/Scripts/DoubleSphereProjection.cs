// Assets/Experiments/Exp-RobotDsDome/Scripts/DoubleSphereProjection.cs
using System;

namespace PicoTest.Experiments.RobotDsDome
{
    /// <summary>
    /// Double Sphere（DS）相机前向投影（Usenko et al. 2018）：相机系 3D 光线 → 像素(u,v) + 有效判定。
    /// 逐行照抄 <c>Tools/undistort_ds.py</c> 的 <c>ds_project</c>；shader HLSL 必须逐行照抄本函数以保 1:1。
    /// 纯 C#（零 UnityEngine，可秒测）。内参 [xi, alpha, fx, fy, cx, cy]，无单独畸变系数。
    /// 约定：相机系 +X 右、+Y 下（图像 v 向下）、+Z 光轴前（OpenCV 约定，与 ds_project 一致）。
    /// </summary>
    public readonly struct DoubleSphereProjection
    {
        private readonly double _xi, _alpha, _fx, _fy, _cx, _cy, _w2;
        private readonly int _w, _h;

        public DoubleSphereProjection(double xi, double alpha, double fx, double fy, double cx, double cy,
            int width, int height)
        {
            _xi = xi; _alpha = alpha; _fx = fx; _fy = fy; _cx = cx; _cy = cy;
            _w = width; _h = height;
            _w2 = ComputeW2(xi, alpha);
        }

        /// <summary>xi/alpha 决定的背后可见半角边界系数（valid: Z &gt; -w2·|d|）。</summary>
        public static double ComputeW2(double xi, double alpha)
        {
            double w1 = alpha <= 0.5 ? alpha / (1.0 - alpha) : (1.0 - alpha) / alpha;
            return (w1 + xi) / Math.Sqrt(2.0 * w1 * xi + xi * xi + 1.0);
        }

        public double W2 => _w2;

        /// <summary>方向(X,Y,Z) 不必归一化。out u,v 为像素；inFov=DS 有效域内（含图像外但可投的宽角）。</summary>
        public bool ProjectDirection(double x, double y, double z, out double u, out double v, out bool inFov)
        {
            double d1 = Math.Sqrt(x * x + y * y + z * z);
            double k = _xi * d1 + z;
            double d2 = Math.Sqrt(x * x + y * y + k * k);
            double norm = _alpha * d2 + (1.0 - _alpha) * k;
            inFov = norm > 1e-6 && z > -_w2 * d1;
            double ns = inFov ? norm : 1.0;
            u = _fx * x / ns + _cx;
            v = _fy * y / ns + _cy;
            return inFov && u >= 0 && u <= _w && v >= 0 && v <= _h;
        }

        /// <summary>归一化 UV（u/w, v/h）；v 翻转/镜像留给 shader 处理。</summary>
        public bool ProjectToUV(double x, double y, double z, out double uNorm, out double vNorm, out bool inFov)
        {
            bool inImg = ProjectDirection(x, y, z, out double u, out double v, out inFov);
            uNorm = u / _w; vNorm = v / _h;
            return inImg;
        }
    }
}
