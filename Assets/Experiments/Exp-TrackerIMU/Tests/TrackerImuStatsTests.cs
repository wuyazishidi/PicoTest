using NUnit.Framework;

namespace PicoTest.Experiments.TrackerIMU.Tests
{
    public class TrackerImuStatsTests
    {
        const string Sn = "PA7B10DGH72801234";

        [Test]
        public void SameTimestampTwice_SecondIsStale()
        {
            var s = new TrackerImuStats();
            Assert.IsTrue(s.Feed(Sn, true, 100, 0));
            Assert.IsFalse(s.Feed(Sn, true, 100, 10));
            var st = s.BySn[Sn];
            Assert.AreEqual(2, st.PollCount);
            Assert.AreEqual(1, st.NewCount);
            Assert.AreEqual(0, st.NullCount);
        }

        [Test]
        public void NullPoll_CountedNotNew()
        {
            var s = new TrackerImuStats();
            Assert.IsFalse(s.Feed(Sn, false, 0, 0));
            var st = s.BySn[Sn];
            Assert.AreEqual(1, st.PollCount);
            Assert.AreEqual(1, st.NullCount);
            Assert.AreEqual(0, st.NewCount);
        }

        [Test]
        public void DecreasingTimestamp_CountsNonMonotonicButStillNew()
        {
            var s = new TrackerImuStats();
            s.Feed(Sn, true, 200, 0);
            Assert.IsTrue(s.Feed(Sn, true, 150, 10)); // 回退：算新样本 + 记违规
            var st = s.BySn[Sn];
            Assert.AreEqual(1, st.NonMonotonic);
            Assert.AreEqual(2, st.NewCount);
        }

        [Test]
        public void NewHz_CountsPerSegmentSeconds()
        {
            var s = new TrackerImuStats();
            for (int i = 0; i < 10; i++)
                s.Feed(Sn, true, 1000 + i, i * 200); // 10 个新样本铺满 0~1800ms
            double hz = s.BySn[Sn].NewHz(2000);
            Assert.AreEqual(5.0, hz, 0.01);
        }

        [Test]
        public void NewHz_SegmentTooShort_ReturnsZero()
        {
            var s = new TrackerImuStats();
            s.Feed(Sn, true, 1, 0);
            Assert.AreEqual(0.0, s.BySn[Sn].NewHz(100));
        }

        [Test]
        public void ResetSegment_ClearsCountsButKeepsDedupState()
        {
            var s = new TrackerImuStats();
            s.Feed(Sn, true, 100, 0);
            s.Feed(Sn, false, 0, 10);
            s.ResetSegment(1000);
            var st = s.BySn[Sn];
            Assert.AreEqual(0, st.PollCount);
            Assert.AreEqual(0, st.NullCount);
            Assert.AreEqual(0, st.NewCount);
            Assert.AreEqual(1000, st.SegmentStartWallMs);
            // LastTs 保留：跨段重复 ts 仍判 stale
            Assert.IsFalse(s.Feed(Sn, true, 100, 1010));
        }

        [Test]
        public void Summary_ShowsSnTailAndAnomalyMarkers()
        {
            var s = new TrackerImuStats();
            s.Feed(Sn, true, 200, 0);
            s.Feed(Sn, true, 150, 10);   // mono!
            s.Feed(Sn, false, 0, 20);    // null!
            string sum = s.Summary(30);
            StringAssert.Contains("1234:", sum);   // SN 尾 4 位
            StringAssert.Contains("mono!1", sum);
            StringAssert.Contains("null!1", sum);
        }

        [Test]
        public void Summary_Empty_DoesNotThrow()
        {
            Assert.AreEqual("(no trackers)", new TrackerImuStats().Summary(0));
        }

        [Test]
        public void MultipleSns_TrackedIndependently()
        {
            var s = new TrackerImuStats();
            s.Feed("SN_A", true, 100, 0);
            s.Feed("SN_B", true, 100, 0);
            s.Feed("SN_A", true, 100, 10);
            Assert.AreEqual(1, s.BySn["SN_A"].NewCount);
            Assert.AreEqual(2, s.BySn["SN_A"].PollCount);
            Assert.AreEqual(1, s.BySn["SN_B"].NewCount);
            Assert.AreEqual(1, s.BySn["SN_B"].PollCount);
        }
    }
}
