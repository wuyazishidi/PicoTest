# Exp-TrackerIMU — 多 Motion Tracker 无体追 IMU 通道验证

计划：`.claude/plans/2026-07-06-multi-tracker-imu.md`
参考调研：YC-Ego `docs/tracker-6dof-guide.md`、`docs/tracker-imu-analysis.md`

## 核心问题

**不开体追（不调 `StartBodyTracking`）时，5 个 PICO Motion Tracker 能否同时保持连接并持续输出 IMU 数据？**

| # | 验证项 | 通过判据 |
|---|---|---|
| P1 | 多 Tracker 连接保持（无体追） | 5 个 Tracker 保持连接 ≥10 分钟，无系统主动断开 |
| P2 | 每路 IMU 数据有效（无体追） | 每 SN `GetSwiftIMUData` 持续非 null，`timestamp` 单调递增，加速度/角速度随晃动变化 |
| P3 | 实际可达采样率 | round-robin 与 每帧全量 两档轮询的每 Tracker 有效新样本率（按 `timestamp` 去重） |
| 对照 | 体追开时行为一致 | `ENABLE_BODY_TRACKING` 编译开关打开重跑 P2，与 YC-Ego 实测交叉印证 |

## 使用方法

1. **生成场景**：菜单 `PicoTest/Tracker IMU/Generate Test Scene` → `Scenes/TrackerImuTest.unity`（已入库，一般无需重新生成）
2. **体追开关**：菜单 `PicoTest/Tracker IMU/Enable Body Tracking Define`（给 Android 加 `ENABLE_BODY_TRACKING`，默认关；R3 对照轮用）
3. **打包**：菜单 `PicoTest/Tracker IMU/Build APK (in-editor)` → `Builds/PicoTest-TrackerImu.apk`（体追开关打开时自动命名 `-bt` 后缀，防混包）。Release 构建（Development Build 有 CheckJNI 崩溃前科），只含本场景
4. **真机轮次**（重启 App 换轮次，R2→R3 建议重启头显）：
   - R1：体追关 + round-robin，10min（前 5min 静置、后 5min 逐个晃动）→ P1+P2
   - R2：体追关 + 每帧全量，5min → P3
   - R3：体追开 + round-robin，5min → 对照
5. **策略切换**：手柄 A/X 键（任一手）在 round-robin ↔ 每帧全量 间切换；HUD 实时显示
6. **取数**：`adb pull /sdcard/Android/data/<pkg>/files/imu_test/` → `python Tools/analyze_imu_test.py <目录>`
7. **看探针**：`adb logcat -s Unity | grep TrackerImuProbe.probe`（每秒一行：连接数/每 SN 新样本率/ts 龄）

## 落盘格式

每次启动新建 `imu_test/<UTC时间戳>_bt{0|1}/`：
- `samples.csv`：每次轮询一行（含 stale，`is_new` 标记按 `imu_ts` 去重）
- `events.csv`：绑定/枚举/连接/断开/策略切换事件（含每 Tracker 电量）

## 判据结论去向

`Docs/decisions.md`：「多 Tracker IMU 采集：支持 / 有条件支持（需哑体追）/ 不支持」+ P3 采样率参考值。
结果日志：`Docs/journal/2026-07-XX-multi-tracker-imu.md`
