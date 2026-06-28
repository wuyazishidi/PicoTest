// Assets/Main/Core/Rendering/GazeServo.cs
using System;

namespace PicoTest.Core.Rendering
{
    /// <summary>
    /// 低速率注视伺服（设计 §6 混合转向的"慢"分量）：把云台/锚点角度以限速、带死区的方式
    /// 逼近目标注视角，避免追每次头部微动而引入延迟。纯角度运算（度），可单测。
    /// </summary>
    public static class GazeServo
    {
        /// <summary>把角度归一化到 (-180, 180]。</summary>
        public static double NormalizeDeg(double a)
        {
            a %= 360.0;
            if (a > 180.0) a -= 360.0;
            else if (a <= -180.0) a += 360.0;
            return a;
        }

        /// <summary>
        /// 单步伺服：current→target，最短角差，死区内不动，单步不超过 rate*dt。
        /// 返回新角度（已归一化到 (-180,180]）。
        /// </summary>
        public static double Step(double current, double target, double dt, double rateDegPerSec, double deadzoneDeg)
        {
            double diff = NormalizeDeg(target - current);
            if (Math.Abs(diff) <= deadzoneDeg) return current;

            double maxStep = rateDegPerSec * dt;
            double step = diff;
            if (step > maxStep) step = maxStep;
            else if (step < -maxStep) step = -maxStep;

            return NormalizeDeg(current + step);
        }
    }
}
