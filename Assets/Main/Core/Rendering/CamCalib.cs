// Assets/Main/Core/Rendering/CamCalib.cs
using System;
using Newtonsoft.Json;

namespace PicoTest.Core.Rendering
{
    public enum Eye { Left, Right }

    /// <summary>
    /// 设备真实 VST 鱼眼双目标定（来自 /sdcard/PicoCalib/cam_calib.json，已烤进 Resources/PicoCalib/cam_calib）。
    /// 纯 C#（Main.Core 禁 UnityEngine；Newtonsoft 由 overrideReferences=false 自动引用）。
    /// 模型 equiDis62：等距鱼眼，畸变顺序 k1,k2,k3,k4,k5,k6,p1,p2 —— 本工程沿用 6 径向项，丢弃切向 p1/p2
    /// （量级 ~1e-3，与 <see cref="FisheyeProjection"/> 的简化一致）。
    /// </summary>
    public sealed class CamCalib
    {
        [JsonProperty("model")] public string Model { get; set; }
        [JsonProperty("distortion_param_order")] public string DistortionParamOrder { get; set; }
        [JsonProperty("resolution_per_eye_wh")] public int[] ResolutionPerEyeWh { get; set; }
        [JsonProperty("stereo_baseline_m")] public double StereoBaselineM { get; set; }
        [JsonProperty("left")] public EyeCalib Left { get; set; }
        [JsonProperty("right")] public EyeCalib Right { get; set; }

        public int Width => ResolutionPerEyeWh != null && ResolutionPerEyeWh.Length >= 1 ? ResolutionPerEyeWh[0] : 0;
        public int Height => ResolutionPerEyeWh != null && ResolutionPerEyeWh.Length >= 2 ? ResolutionPerEyeWh[1] : 0;

        public EyeCalib Get(Eye eye) => eye == Eye.Left ? Left : Right;

        /// <summary>从 JSON 字符串解析；失败抛 <see cref="FormatException"/>。</summary>
        public static CamCalib Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new FormatException("CamCalib: empty json");

            CamCalib c;
            try { c = JsonConvert.DeserializeObject<CamCalib>(json); }
            catch (Exception e) { throw new FormatException($"CamCalib: bad json — {e.Message}", e); }

            if (c == null) throw new FormatException("CamCalib: json deserialized to null");
            c.Validate();
            return c;
        }

        private void Validate()
        {
            if (Left == null || Right == null) throw new FormatException("CamCalib: missing left/right");
            if (Width <= 0 || Height <= 0) throw new FormatException("CamCalib: missing resolution_per_eye_wh");
            Left.Validate("left");
            Right.Validate("right");
        }

        /// <summary>
        /// 产出指定眼的 <see cref="FisheyeProjection"/>（纯数学）。畸变取 D[0..5]=k1..k6，丢 p1/p2。
        /// 外参 R(eye→robotHead) 默认单位阵；坐标系换算（OpenCV→Unity / T_imu_to_cam 反演）尚需设备验证，
        /// 由调用方通过 <paramref name="rEyeRowMajor"/> 传入，避免在此处臆测约定。
        /// </summary>
        public FisheyeProjection ToProjection(Eye eye, double thetaMaxRad, double[] rEyeRowMajor = null)
        {
            var e = Get(eye);
            var d = e.D;
            return new FisheyeProjection(
                e.Fx, e.Fy, e.Cx, e.Cy,
                d[0], d[1], d[2], d[3], d[4], d[5],
                Width, Height, thetaMaxRad, rEyeRowMajor);
        }
    }

    /// <summary>单眼标定。K/T 为行主序嵌套数组，D 为 8 畸变系数。</summary>
    public sealed class EyeCalib
    {
        [JsonProperty("K")] public double[][] K { get; set; }
        [JsonProperty("D")] public double[] D { get; set; }
        [JsonProperty("T_imu_to_cam")] public double[][] TImuToCam { get; set; }
        [JsonProperty("K_rectified")] public double[][] KRectified { get; set; }

        public double Fx => K[0][0];
        public double Fy => K[1][1];
        public double Cx => K[0][2];
        public double Cy => K[1][2];

        public void Validate(string who)
        {
            if (K == null || K.Length < 3 || K[0].Length < 3 || K[1].Length < 3)
                throw new FormatException($"CamCalib.{who}: bad K");
            if (D == null || D.Length < 6)
                throw new FormatException($"CamCalib.{who}: D needs >=6 coeffs, got {(D?.Length ?? 0)}");
        }
    }
}
