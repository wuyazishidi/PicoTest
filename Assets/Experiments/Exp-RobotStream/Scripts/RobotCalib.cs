// Assets/Experiments/Exp-RobotStream/Scripts/RobotCalib.cs
using PicoTest.Core.Rendering;
using PicoTest.Experiments.VstPassthrough;   // ImuCamRig（复用 VstPassthrough 的外参自标定换算）
using PicoTest.Rendering;
using UnityEngine;

namespace PicoTest.Experiments.RobotStream
{
    /// <summary>
    /// 把 Pico 的 cam_calib.json 当"机器人相机"分离出正常标定参数 → 左右 <see cref="FisheyeCalibration"/>：
    /// 内参 K + 畸变 D[0..5]（k1..k6，切向丢弃）来自设备实测；外参经 <see cref="ImuCamRig"/> 从
    /// T_imu_to_cam 换算（判读实测 cam→imu）。将来真机器人 = 换 cam_calib.json，本助手不改。
    /// 纯静态、可单测；与 VstPassthroughFeeder.MakeCalib 同款换算，抽出来供本 demo 与测试共用。
    /// </summary>
    public static class RobotCalib
    {
        /// <summary>返回左右眼标定。useExtrinsics=false 时外参用单位阵（对照）。</summary>
        public static (FisheyeCalibration left, FisheyeCalibration right) BuildEyeCalibrations(
            CamCalib calib, bool useExtrinsics)
        {
            Quaternion extL = Quaternion.identity, extR = Quaternion.identity;
            if (useExtrinsics)
            {
                var rig = ImuCamRig.FromCalib(calib);
                extL = ToQuaternion(rig.LeftREye);
                extR = ToQuaternion(rig.RightREye);
            }
            return (
                MakeEye("RobotLeft", calib, Eye.Left, extL),
                MakeEye("RobotRight", calib, Eye.Right, extR));
        }

        private static FisheyeCalibration MakeEye(string name, CamCalib c, Eye eye, Quaternion ext)
        {
            var e = c.Get(eye);
            var so = ScriptableObject.CreateInstance<FisheyeCalibration>();
            so.name = name;
            so.fx = (float)e.Fx; so.fy = (float)e.Fy; so.cx = (float)e.Cx; so.cy = (float)e.Cy;
            so.k1 = (float)e.D[0]; so.k2 = (float)e.D[1]; so.k3 = (float)e.D[2];
            so.k4 = (float)e.D[3]; so.k5 = (float)e.D[4]; so.k6 = (float)e.D[5];
            so.width = c.Width; so.height = c.Height;
            so.extrinsicRotation = ext;
            return so;
        }

        /// <summary>行主序 3x3（列=基向量像）→ Quaternion；R_eye 为真旋转（det=+1，ImuCamRig 保证）。</summary>
        public static Quaternion ToQuaternion(double[] r)
        {
            var fwd = new Vector3((float)r[2], (float)r[5], (float)r[8]); // R·ẑ
            var up = new Vector3((float)r[1], (float)r[4], (float)r[7]);  // R·ŷ
            return Quaternion.LookRotation(fwd, up);
        }
    }
}
