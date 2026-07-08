// Assets/Experiments/Exp-VstPassthrough/Scripts/ImuCamRig.cs
using System;
using PicoTest.Core.Rendering;

namespace PicoTest.Experiments.VstPassthrough
{
    /// <summary>
    /// 从 cam_calib.json 的 T_imu_to_cam 自标定出"Unity 头系 → 相机系"外参（纯 C#，零 UnityEngine，
    /// 晋升时可直接迁入 Main.Core）。
    ///
    /// 两个不臆测原则：
    /// 1. **T 的方向语义不信字段名**：对 "imu→cam"（p_cam=R·p_imu+t，轴=R 的行，中心=−Rᵀt）与
    ///    "cam→imu"（相机位姿，轴=R 的列，中心=t）两种解读分别打分——正确解读下双相机基线必与
    ///    图像横轴（相机 x 轴均值）共线，错误解读则近乎垂直。真机 A9410 数据实测：cam→imu 自洽。
    /// 2. **IMU 系轴向约定不臆测**：直接用标定自身构造头系基（IMU 坐标表示）：
    ///    X=左→右相机方向（用户右）、Z=双相机光轴均值（前）、Y=X×Z（右手系 ⇒ 上）。
    ///
    /// 输出 R_eye 与现管线（identity 外参 + flipV=1 画面正立）的**有效 y-up 相机系**对齐：
    /// R_eye = F · R(imu→cam) · M，其中 M 列 = (X,Y,Z)，F = diag(1,−1,1) 把 OpenCV y-down 翻成 y-up。
    /// F 与 M（各 det=−1）相抵，R_eye 为真旋转（det=+1），可安全转四元数进
    /// <c>FisheyeCalibration.extrinsicRotation</c>；shader 侧 c = mul(R_eye, d) 语义不变。
    /// </summary>
    public sealed class ImuCamRig
    {
        /// <summary>选中的 T 语义："cam_to_imu"（真机数据实测）或 "imu_to_cam"（字段名面值）。</summary>
        public string TReading { get; private set; }
        /// <summary>选中解读的基线/图像横轴共线度 |cos|，应接近 1。</summary>
        public double ConsistencyScore { get; private set; }
        /// <summary>相机中心距（应复原 stereo_baseline_m）。</summary>
        public double BaselineM { get; private set; }
        /// <summary>行主序 3x3：Unity 头系方向 → 有效相机系（y-up），直接喂 shader _LeftRot/_RightRot。</summary>
        public double[] LeftREye { get; private set; }
        public double[] RightREye { get; private set; }
        /// <summary>相机中心在头系的位置 (x,y,z)，头系原点=双相机中点。v1 仅供诊断/断言，平移补偿留后续。</summary>
        public double[] LeftCamPosHead { get; private set; }
        public double[] RightCamPosHead { get; private set; }

        private ImuCamRig() { }

        public static ImuCamRig FromCalib(CamCalib calib)
        {
            if (calib?.Left?.TImuToCam == null || calib.Right?.TImuToCam == null)
                throw new FormatException("ImuCamRig: 标定缺 T_imu_to_cam");

            var (rL, tL) = SplitT(calib.Left.TImuToCam, "left");
            var (rR, tR) = SplitT(calib.Right.TImuToCam, "right");

            // 两种解读打分：正确解读下 基线 ∥ 图像横轴
            var asImuToCam = (l: EyeGeomImuToCam(rL, tL), r: EyeGeomImuToCam(rR, tR));
            var asCamToImu = (l: EyeGeomCamToImu(rL, tL), r: EyeGeomCamToImu(rR, tR));
            double scoreItc = Consistency(asImuToCam.l, asImuToCam.r);
            double scoreCti = Consistency(asCamToImu.l, asCamToImu.r);

            bool camToImu = scoreCti >= scoreItc;
            var (gl, gr) = camToImu ? asCamToImu : asImuToCam;

            // 头系基（IMU 坐标表示）：X=用户右, Z=前, Y=X×Z=上（右手系）
            double[] x = Norm(Sub(gr.center, gl.center));
            double[] zRaw = Norm(Add(gl.axisZ, gr.axisZ));
            double[] z = Norm(Sub(zRaw, Scale(x, Dot(zRaw, x))));
            double[] y = Cross(x, z);
            // M 列 = X,Y,Z（行主序展开）
            double[] m =
            {
                x[0], y[0], z[0],
                x[1], y[1], z[1],
                x[2], y[2], z[2],
            };

            // R(imu→cam)：imu_to_cam 解读 = R 本身；cam→imu 解读（R=相机位姿）= Rᵀ
            double[] rCvL = camToImu ? Transpose3(rL) : rL;
            double[] rCvR = camToImu ? Transpose3(rR) : rR;

            double[] mid = Scale(Add(gl.center, gr.center), 0.5);
            return new ImuCamRig
            {
                TReading = camToImu ? "cam_to_imu" : "imu_to_cam",
                ConsistencyScore = camToImu ? scoreCti : scoreItc,
                BaselineM = Length(Sub(gr.center, gl.center)),
                LeftREye = FlipY(Mul3(rCvL, m)),
                RightREye = FlipY(Mul3(rCvR, m)),
                LeftCamPosHead = ToHeadFrame(gl.center, mid, x, y, z),
                RightCamPosHead = ToHeadFrame(gr.center, mid, x, y, z),
            };
        }

        // ── 解读几何 ──────────────────────────────────────────────

        private readonly struct EyeGeom
        {
            public readonly double[] center, axisX, axisZ;
            public EyeGeom(double[] c, double[] ax, double[] az) { center = c; axisX = ax; axisZ = az; }
        }

        /// <summary>imu→cam 解读：p_cam = R·p_imu + t ⇒ 相机轴 = R 的行，中心 = −Rᵀt。</summary>
        private static EyeGeom EyeGeomImuToCam(double[] r, double[] t)
        {
            double[] rt = Transpose3(r);
            return new EyeGeom(
                Scale(Apply3(rt, t), -1),
                new[] { r[0], r[1], r[2] },
                new[] { r[6], r[7], r[8] });
        }

        /// <summary>cam→imu 解读（相机在 IMU 系的位姿）：相机轴 = R 的列，中心 = t。</summary>
        private static EyeGeom EyeGeomCamToImu(double[] r, double[] t)
            => new EyeGeom(t,
                new[] { r[0], r[3], r[6] },
                new[] { r[2], r[5], r[8] });

        private static double Consistency(EyeGeom l, EyeGeom r)
        {
            double[] baseline = Sub(r.center, l.center);
            if (Length(baseline) < 1e-6) return 0;
            return Math.Abs(Dot(Norm(baseline), Norm(Add(l.axisX, r.axisX))));
        }

        private static (double[] r, double[] t) SplitT(double[][] t4, string who)
        {
            if (t4.Length < 3 || t4[0].Length < 4 || t4[1].Length < 4 || t4[2].Length < 4)
                throw new FormatException($"ImuCamRig: {who}.T_imu_to_cam 不是 3x4/4x4");
            return (
                new[] { t4[0][0], t4[0][1], t4[0][2], t4[1][0], t4[1][1], t4[1][2], t4[2][0], t4[2][1], t4[2][2] },
                new[] { t4[0][3], t4[1][3], t4[2][3] });
        }

        private static double[] ToHeadFrame(double[] p, double[] origin, double[] x, double[] y, double[] z)
        {
            double[] d = Sub(p, origin);
            return new[] { Dot(d, x), Dot(d, y), Dot(d, z) };
        }

        // ── double[3] / 行主序 double[9] 小工具 ──────────────────

        private static double[] Add(double[] a, double[] b) => new[] { a[0] + b[0], a[1] + b[1], a[2] + b[2] };
        private static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        private static double[] Scale(double[] a, double s) => new[] { a[0] * s, a[1] * s, a[2] * s };
        private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        private static double Length(double[] a) => Math.Sqrt(Dot(a, a));
        private static double[] Norm(double[] a)
        {
            double len = Length(a);
            if (len < 1e-12) throw new FormatException("ImuCamRig: 退化方向（零向量）");
            return Scale(a, 1.0 / len);
        }
        private static double[] Cross(double[] a, double[] b) => new[]
        {
            a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0],
        };
        private static double[] Transpose3(double[] r) => new[]
        {
            r[0], r[3], r[6],
            r[1], r[4], r[7],
            r[2], r[5], r[8],
        };
        private static double[] Apply3(double[] r, double[] v) => new[]
        {
            r[0] * v[0] + r[1] * v[1] + r[2] * v[2],
            r[3] * v[0] + r[4] * v[1] + r[5] * v[2],
            r[6] * v[0] + r[7] * v[1] + r[8] * v[2],
        };
        private static double[] Mul3(double[] a, double[] b)
        {
            var o = new double[9];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    o[i * 3 + j] = a[i * 3] * b[j] + a[i * 3 + 1] * b[3 + j] + a[i * 3 + 2] * b[6 + j];
            return o;
        }
        /// <summary>F·R，F=diag(1,−1,1)：翻第二行（OpenCV y-down → 管线有效 y-up）。</summary>
        private static double[] FlipY(double[] r) => new[]
        {
            r[0], r[1], r[2],
            -r[3], -r[4], -r[5],
            r[6], r[7], r[8],
        };
    }
}
