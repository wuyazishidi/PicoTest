# Exp-RobotDsDome — 机器人双目（Double Sphere）→ 穹顶

设计：`Docs/designs/2026-07-10-robot-ds-dome-demo.md`。**另起炉灶**：只引用 Main（穹顶网格/退出），不改其他 demo。

## 目标

拿到机器人真实双目相机参数（`StreamingAssets/3-camchain.yaml`，**Double Sphere 模型**）+ 实拍 SBS 视频（`StreamingAssets/episode_000000.mp4`）后，参考 VstPassthroughDemo 把机器人画面投到穹顶测试去畸变/立体/沉浸。

## 与 VstPassthrough 的根本差异：相机模型

VstPassthrough 用等距鱼眼（equiDis62）；机器人相机是 **Double Sphere（ds）**，投影数学完全不同 → 本 demo 新写一套 DS shader（`RobotDsDome.shader`，照抄 `Tools/undistort_ds.py` 的 `ds_project`），不复用 FisheyeDome。内参 `[xi, alpha, fx, fy, cx, cy]`，无单独畸变系数，1920×1080/眼，基线≈55.7mm。

## 关键设计

- **DS 投影可回归**：`DoubleSphereProjection`（纯 C#）对 Python golden 值 <0.01px；shader 逐行照抄。
- **标定 yaml → SO**：`DsCamchainImporter`（菜单 Import Camchain）解析 `3-camchain.yaml` → `Calibration/RobotDsLeft/Right.asset`。不缩放内参（归一化 UV 天然对齐 960×540 降采样视频）。
- **视频**：`VideoPlayer` 播 `episode_000000.mp4`（h264，编辑器可解码肉眼验证）→ RenderTexture → 穹顶，SBS 左半→左眼(cam0)、右半→右眼(cam1)。
- **显示复用 VstPassthrough 思路**：capture/worldlocked 位姿、cmd 调参、A 键对比原生透视、B 退出、HUD。

## 用法

### 编辑器（肉眼验证去畸变）
1. 菜单 `PicoTest/Robot DS Dome/Import Camchain`（生成 DS 标定资产）
2. 菜单 `PicoTest/Robot DS Dome/Build Demo Scene`（生成场景 + 确保 shader Always-Included）
3. Play → 穹顶显示 episode 视频、左右分眼、DS 去畸变。flip/mirror 若朝向不对经 cmd 或 Inspector 调。

### 真机
1. 菜单 `PicoTest/Build APK/Robot DS Dome` → `Builds/PicoTest-RobotDsDome.apk`
2. `PicoTest/Install APK/Robot DS Dome`（签名冲突先 `adb uninstall com.wuyazishidi.picotest`）
3. 调参（免重打包，写 `files/robotds/cmd.txt`）：
   `radius <m>` · `latency <ms>` · `mode worldlocked|captureproxy` · `flip 0|1` · `mirror 0|1` · `cover <deg>` · `feather <deg>` · `dome on|off` · `hud on|off` · `dump`
4. 手柄：**A**=隐藏穹顶对比原生透视，**B**=安全退出

## 接更多机器人数据

换 `3-camchain.yaml` + `episode_*.mp4` → 重跑 Import Camchain → 场景标定引用更新即可，shader/feeder 不改（只要还是 ds 模型）。

## 与 Exp-RobotStream 的关系

RobotStream 测 WebRTC **传输**；本 demo 测 DS **光学**。将来真机器人 = DS 光学 + WebRTC 传输合流，两实验各验一半。
