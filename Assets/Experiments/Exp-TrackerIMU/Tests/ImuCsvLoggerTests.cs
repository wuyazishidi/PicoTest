using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace PicoTest.Experiments.TrackerIMU.Tests
{
    public class ImuCsvLoggerTests
    {
        string _dir;
        CultureInfo _prevCulture;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ImuCsvLoggerTests_" + Path.GetRandomFileName());
            _prevCulture = Thread.CurrentThread.CurrentCulture;
        }

        [TearDown]
        public void TearDown()
        {
            Thread.CurrentThread.CurrentCulture = _prevCulture;
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void CreatesBothFilesWithHeaders()
        {
            using (new ImuCsvLogger(_dir)) { }
            Assert.AreEqual(ImuCsvLogger.SamplesHeader, File.ReadLines(Path.Combine(_dir, "samples.csv")).First());
            Assert.AreEqual(ImuCsvLogger.EventsHeader, File.ReadLines(Path.Combine(_dir, "events.csv")).First());
        }

        [Test]
        public void SampleRow_ColumnCountMatchesHeader()
        {
            using (var log = new ImuCsvLogger(_dir))
            {
                log.WriteSample(12.345, 7, "RR", false, "SN01", true, true, 123456789L,
                    1.5, -2.25, 9.81, 0.1, 0.2, 0.3, 0, 0, 0, -0.5, 0.5, 1e-9);
            }
            var lines = File.ReadAllLines(Path.Combine(_dir, "samples.csv"));
            Assert.AreEqual(2, lines.Length);
            int headerCols = ImuCsvLogger.SamplesHeader.Split(',').Length;
            Assert.AreEqual(headerCols, lines[1].Split(',').Length);
        }

        [Test]
        public void DecimalSeparator_InvariantUnderGermanCulture()
        {
            // de-DE 用逗号做小数点——若未用 InvariantCulture，CSV 列会被撑爆
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            using (var log = new ImuCsvLogger(_dir))
            {
                log.WriteSample(1.5, 1, "FULL", true, "SN01", true, false, 42L,
                    1.25, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                log.WriteEvent(2.5, "TEST", "detail");
            }
            var sample = File.ReadAllLines(Path.Combine(_dir, "samples.csv"))[1];
            int headerCols = ImuCsvLogger.SamplesHeader.Split(',').Length;
            Assert.AreEqual(headerCols, sample.Split(',').Length);
            StringAssert.Contains("1.25", sample);
            var evt = File.ReadAllLines(Path.Combine(_dir, "events.csv"))[1];
            Assert.AreEqual("2.50,TEST,detail", evt);
        }

        [Test]
        public void SampleRows_Counted()
        {
            using (var log = new ImuCsvLogger(_dir))
            {
                for (int i = 0; i < 3; i++)
                    log.WriteSample(i, i, "RR", false, "SN01", false, false, 0,
                        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                Assert.AreEqual(3, log.SampleRows);
                Assert.IsNull(log.WriteError);
            }
        }

        [Test]
        public void Flush_MakesRowsVisibleWithoutDispose()
        {
            using (var log = new ImuCsvLogger(_dir))
            {
                log.WriteEvent(1, "E1", "x");
                log.Flush();
                // writer 仍持有文件 —— 必须以 FileShare.ReadWrite 读，File.ReadAllLines 会共享冲突
                using var fs = new FileStream(Path.Combine(_dir, "events.csv"),
                    FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var r = new StreamReader(fs);
                var lines = r.ReadToEnd().TrimEnd('\r', '\n').Split('\n');
                Assert.AreEqual(2, lines.Length);
            }
        }
    }
}
