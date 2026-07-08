# Exp-VstPassthrough — 自有管线复现 PICO 原生透视

设计：`Docs/designs/2026-07-08-vst-passthrough-demo.md`。**另起炉灶**：不改 `FisheyeDomeXRLive`（XRLiveDemo）与 `Assets/Main/` 任何代码，只引用复用（`Main.Vst.VstCamera` / `FisheyeDomeRenderer` / `FisheyeDome.shader` / `CamCalib`）。

## 目标

用"VST raw 鱼眼 → 穹顶"管线复现 PICO 原生透视（see-through）的对齐效果——头锁定、与真实世界 1:1、转头不拖影，可与原生透视 A/B 切换对比。这是遥操作管线正确性的验收手段。

## 与 XRLiveDemo 的差异

| | XRLiveDemo | 本实验 |
|---|---|---|
| 锚定 | WorldLocked + 云台伺服 | 头锁定（`head`）/ 捕获时刻位姿（`capture`，默认） |
| 外参 | 单位阵 | `T_imu_to_cam` 经 `ImuCamRig` 自标定换算进 shader |
| radius | 20m | 1.5m（原生透视重投影距离量级），运行时可调 |
| 调参 | 改码重打包 | adb cmd.txt 运行时调参 |

## 关键发现（已用测试锁定）

`cam_calib.json` 的 `T_imu_to_cam` **实为相机在 IMU 系的位姿（cam→imu）**，与字段名相反：按字段名解读时基线方向与图像横轴垂直（物理矛盾），按 cam→imu 解读完全自洽。`ImuCamRig` 对两种解读打分自动选择，IMU 轴向约定也从标定自身构造（不臆测）。见 `Tests/ImuCamRigTests.cs`。

## 用法

1. 菜单 `PicoTest/VST Passthrough/Build Demo Scene` → 生成场景
2. 菜单 `PicoTest/VST Passthrough/Build APK (in-editor)` → `Builds/PicoTest-VstPassthrough.apk`（Release，规避 CheckJNI 崩溃）
3. 装机：`Tools/install-latest-apk.ps1 -Launch`
4. 真机调参（免重打包）：
   ```
   adb shell "echo radius 1.2 > /sdcard/Android/data/com.wuyazishidi.picotest/files/passthrough/cmd.txt"
   ```
   命令：`radius <m>` `latency <ms>` `mode head|capture` `ext calib|id` `dome on|off` `cover <deg>` `feather <deg>` `hud on|off` `dump`
5. 手柄：**A** = 隐藏/显示穹顶（与原生透视对比），**B** = 安全退出

## 真机验收（设计 §验收标准）

- `capture` 模式转头画面世界稳定（对比 `mode head` 的拖影）
- A 键切原生透视：近景位置偏差 ≤ 数 cm、无明显缩放
- `ext id` ⇄ `ext calib` 对比可见对齐改善
