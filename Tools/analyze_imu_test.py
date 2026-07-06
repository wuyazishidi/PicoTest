#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""多 Tracker IMU 测试分析（Exp-TrackerIMU，计划 .claude/plans/2026-07-06-multi-tracker-imu.md）。

输入：TrackerImuProbe 落盘目录（含 samples.csv + events.csv），可传多个（R1/R2/R3 各一）。
输出：按 SN×策略 的有效率 / 新样本率 / timestamp 间隔分布 / 单调性，以及 P1~P3 判据结论。

用法：
    python Tools/analyze_imu_test.py <dir> [<dir> ...] [--p1-minutes 10]
"""
import argparse
import csv
import math
import os
import statistics
import sys
from collections import defaultdict

# Windows 控制台默认 GBK，中文/上标字符会 UnicodeEncodeError —— 强制 UTF-8
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.stderr.reconfigure(encoding="utf-8", errors="replace")


def read_csv(path):
    if not os.path.isfile(path):
        return []
    with open(path, newline="", encoding="utf-8") as f:
        return list(csv.DictReader(f))


def detect_ts_to_ms(intervals):
    """imu_ts 单位未在 SDK 文档言明——按新样本间隔中位数猜量纲，返回 ts→ms 的除数。"""
    if not intervals:
        return 1.0, "ms?"
    med = statistics.median(intervals)
    if med > 1e5:   # 期望采样间隔 ~几十 ms：中位数 >1e5 说明单位是 ns
        return 1e6, "ns"
    if med > 1e2:   # us
        return 1e3, "us"
    return 1.0, "ms"


def pct(values, p):
    if not values:
        return float("nan")
    s = sorted(values)
    k = min(len(s) - 1, max(0, int(round(p / 100.0 * (len(s) - 1)))))
    return s[k]


def analyze_dir(d, p1_minutes):
    samples = read_csv(os.path.join(d, "samples.csv"))
    events = read_csv(os.path.join(d, "events.csv"))
    if not samples:
        print(f"!! {d}: samples.csv 为空/缺失，跳过")
        return None

    bt = samples[0]["body_tracking"]
    print(f"\n{'=' * 78}\n目录 {d}   body_tracking={bt}   样本 {len(samples)} 行")

    # ---- 连接事件（P1）----
    connects, disconnects = [], []
    concurrent, max_concurrent = 0, 0
    t5 = None            # 第 5 个 tracker 到位时刻
    t_end = float(samples[-1]["wall_ms"])
    for e in events:
        if e["event"] == "CONNECT":
            connects.append(e)
            concurrent += 1
            max_concurrent = max(max_concurrent, concurrent)
            if concurrent == 5 and t5 is None:
                t5 = float(e["wall_ms"])
        elif e["event"] == "DISCONNECT":
            disconnects.append(e)
            concurrent -= 1
    held_min = (t_end - t5) / 60000.0 if t5 is not None else 0.0
    print(f"\n[P1] 连接：CONNECT={len(connects)}  DISCONNECT={len(disconnects)}  "
          f"峰值同连={max_concurrent}  5连保持={held_min:.1f}min")
    if disconnects:
        for e in disconnects:
            print(f"     断开 @{float(e['wall_ms']) / 60000.0:.1f}min  {e['detail']}")
    p1 = "PASS" if (len(disconnects) == 0 and max_concurrent >= 5 and held_min >= p1_minutes) else \
         ("FAIL(有断开)" if disconnects else f"未达标(峰值{max_concurrent}连/{held_min:.1f}min)")
    print(f"     P1 = {p1}")

    # ---- 按 策略×SN 分组（P2/P3）----
    groups = defaultdict(list)
    for r in samples:
        groups[(r["strategy"], r["sn"])].append(r)

    print(f"\n[P2/P3] 按 策略×SN：")
    hdr = f"{'策略':<5} {'SN尾4':<6} {'轮询':>7} {'null%':>6} {'新样本':>7} {'新Hz':>6} " \
          f"{'ts间隔ms p50/p95/max':>22} {'单调违规':>5} {'|a|范围 m/s²':>14} {'|w|范围 rad/s':>14}"
    print(hdr)
    print("-" * len(hdr))
    p2_fail = []
    for (strat, sn), rows in sorted(groups.items()):
        polls = len(rows)
        nulls = sum(1 for r in rows if r["poll_ok"] == "0")
        news = [r for r in rows if r["is_new"] == "1"]
        dur_s = (float(rows[-1]["wall_ms"]) - float(rows[0]["wall_ms"])) / 1000.0
        new_hz = len(news) / dur_s if dur_s > 1 else float("nan")

        ts = [int(r["imu_ts"]) for r in news]
        raw_iv = [b - a for a, b in zip(ts, ts[1:])]
        div, unit = detect_ts_to_ms([abs(v) for v in raw_iv if v != 0])
        iv_ms = [v / div for v in raw_iv if v > 0]
        mono_bad = sum(1 for v in raw_iv if v < 0)

        amag = [math.dist((0, 0, 0), (float(r["ax"]), float(r["ay"]), float(r["az"]))) for r in news]
        wmag = [math.dist((0, 0, 0), (float(r["wx"]), float(r["wy"]), float(r["wz"]))) for r in news]
        a_rng = f"{min(amag):.1f}~{max(amag):.1f}" if amag else "-"
        w_rng = f"{min(wmag):.2f}~{max(wmag):.2f}" if wmag else "-"

        null_pct = 100.0 * nulls / polls if polls else 0.0
        iv_str = f"{pct(iv_ms, 50):.1f}/{pct(iv_ms, 95):.1f}/{max(iv_ms):.1f}" if iv_ms else "-"
        print(f"{strat:<6} {sn[-4:]:<6} {polls:>7} {null_pct:>5.1f}% {len(news):>7} {new_hz:>6.1f} "
              f"{iv_str:>22} {mono_bad:>5} {a_rng:>14} {w_rng:>14}   (ts单位≈{unit})")

        # P2：null 率 <1%、无单调违规、加速度幅值有可辨变化（晃动段 |a| 波动 >2 m/s²）
        if null_pct >= 1.0 or mono_bad > 0 or (amag and (max(amag) - min(amag)) < 2.0):
            p2_fail.append((strat, sn[-4:], f"null={null_pct:.1f}% mono={mono_bad} a_span="
                            f"{(max(amag) - min(amag)) if amag else 0:.1f}"))

    if p2_fail:
        print(f"\n     P2 = 存疑（人工复核以下组；|a| 无变化可能只是该轮次没晃动该 tracker）")
        for f in p2_fail:
            print(f"       - {f}")
    else:
        print(f"\n     P2 = PASS（全组 null<1%、ts 单调、加速度随动明显）")

    print(f"\n[P3] 新样本率（Hz）即上表「新Hz」列，按 RR / FULL 两档分别读取。")
    return {"dir": d, "p1": p1, "p2_fail": p2_fail}


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("dirs", nargs="+", help="TrackerImuProbe 输出目录（含 samples.csv/events.csv）")
    ap.add_argument("--p1-minutes", type=float, default=10.0, help="P1 需要的 5 连保持分钟数（默认 10）")
    args = ap.parse_args()

    results = [analyze_dir(d, args.p1_minutes) for d in args.dirs]
    results = [r for r in results if r]
    if not results:
        sys.exit(1)

    print(f"\n{'=' * 78}\n总结（结论进 Docs/decisions.md）：")
    for r in results:
        print(f"  {os.path.basename(r['dir'])}: P1={r['p1']}  P2={'PASS' if not r['p2_fail'] else '存疑'}")
    print("  判定：P1+P2 全过=「支持」；无体追断开=「有条件支持（需哑体追保连接）」。")


if __name__ == "__main__":
    main()
