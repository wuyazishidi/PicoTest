# 计划：验证多个 Motion Tracker 同时获取 IMU 数据（Exp-TrackerIMU）

## Context（背景 / 为什么）

后续可能脱离 YC-Ego 重新开发采集软件，前提是确认：**多个 PICO Motion Tracker 能否同时、持续地提供 IMU 数据**。

已知事实（来自 YC-Ego 调研，见 `../YC-Ego/docs/tracker-6dof-guide.md`、`tracker-imu-analysis.md`）：
- IMU 走企业版 TOB 通道：`PXR_Enterprise.GetSwiftIMUData(sn, predictTime)`，按序列号逐个调（无批量接口），
  单次 Binder 往返约 3ms（主线程）；
- **体追模式下** 5 个 Tracker 同连 + IMU 100% 有效已实测（YC-Ego `tracker.bin`，`imu_valid` 3000/3000）；
- **未验证**：新软件如果**不开体追**（`StartBodyTracking`），5 个 Tracker 的连接与 IMU 通道是否依然完好——
  这是本次测试要回答的核心问题；
- 设备：PICO 4 Ultra（A9210）OS 5.15.5；灭屏保活须设 `ScreenOffDelay(NEVER)`。

## 目标 & 验收

**核心问题：不依赖 YC-Ego 的运行条件（尤其是不开体追）时，多个 Tracker 能否同时获取 IMU 数据？**

| # | 验证项 | 通过判据 |
|---|---|---|
| P1 | 多 Tracker 连接保持（无体追） | 不调 `StartBodyTracking`，5 个 Tracker 保持连接 ≥10 分钟，无系统主动断开 |
| P2 | 每路 IMU 数据有效（无体追） | 每个 SN 的 `GetSwiftIMUData` 持续返回非 null，`timestamp` 单调递增，加速度/角速度随晃动明显变化 |
| P3 | 实际可达采样率 | 记录每 Tracker 有效新样本率（按 `timestamp` 去重）：主线程 round-robin 与 每帧全量 两种轮询各测一轮，得出参考数字 |
| 对照 | 体追开时行为一致性 | 开体追重复 P2 一轮，与 YC-Ego 已有实测交叉印证（数据应无差别） |

通过标准：P1 + P2 全过 → 结论「支持」，重写采集软件的技术地基成立；P1 失败（无体追时系统断开多余 Tracker）→ 结论「有条件支持」，新软件须保留哑体追（Start 后不读数据）维持连接，记入决策。

## 方案

### 落位

`Assets/Experiments/Exp-TrackerIMU/`（复制 `_TEMPLATE`），真机验证为主，PC 侧只做数据分析。

### 组件（极简）

1. **`TrackerImuProbe.cs`** — 场景唯一入口：
   - 绑企业服务（参考 YC-Ego `EnterpriseService.cs`；注意 bind 顺序坑：曾有第二次 bind 回调不触发的前科）；
   - `GetSwiftTrackerDevices()` 枚举 SN + 订阅 `MotionTrackerConnectionAction`；
   - 编译开关 `ENABLE_BODY_TRACKING`（默认关）覆盖「无体追 / 有体追」两种状态；
   - 每秒探针日志（logcat 可 grep：连接数、每 SN 最近 IMU timestamp 差）+ 屏上 HUD（免摘头显）。
2. **轮询策略**：主线程 round-robin（1 个/帧）与 每帧全量 两档，运行时按键切换（P3 用）。
3. **`ImuCsvLogger.cs`**：每条采样落盘（SN、策略、`IMUData` 全字段、本地 `NowNs`），CSV 即可（测试量小，不必二进制）。
4. **`Tools/analyze_imu_test.py`**：按 SN×策略统计有效率、timestamp 间隔分布 → 直接产出 P1~P3 判据数字。

### 真机轮次

| 轮次 | 体追 | 策略 | 时长 | 覆盖 |
|---|---|---|---|---|
| R1 | 关 | round-robin | 10min（前 5min 静置、后 5min 逐个晃动） | P1 + P2 |
| R2 | 关 | 每帧全量 | 5min | P3（含帧率代价观察） |
| R3 | 开 | round-robin | 5min | 对照轮 |

设备准备：5× Tracker 充满电、系统里完成配对；R3 前如需增强 5 配置则在系统设置校准；轮次间重启 App，R2→R3 建议重启头显（避免采集服务残留状态）。

### 新增文件

- `Assets/Experiments/Exp-TrackerIMU/Experiment.TrackerIMU.asmdef`（references: `Main.Core`, `PICO.TobSupport`, `Unity.XR.PICO`）
- `.../Scripts/TrackerImuProbe.cs`、`ImuCsvLogger.cs`
- `.../Editor/TrackerImuSceneBuilder.cs`（菜单生成极简场景）
- `Tools/analyze_imu_test.py`
- 结果记 `Docs/journal/2026-07-XX-multi-tracker-imu.md`，结论进 `Docs/decisions.md`

## 风险点

- **P1 的隐性失败模式**（本次测试的真正悬念）：无体追时系统可能只维持 ≤3 个连接（配对上限可能与体追配置绑定）；
- Tracker 个体差异前科：融合速度 `vx/vy/vz` 有个体恒零现象——判据只看原始加速度/角速度与 timestamp，不依赖速度字段；
- YIUIMCP 编译循环与 batchmode 构建互斥（宪法已知陷阱，照走）。

## 产出

`Docs/decisions.md` 新增一条：「多 Tracker IMU 采集：支持 / 有条件支持（需哑体追）/ 不支持」，附 P3 的每 Tracker 采样率参考值，作为新采集软件立项依据。
