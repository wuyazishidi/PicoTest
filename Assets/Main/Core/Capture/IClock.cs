// Assets/Main/Core/Capture/IClock.cs
using System.Diagnostics;

namespace PicoTest.Core.Capture
{
    public interface IClock { long NowNs(); }

    /// <summary>Stopwatch 单调纳秒时钟（线程安全），对齐 YC-Ego TimeBase 约定。</summary>
    public sealed class MonotonicClock : IClock
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        public long NowNs() => (long)(_sw.ElapsedTicks * (1_000_000_000.0 / Stopwatch.Frequency));
    }
}
