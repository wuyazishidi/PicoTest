using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace PicoTest.Experiments.TrackerIMU
{
    /// <summary>
    /// IMU 测试 CSV 落盘（samples.csv + events.csv）。测试量小（每帧全量 5 Tracker @72fps ≈ 27KB/s），
    /// CSV 足够，不必二进制。全部 InvariantCulture；写失败只在首个异常上报一次然后静默停写，
    /// 不允许 IO 问题拖垮真机轮次本体。
    /// </summary>
    public class ImuCsvLogger : IDisposable
    {
        public const string SamplesHeader =
            "wall_ms,frame,strategy,body_tracking,sn,poll_ok,is_new,imu_ts," +
            "ax,ay,az,wx,wy,wz,vx,vy,vz,w_ax,w_ay,w_az";
        public const string EventsHeader = "wall_ms,event,detail";

        /// <summary>首个写失败异常（null=一切正常）。探针每秒检查并上抛到日志。</summary>
        public Exception WriteError { get; private set; }
        public long SampleRows { get; private set; }
        public string Dir { get; }

        StreamWriter _samples;
        StreamWriter _events;
        readonly StringBuilder _sb = new StringBuilder(256);

        public ImuCsvLogger(string dir)
        {
            Dir = dir;
            try
            {
                Directory.CreateDirectory(dir);
                _samples = new StreamWriter(Path.Combine(dir, "samples.csv"), false, new UTF8Encoding(false));
                _samples.WriteLine(SamplesHeader);
                _events = new StreamWriter(Path.Combine(dir, "events.csv"), false, new UTF8Encoding(false));
                _events.WriteLine(EventsHeader);
            }
            catch (Exception e) { Fail(e); }
        }

        public void WriteSample(double wallMs, long frame, string strategy, bool bodyTracking,
            string sn, bool pollOk, bool isNew, long imuTs,
            double ax, double ay, double az, double wx, double wy, double wz,
            double vx, double vy, double vz, double wAx, double wAy, double wAz)
        {
            if (_samples == null) return;
            var c = CultureInfo.InvariantCulture;
            _sb.Clear();
            _sb.Append(wallMs.ToString("F2", c)).Append(',').Append(frame).Append(',')
               .Append(strategy).Append(',').Append(bodyTracking ? 1 : 0).Append(',')
               .Append(sn).Append(',').Append(pollOk ? 1 : 0).Append(',').Append(isNew ? 1 : 0).Append(',')
               .Append(imuTs).Append(',')
               .Append(ax.ToString("R", c)).Append(',').Append(ay.ToString("R", c)).Append(',').Append(az.ToString("R", c)).Append(',')
               .Append(wx.ToString("R", c)).Append(',').Append(wy.ToString("R", c)).Append(',').Append(wz.ToString("R", c)).Append(',')
               .Append(vx.ToString("R", c)).Append(',').Append(vy.ToString("R", c)).Append(',').Append(vz.ToString("R", c)).Append(',')
               .Append(wAx.ToString("R", c)).Append(',').Append(wAy.ToString("R", c)).Append(',').Append(wAz.ToString("R", c));
            try { _samples.WriteLine(_sb.ToString()); SampleRows++; }
            catch (Exception e) { Fail(e); }
        }

        /// <summary>detail 内的逗号会破坏 CSV 列 —— 调用方保证 detail 用分号分隔字段。</summary>
        public void WriteEvent(double wallMs, string evt, string detail)
        {
            if (_events == null) return;
            try { _events.WriteLine($"{wallMs.ToString("F2", CultureInfo.InvariantCulture)},{evt},{detail}"); }
            catch (Exception e) { Fail(e); }
        }

        /// <summary>探针 1Hz 调用：把缓冲落到磁盘，掉电/崩溃最多丢 1 秒。</summary>
        public void Flush()
        {
            try { _samples?.Flush(); _events?.Flush(); }
            catch (Exception e) { Fail(e); }
        }

        void Fail(Exception e)
        {
            if (WriteError == null) WriteError = e;
            try { _samples?.Dispose(); } catch { }
            try { _events?.Dispose(); } catch { }
            _samples = null;
            _events = null;
        }

        public void Dispose()
        {
            try { _samples?.Flush(); _samples?.Dispose(); } catch { }
            try { _events?.Flush(); _events?.Dispose(); } catch { }
            _samples = null;
            _events = null;
        }
    }
}
