// Assets/Experiments/Exp-RobotDsDome/Scripts/DsCamchainParser.cs
using System;
using System.Collections.Generic;
using System.Globalization;

namespace PicoTest.Experiments.RobotDsDome
{
    /// <summary>单目 DS 标定解析结果。</summary>
    public struct DsCam
    {
        public string model;
        public double xi, alpha, fx, fy, cx, cy;
        public int width, height;
    }

    /// <summary>双目 DS camchain（cam0=左, cam1=右）+ 基线（cam1 相对 cam0 平移模长）。</summary>
    public struct DsCamchain
    {
        public DsCam cam0, cam1;
        public double baselineM;
    }

    /// <summary>
    /// Kalibr camchain.yaml 最小解析器（Unity 无 YAML 库）。只取 cam0/cam1 的 ds 内参/分辨率
    /// 与 cam1 的 T_cn_cnm1 平移（基线）。纯 C#、可单测；格式为固定缩进的 Kalibr 输出。
    /// </summary>
    public static class DsCamchainParser
    {
        public static DsCamchain Parse(string yaml)
        {
            if (string.IsNullOrWhiteSpace(yaml)) throw new FormatException("camchain: empty");
            var lines = yaml.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

            var chain = new DsCamchain();
            string cur = null;                       // 当前 cam key
            bool inT = false; var tRows = new List<double[]>();

            foreach (var raw in lines)
            {
                string line = raw.TrimEnd();
                if (line.Length == 0) continue;

                // 顶格 camN:
                if (line[0] != ' ' && line.EndsWith(":"))
                {
                    cur = line.Substring(0, line.Length - 1).Trim();
                    inT = false;
                    continue;
                }
                if (cur == null) continue;
                string t = line.Trim();

                if (t.StartsWith("T_cn_cnm1:")) { inT = true; tRows.Clear(); continue; }
                if (inT)
                {
                    if (t.StartsWith("- ["))
                    {
                        var row = ParseFloats(t);
                        if (row.Length >= 4) tRows.Add(row);
                        if (tRows.Count >= 3 && cur == "cam1")
                        {
                            double tx = tRows[0][3], ty = tRows[1][3], tz = tRows[2][3];
                            chain.baselineM = Math.Sqrt(tx * tx + ty * ty + tz * tz);
                        }
                        continue;
                    }
                    inT = false; // 非 - [ 行结束矩阵块
                }

                if (t.StartsWith("camera_model:")) SetModel(ref chain, cur, After(t));
                else if (t.StartsWith("intrinsics:")) SetIntrinsics(ref chain, cur, ParseFloats(t));
                else if (t.StartsWith("resolution:")) SetResolution(ref chain, cur, ParseFloats(t));
            }

            Validate(chain.cam0, "cam0");
            Validate(chain.cam1, "cam1");
            return chain;
        }

        private static void Validate(DsCam c, string who)
        {
            if (c.model != "ds") throw new FormatException($"{who}: 期望 ds 模型，得到 '{c.model}'");
            if (c.width <= 0 || c.height <= 0) throw new FormatException($"{who}: 缺 resolution");
            if (c.fx == 0 && c.fy == 0) throw new FormatException($"{who}: 缺 intrinsics");
        }

        private static string After(string kv)
        {
            int i = kv.IndexOf(':');
            return i < 0 ? "" : kv.Substring(i + 1).Trim();
        }

        private static double[] ParseFloats(string s)
        {
            int a = s.IndexOf('['), b = s.LastIndexOf(']');
            if (a < 0 || b <= a) return Array.Empty<double>();
            var inner = s.Substring(a + 1, b - a - 1);
            var parts = inner.Split(',');
            var outv = new List<double>(parts.Length);
            foreach (var p in parts)
            {
                var q = p.Trim();
                if (q.Length == 0) continue;
                if (double.TryParse(q, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) outv.Add(d);
            }
            return outv.ToArray();
        }

        private static void SetModel(ref DsCamchain c, string cam, string m)
        { if (cam == "cam0") c.cam0.model = m; else if (cam == "cam1") c.cam1.model = m; }

        private static void SetIntrinsics(ref DsCamchain c, string cam, double[] v)
        {
            if (v.Length < 6) return;
            var d = new DsCam { xi = v[0], alpha = v[1], fx = v[2], fy = v[3], cx = v[4], cy = v[5] };
            if (cam == "cam0") { d.model = c.cam0.model; d.width = c.cam0.width; d.height = c.cam0.height; c.cam0 = d; }
            else if (cam == "cam1") { d.model = c.cam1.model; d.width = c.cam1.width; d.height = c.cam1.height; c.cam1 = d; }
        }

        private static void SetResolution(ref DsCamchain c, string cam, double[] v)
        {
            if (v.Length < 2) return;
            if (cam == "cam0") { c.cam0.width = (int)v[0]; c.cam0.height = (int)v[1]; }
            else if (cam == "cam1") { c.cam1.width = (int)v[0]; c.cam1.height = (int)v[1]; }
        }
    }
}
