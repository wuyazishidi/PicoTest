using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PicoTest.Experiments.TrackerIMU
{
    /// <summary>
    /// 纯 C#（零 UnityEngine 依赖）的每 SN IMU 采样统计：按 imu_ts 去重判"新样本"、
    /// 单调性违规计数、分段（策略切换后 Reset）有效新样本率。P2/P3 判据的在线版本，
    /// 离线权威数字由 Tools/analyze_imu_test.py 从 CSV 复算。
    /// </summary>
    public class TrackerImuStats
    {
        public class SnStats
        {
            public string Sn;
            public long PollCount;        // 本段内轮询次数（含返回 null）
            public long NullCount;        // 返回 null 的次数
            public long NewCount;         // imu_ts 与上次不同的次数
            public long NonMonotonic;     // imu_ts 比上一个新样本小的次数（P2 单调性违规）
            public long LastTs;           // 最近一次样本的 imu_ts（0=还没见过）
            public double LastNewWallMs;  // 最近一个新样本的本地时刻
            public double SegmentStartWallMs;

            /// <summary>本段有效新样本率（Hz）。段时长不足 0.5s 时返回 0 避免噪声数字。</summary>
            public double NewHz(double nowWallMs)
            {
                double sec = (nowWallMs - SegmentStartWallMs) / 1000.0;
                return sec < 0.5 ? 0.0 : NewCount / sec;
            }
        }

        readonly Dictionary<string, SnStats> _bySn = new Dictionary<string, SnStats>();

        public IReadOnlyDictionary<string, SnStats> BySn => _bySn;

        /// <summary>
        /// 喂入一次轮询结果。ok=false 表示 GetSwiftIMUData 返回 null（ts 忽略）。
        /// 返回该样本是否为"新样本"（imu_ts 首见或与上次不同）。
        /// </summary>
        public bool Feed(string sn, bool ok, long ts, double wallMs)
        {
            if (!_bySn.TryGetValue(sn, out var s))
            {
                s = new SnStats { Sn = sn, SegmentStartWallMs = wallMs };
                _bySn[sn] = s;
            }
            s.PollCount++;
            if (!ok) { s.NullCount++; return false; }

            bool isNew = ts != s.LastTs;
            if (isNew)
            {
                if (s.LastTs != 0 && ts < s.LastTs) s.NonMonotonic++;
                s.NewCount++;
                s.LastTs = ts;
                s.LastNewWallMs = wallMs;
            }
            return isNew;
        }

        /// <summary>策略切换时调用：清零分段计数（保留 LastTs 以维持去重与单调性判断的连续性）。</summary>
        public void ResetSegment(double wallMs)
        {
            foreach (var s in _bySn.Values)
            {
                s.PollCount = 0;
                s.NullCount = 0;
                s.NewCount = 0;
                s.SegmentStartWallMs = wallMs;
            }
        }

        /// <summary>
        /// 单行摘要（探针日志/HUD 用），每 SN 显示尾 4 位：`8CA1:17.9Hz age=12ms mono!2 null!5`
        /// （mono!/null! 仅在非零时出现）。
        /// </summary>
        public string Summary(double nowWallMs)
        {
            var sb = new StringBuilder();
            foreach (var s in _bySn.Values)
            {
                if (sb.Length > 0) sb.Append(" | ");
                string tail = s.Sn != null && s.Sn.Length > 4 ? s.Sn.Substring(s.Sn.Length - 4) : s.Sn;
                double ageMs = s.NewCount > 0 ? nowWallMs - s.LastNewWallMs : -1;
                sb.Append(tail).Append(':')
                  .Append(s.NewHz(nowWallMs).ToString("F1", CultureInfo.InvariantCulture)).Append("Hz");
                sb.Append(" age=").Append(ageMs < 0 ? "-" : ((long)ageMs).ToString(CultureInfo.InvariantCulture) + "ms");
                if (s.NonMonotonic > 0) sb.Append(" mono!").Append(s.NonMonotonic);
                if (s.NullCount > 0) sb.Append(" null!").Append(s.NullCount);
            }
            return sb.Length == 0 ? "(no trackers)" : sb.ToString();
        }
    }
}
