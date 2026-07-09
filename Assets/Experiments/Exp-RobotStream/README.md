# Exp-RobotStream — WebRTC 传输 + VstPassthrough 显示（机器人画面预演）

设计：`Docs/designs/2026-07-09-robot-stream-webrtc-demo.md`。**另起炉灶**：不改 Exp-WebRTC / Exp-VstPassthrough / Main，只引用复用。

## 目标

用 VstPassthroughDemo 的显示方案（ImuCamRig 外参 + capture 位姿补偿 + cmd 调参 + A/B 对比原生透视）跑 WebRTC 传输测试，结构上让**后续接机器人画面直接复用**（真机器人 = 换数据源 + 换标定 + 接位姿流，feeder 不改）。相机参数暂用 Pico 的 `cam_calib.json` 分离出的正常标定参数。

## 复用关系

- 传输：`Experiment.WebRTC` 的 `IWebRtcVideoSource`/`UnityWebRtcVideoSource`/`FakeStereoVideoSource`/`WebSocketSignaling`（只读引用）
- 外参：`Experiment.VstPassthrough` 的 `ImuCamRig`（`RobotCalib` 内部调用）
- 穹顶：Main 的 `FisheyeDomeRenderer` + `FisheyeDome.shader`

## 与 Exp-WebRTC 的区别

Exp-WebRTC 是 WebRTC 打通期（WorldLocked + 云台伺服 + 单位阵外参，老方案）。本 demo 把 VstPassthrough 的整套改进搬到 WebRTC 路径并面向真机器人组织。Exp-WebRTC 作为传输层被复用、不改。

## 用法

### PC 环回（编辑器接收浏览器发的 Pico 实拍流）
1. `cd Assets/Experiments/Exp-WebRTC/Server && node signaling.js`（信令 ws://…:8765）
2. 菜单 `PicoTest/Robot Stream/Build Demo Scene` → 打开场景，选中 `RobotStreamFeeder`，`useRealWebRtc=true`、`signalingUrl=ws://127.0.0.1:8765`，Play
3. 浏览器开 `Exp-WebRTC/Server/webrtc-sender.html` → 连接信令 → 开始呼叫（发 SBS 画面）
4. 穹顶显示该画面、左右分眼、去畸变

### 编辑器纯冒烟（无信令/浏览器）
`useRealWebRtc=false` → 假帧源（左红/右蓝渐变），验证源→穹顶链路。

### 真机
1. 菜单 `PicoTest/Build APK/Robot Stream` → `Builds/PicoTest-RobotStream.apk`
2. `PicoTest/Install APK/Robot Stream` 装机（签名冲突先 `adb uninstall com.wuyazishidi.picotest`）
3. `signalingUrl` 改成 PC 局域网 IP；PC 起信令 + 发送端
4. 调参（免重打包）：`adb shell "echo mode captureproxy > /sdcard/Android/data/com.wuyazishidi.picotest/files/robotstream/cmd.txt"`
   命令：`radius <m>` `latency <ms>` `mode worldlocked|captureproxy` `ext calib|id` `dome on|off` `flip 0|1` `cover <deg>` `feather <deg>` `hud on|off` `dump`
5. 手柄：**A**=隐藏穹顶对比原生透视，**B**=安全退出

## 位姿模式

- `worldlocked`（默认）：朝向世界锁、位置跟眼——静止机器人相机最优，转头零延迟环顾已收画面，不需位姿流
- `captureproxy`：用观看者头位姿当机器人头位姿替身 + 固定 latency 回溯，演练 capture 补偿
- `external`：`PushRobotPose(ts,pos,rot)` 被调用即进入——面向真机器人 DataChannel 每帧位姿（本 demo 仅留 API seam，DataChannel 收发未实现）

## 接真机器人时要改的

1. 换 `cam_calib.json`（机器人相机内参/畸变/基线/外参）——`RobotCalib` 不改
2. 视频源已是 `IWebRtcVideoSource`——机器人发端对上信令即可
3. 机器人每帧位姿经 DataChannel → 调 `PushRobotPose` → 自动走 external capture 补偿
