# 2026-07-09 Robot Stream WebRTC Demo（Exp-RobotStream）

分支：`fisheye-stereo-dome`。设计 `Docs/designs/2026-07-09-robot-stream-webrtc-demo.md`。
需求：新建 demo，用 VstPassthroughDemo 实施的显示方案跑 WebRTC 传输测试，后续接机器人画面可直接复用；机器人相机参数暂用 Pico 的 cam_calib.json 分离出正常标定参数。**不影响其他 demo**。

## 做了什么

新实验 `Assets/Experiments/Exp-RobotStream/`，= Exp-WebRTC 传输层 + VstPassthrough 显示方案的结合，全部引用复用、不改被引用方：

| 产出 | 说明 |
|---|---|
| `RobotCalib`（静态助手，可单测） | cam_calib.json → `ImuCamRig` 外参 → 左右 `FisheyeCalibration`（内参 K + k1..k6 + 外参四元数）。"用 Pico 参数当机器人相机分离出正常标定"的落点 |
| `RobotStreamFeeder` | WebRTC 源（`IWebRtcVideoSource`）→ 穹顶；capture/worldlocked/external 三位姿模式；`PushRobotPose` seam（面向真机器人 DataChannel）；cmd.txt 调参；A 键对比原生透视；B 退出；HUD |
| `RobotStreamSceneBuilder` | 菜单 `PicoTest/Robot Stream/Build Demo Scene` → 生成 `RobotStreamDemo.unity`（已生成入库） |
| Builder 注册 `robotstream` | 中央注册表加一条 → `Build APK/Robot Stream` + `Install APK/Robot Stream` 菜单自动出现；产物 `PicoTest-RobotStream.apk` |
| 测试 | EditMode +4（`RobotCalibTests`：真实 cam_calib 内参/畸变/外参施加与单位阵切换）、PlayMode +1（`RobotStreamFeederSmokeTests`：假源→穹顶非黑帧） |

## 为何这么做（关键决策）

- **另起新实验而非改 Exp-WebRTC**：Exp-WebRTC 是 WebRTC 打通期显示（WorldLocked + 云台伺服 + 单位阵外参，VstPassthrough 之前的老方案）。本 demo 把 VstPassthrough 整套"逼近原生透视"的改进（ImuCamRig 外参、capture 位姿补偿、cmd 调参、A/B 对比）搬到 WebRTC 路径，并面向真机器人接入组织。Exp-WebRTC 作传输层被复用、一行不改。
- **引用复用而非重复造**：asmdef 引用 `Experiment.WebRTC`（传输）+ `Experiment.VstPassthrough`（ImuCamRig）+ Main（穹顶）。传递依赖会把 Main.Vst/PICO.TobSupport 拖进本 demo（VstPassthrough 引用了它们）——实验期接受；**晋升提示**：ImuCamRig 是纯数学（仅依赖 Main.Core），晋升时应移入 Main.Core 以断开此传递依赖。
- **标定用 Pico 参数**：测试素材 Server/camera.mp4 是 Pico 4U 实拍 SBS 2560×960，与 Pico cam_calib（1280×960/眼）分辨率一致 → 用 Pico 真实 K/D/基线 + ImuCamRig 外参即可端到端验证，真机器人到位换 cam_calib.json 即可，RobotCalib/feeder 不改。
- **flipV=0**：WebRTC 普通视频纹理正立（VstPassthrough 的 flipV=1 是 Pico raw top-down 缓冲专有）。
- **位姿 seam `PushRobotPose`**：真机器人每帧位姿经 DataChannel → 调它 → 自动 external capture 补偿（notes 方案 1）。DataChannel 收发本 demo 未实现，仅留 API；测试期走 worldlocked（静止机器人最优）/captureproxy（用头位姿演练补偿）。

## 测试结果

- 编译 Success 0 错误；**EditMode 102（+4）/ PlayMode 9（+1）全过**，1 skip 为既有 HEVC 探针。`.gates/tests-green` 已写。
- `RobotStreamDemo.unity` 已生成，`BuildSceneRegistryTests` 守护通过（注册表 6 场景全部存在）。

## 遗留 / 下一步

- PC 环回人审：`node signaling.js` + `webrtc-sender.html` 发 camera.mp4 → 穹顶显示实拍鱼眼、左右分眼、去畸变（编辑器 useRealWebRtc=true）。
- 真机：`Build APK/Robot Stream` → 装机 → 压测 **com.unity.webrtc 在 PICO 的硬解/兼容性**（decisions.md 遗留风险，本 demo 主要测试目标）。
- **DataChannel 每帧位姿收发**（真机器人 capture 补偿的前提）未实现——下一步；协议见 notes 方案 1（`{frame_id, capture_ts, head_pose}`）。
- 分辨率：真机器人相机（设计记 1280×720）内参不同，接入时换 cam_calib 即可（UV 归一化，宽高由标定驱动）。
