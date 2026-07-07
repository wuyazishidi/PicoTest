# 2026-07-06 — Exp-TrackerIMU：多 Tracker 无体追 IMU 验证（代码就绪，待真机）

计划：`.claude/plans/2026-07-06-multi-tracker-imu.md`。核心问题：**不开体追时 5 个 Motion Tracker 能否同时保持连接并持续输出 IMU**（决定能否脱离 YC-Ego 重写采集软件）。

## 做了什么

新建 `Assets/Experiments/Exp-TrackerIMU/`（PC 级全部完成）：

- `Scripts/TrackerImuProbe.cs` — 场景唯一入口：企业服务 Init→防御性 UnBind→Bind（bind 回调只置 volatile 标志，后续保活/体追/枚举全部回 Update 主线程，规避 YC-Ego「第二次 bind 回调不触发」+ Binder 线程禁 JNI 两个前科）；灭屏保活（ScreenOffDelay NEVER + AutoSleep/PSensor off）；连接真值 1Hz `GetSwiftTrackerDevices` 对账（`MotionTrackerConnectionAction` 只当触发器，它会漏断开）；轮询策略 RoundRobin（1 SN/帧）↔ FullEveryFrame 手柄 A/X 切换；`ENABLE_BODY_TRACKING` 编译开关（默认关）；每秒 `TrackerImuProbe.probe` logcat 探针 + 头显 TextMesh HUD
- `Scripts/TrackerImuStats.cs` — 纯 C# 在线统计：按 `imu_ts` 去重判新样本、单调性违规、分段新样本率
- `Scripts/ImuCsvLogger.cs` — samples.csv（每次轮询一行，含 stale + `is_new` 标记）+ events.csv（绑定/连接/断开/策略/电量），InvariantCulture，写失败静默停写不拖垮轮次
- `Editor/TrackerImuSceneBuilder.cs` — 菜单 `PicoTest/Tracker IMU/Build Test Scene` 生成极简场景；`Enable/Disable Body Tracking Define` 切 Android scripting define
- `Tests/` — EditMode 14 条（stats 去重/单调/速率/摘要 + CSV 表头/列数/de-DE 文化不变性/Flush）
- `Tools/analyze_imu_test.py` — 按 SN×策略输出 null 率/新 Hz/ts 间隔分布/单调违规/|a|/|w| 范围 + P1~P3 判定（已用合成数据自测；`imu_ts` 量纲按间隔中位数自动猜 ns/us/ms，真机数据后确认）

## 测试结果

- 编译通过（直连 RPC `TriggerCompile`，三个新程序集均生成）
- EditMode 89/89 + PlayMode 8/8（skip 1，既有）全绿，`.gates/tests-green` 已写
- 修了 1 个测试自身 bug：writer 未关时 `File.ReadAllLines` 在 Windows 共享冲突 → 改 `FileShare.ReadWrite` 读

## 基建发现（重要）

**YIUIMCP RPC 之前 502 不是 Unity 忙，是本机 HTTP 代理拦截了 127.0.0.1** —— curl 加 `--noproxy "*"` 立即 200。此前 memory 记的「轮询等 Unity 空闲」对这台机器不完全对。

## 真机首日实测（2026-07-06 下午，PICO 4U，5× Tracker）

**核心问题当场有了初步答案：无体追时 5 Tracker IMU 完全可用。**

- 启动 0.1s 内 5 个全连（无体追、无校准），RR 策略下每路稳定 **18.0Hz**（90fps÷5 理论值），
  帧率满 90，零 null、零单调违规，运行 30+ 分钟无系统主动断开（中途 3 次断开为人为按电源键，均自动重连）。
- **配对上限实验**（App 内绕过系统面板）：`StartSwiftTrackerPairing(6/7/8/0)` 全部 rc=0（受理），
  但第 6 个实体 Tracker 在配对模式下始终不被绑定；`dump` 全量枚举确认服务设备表**恰好 5 台全 bind=1**，
  第 6 个完全不可见。→ **强证据：5 = 系统服务端槽位上限**。鉴别实验 B（unbond 一个再配回）用户选择不执行
  （保留现有配对），故「API 用法不完整」的备择解释未 100% 排除。
- **工具链发现**：手柄按键两条输入通道（XR InputDevices + Input System）真机全程无响应
  `ctl[xr=0 is=0]`（原因未查明）→ 加了 `files/imu_test/cmd.txt` adb 命令通道
  （`pair/unbond/strat/dump`），PC 可直接驱动实验，比手柄可靠。
- HUD：真机包裁剪 `LegacyRuntime.fontsettings`（引擎打一条 E 日志后返回 null），回退链生效。
- 电量量纲：SwiftDevice.battery 返回 7~10 且会话内 8→7 递减 → **0~10 级**，不是百分比（YC-Ego 遗留疑问解决）。
- 配对/校准结论：配对可全 App 内（企业 API，槽位内有效）；校准只能 App 内拉起系统 single-glance 流程；
  纯 IMU 路线完全不需要校准。

## 遗留（设备到位后）

1. 编辑器里跑菜单 `PicoTest/Tracker IMU/Build Test Scene` 生成场景（本轮未自动执行：RPC ExecuteMenu 会静默丢弃编辑器当前未保存场景，人来做）
2. Release 构建（Development Build 有 CheckJNI 崩溃前科）只含该场景 → 真机 R1（体追关+RR，10min）/ R2（体追关+FULL，5min）/ R3（体追开+RR，5min，先菜单开 define）
3. `adb pull .../files/imu_test/` → `python Tools/analyze_imu_test.py` → 结论进 `Docs/decisions.md`（支持 / 有条件支持需哑体追 / 不支持 + P3 采样率参考值）
4. 风险预告：P1 隐性失败模式 = 无体追时系统只维持 ≤3 连接（配对上限可能绑体追配置）；判据不依赖融合速度字段（tracker 个体恒零前科）
