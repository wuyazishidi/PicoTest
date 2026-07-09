# Robot Stream WebRTC Demo（Exp-RobotStream）

日期：2026-07-09　分支：`fisheye-stereo-dome`　状态：实验（未晋升）
关联：`Docs/designs/2026-07-08-vst-passthrough-demo.md`（显示方案来源）、`Docs/designs/2026-07-08-robot-stream-passthrough-notes.md`（可行性/位姿补偿推演）、`Docs/designs/2026-07-02-webrtc-stereo-dome.md`（WebRTC 传输）

## 目标

新建 demo，用 **VstPassthroughDemo 实施的显示方案** 跑 **WebRTC 传输** 的测试，结构上让**后续接机器人画面可直接复用**（真机器人 = 换数据源 + 换标定 + 接位姿流，管线不改）。相机参数暂用现有 Pico 的 `cam_calib.json` 分离出的正常标定参数（内参 K / 畸变 D / 基线 / 外参经 ImuCamRig 换算）。**不影响其他 demo**：全部代码在 `Assets/Experiments/Exp-RobotStream/`，只引用复用 Exp-WebRTC / Exp-VstPassthrough / Main，不改它们。

## 与现有 Exp-WebRTC 的差别（为何另起而非改）

现 `WebRtcDomeFeeder` 是 WebRTC 打通期（M0）的显示：WorldLocked + 云台伺服 + 单位阵外参，是 VstPassthrough 之前的老方案。本 demo 是把 VstPassthrough 一整套"逼近原生透视"的改进搬到 WebRTC 路径上，且面向真机器人接入组织结构。不改 Exp-WebRTC（它作为传输层被复用）。

## 复用点（不重复造）

| 来源 | 复用 | 方式 |
|---|---|---|
| Exp-WebRTC | `IWebRtcVideoSource`/`UnityWebRtcVideoSource`/`FakeStereoVideoSource`/`VideoFileSource`/`WebSocketSignaling` | asmdef 引用 `Experiment.WebRTC`（只读，不改） |
| Exp-VstPassthrough | `ImuCamRig`（T_imu_to_cam → 头系→相机系外参自标定换算） | asmdef 引用 `Experiment.VstPassthrough`（只读） |
| Main | `FisheyeDomeRenderer` / `FisheyeDome.shader` / `CamCalib` | 已有引用 |

> 晋升提示：`ImuCamRig` 现被两个实验引用，是纯数学（仅依赖 Main.Core），晋升时应移入 `Main.Core`，避免跨实验引用把 Main.Vst/PICO.TobSupport 拖进本 demo。当前实验期接受此传递依赖。

## 方案

### 1. 标定：Pico 参数当机器人相机（`RobotCalib` 静态助手，可单测）

运行时读 `StreamingAssets/cam_calib.json` → `CamCalib.Parse` → `ImuCamRig.FromCalib` → 造左右 `FisheyeCalibration`（内参、k1..k6、外参四元数）。这就是"用现有 Pico 相机参数分离出正常标定参数"：K/D/基线是 Pico 实测（1280×960、baseline 0.064），外参由 ImuCamRig 从 `T_imu_to_cam` 换算（判读实测 cam→imu）。**与 VstPassthrough 的差别**：视频来自 WebRTC 普通视频纹理（正立）→ `flipV=0`（VstPassthrough 用 Pico raw top-down 缓冲才需 flipV=1）。读不到 json 回退 Inspector 的 RealLeft/RealRight + 单位阵外参。

### 2. 传输：WebRTC 源 → 穹顶（`RobotStreamFeeder`）

- 帧源 `IWebRtcVideoSource`：`useRealWebRtc=true` 走 `UnityWebRtcVideoSource`（收远端 SBS 流）；`false` 走 `FakeStereoVideoSource`（编辑器冒烟）。真机器人 = 同一接口换实现，feeder 不改。
- 测试素材：Server 的 `camera.mp4` 是 Pico 4U 实拍 SBS 2560×960（与 Pico 标定分辨率一致）→ 浏览器 `webrtc-sender.html` 发流，穹顶显示。
- 帧到达（`Frame` 纹理变化）→ 绑 `leftTex/rightTex`（SBS UV 分半）→ `PushParameters`。

### 3. 显示/位姿（照搬 VstPassthrough）

- **位姿模式**（cmd 切）：
  - `worldlocked`（默认）：穹顶朝向世界锁、位置跟眼——**静止机器人相机的最优解**，转头零延迟环顾已收画面，不需任何位姿流。
  - `captureproxy`：用观看者头位姿当"机器人头位姿"替身 + 固定 `latencyMs` 回溯（环形缓冲），**演练 VstPassthrough 的 capture 位姿补偿**，无真机器人也能验证补偿链路。
- **位姿 seam（面向真机器人）**：公开 `PushRobotPose(tsSec, pos, rot)`——将来 WebRTC DataChannel 收到机器人每帧位姿即调它，自动切外部 capture 模式，穹顶锚到"该帧拍摄时机器人头朝向"（见 notes 的方案 1）。测试期无人调用即走 worldlocked/proxy。
- **调参**（照搬）：`persistentDataPath/robotstream/cmd.txt` 1s 轮询：`radius/latency/mode/ext/dome/flip/cover/feather/hud/src/dump`。
- **A/B 对比**：右手 A 键（或 cmd `dome off`）隐藏穹顶露原生透视对比；**B 键**安全退出（源 Stop → killProcess 路径）。HUD 跟头显示模式/参数/源状态。

### 4. 场景与构建

- `Editor/RobotStreamSceneBuilder`：菜单 `PicoTest/Robot Stream/Build Demo Scene` → 生成 `Scenes/RobotStreamDemo.unity`（XR Origin + feeder，极简）
- 构建/装机归口中央注册表：`Builder.SceneRegistry` 加 `robotstream` → 菜单 `PicoTest/Build APK/Robot Stream`、`PicoTest/Install APK/Robot Stream` 自动出现（产物 `PicoTest-RobotStream.apk`）
- 无新 shader；不改 Builder 以外任何现有文件（Builder 只加一条注册项 + 一对菜单）

## 验收标准

**PC 级（阻塞 commit）**
1. EditMode：`RobotCalibTests` 用真实 cam_calib.json —— 左右 FisheyeCalibration 内参 fx/fy/cx/cy 与 json 的 K 对应、k1..k6=D[0..5]、宽高=1280×960；`useExtrinsics=true` 时外参四元数非单位且左右不同（ImuCamRig 施加）、`false` 时为单位阵
2. PlayMode：`RobotStreamFeederSmokeTests` 假源 → feeder.Frame 中心非黑（WebRTC 源→穹顶链路打通）
3. 全套件保持全绿

**真机/PC 环回级（设备/信令到位后人审）**
4. PC 环回（`node signaling.js` + `webrtc-sender.html` 发 camera.mp4）→ 穹顶显示实拍鱼眼、左右分眼、去畸变正确
5. `worldlocked` 下转头画面世界稳定；A 键切原生透视对比对齐
6. `ext calib` vs `id`、`captureproxy` vs `worldlocked` 对比可见预期差异

## 风险与已知未知

- **分辨率契合**：Pico 标定 1280×960/眼；真机器人相机（设计记 1280×720）分辨率/内参不同，接入时换 cam_calib 即可（UV 归一化，宽高由标定驱动）。测试用 camera.mp4（960）匹配。
- **WebRTC 在 PICO 硬解/兼容性**未真机验证（decisions.md 遗留）——本 demo 正是要压这个测试。
- **真机器人位姿补偿**需 DataChannel 带每帧位姿（notes 方案 1）；DataChannel 收发本 demo 未实现（仅留 `PushRobotPose` seam），属下一步。
- 旋转-only 近似同 VstPassthrough（相机-眼平移差不补偿）。
