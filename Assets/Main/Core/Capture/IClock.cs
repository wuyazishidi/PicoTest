// Assets/Main/Core/Capture/IClock.cs
using System.Diagnostics;

namespace PicoTest.Core.Capture
{
    public interface IClock { long NowNs(); }

    /// <summary>
    /// Stopwatch 单调纳秒时钟（线程安全），对齐 YC-Ego TimeBase 约定。
    ///
    /// 精度论证（double 方案）：
    ///   _sw.ElapsedTicks 在典型 PC 上约为 10 MHz（100 ns 分辨率）的计数器，
    ///   运行约 100 天后 ticks ≈ 8.6×10¹³，远小于 double 的精确整数上限 2^53 ≈ 9×10¹⁵，
    ///   因此 (double)ticks 本身无精度损失；乘以常量 1e9/Frequency（自身误差 &lt; 1 ULP）
    ///   后最终误差 &lt; 0.02 ns，满足采集时间戳需求。
    ///
    ///   为何不用整数乘法（ticks * 1_000_000_000L / Frequency）：
    ///   ticks * 1_000_000_000L 约在 11 秒后（ticks ≈ 1.1×10⁸，乘积 ≈ 1.1×10¹⁷）溢出 long
    ///   （long.MaxValue ≈ 9.2×10¹⁸ 看似够用，但在 1 GHz 时钟下约 9.2 秒即溢出），
    ///   故整数路径不安全；double 乘法是此场景的正确选择。
    /// </summary>
    public sealed class MonotonicClock : IClock
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        public long NowNs() => (long)(_sw.ElapsedTicks * (1_000_000_000.0 / Stopwatch.Frequency));
    }
}
