# Exp-RobotStreamLeftPreview — HTTP Offer WebRTC 左目预览（机器人画面预演）

设计：`Docs/designs/2026-07-16-robot-stream-left-preview-demo.md`。**另起炉灶**：不改 Exp-WebRTC /
Exp-RobotStream / Exp-VstPassthrough / Main，只引用复用。

## 目标

直连 `Tools/run_stereo_left_viewer.py`（机器人侧 aiortc WebRTC 服务，只推 `ego_stereo` 双目相机的
**左目单目画面**，信令走一次性 HTTP offer/answer、无中继、无 trickle），用 VstPassthroughDemo 同款
穹顶显示方案（capture 位姿补偿 + cmd 调参 + A/B 对比 + HUD + 安全退出）呈现，目标是达到与
Exp-VstPassthrough 相同的显示质量——左右眼贴同一张单目纹理（无立体视差，源本身就是单目）。

## 与 Exp-RobotStream 的区别

Exp-RobotStream 信令走 Node `signaling.js` 中继 + 浏览器发送端，画面是 SBS 双目。本 demo 的对端是
`run_stereo_left_viewer.py`（真实机器人左目单目流，单次 HTTP `POST /offer` 握手，无中继服务器、无
浏览器发送端），因此**新写** `HttpOfferVideoSource`（不复用 `ISignaling`/`WebSocketSignaling`——协议
形状不同：那是双向 WS 消息流，这是单次 HTTP 往返，无 candidate 消息）。

## 复用关系

- 传输基础设施：`Experiment.WebRTC` 的 `IWebRtcVideoSource`/`FakeStereoVideoSource`（只读引用）
- 标定：`Experiment.RobotDsDome` 的 `DsEyeCalibration`（Double Sphere 模型，机器人真实相机——不是
  Pico 参数占位；取其 `RobotDsLeft.asset`，来自真实 `3-camchain.yaml`）
- 穹顶：`Experiment.RobotDsDome` 的 `DsDomeRenderer` + `RobotDsDome.shader`（DS 前向投影，同
  `Exp-RobotDsDome`；不是 Main 的等距鱼眼 `FisheyeDomeRenderer`——机器人相机不是等距鱼眼模型）

## 颜色修正

`run_stereo_left_viewer.py` 的服务端对 RGB ndarray 直接 `cv2.imencode`（未转 BGR），画面天生 R/B
互换（该工具网页端默认用 `feColorMatrix` 补偿）。本 demo 用 `Resources/SwapRB.shader` + 一次
`Graphics.Blit` 复现同一补偿（`swapRB` 字段 / cmd `colorfix on|off`，默认开）。

## 用法

### PC 环回（编辑器接收本机跑的 Python 左目 WebRTC 服务）
1. 机器人侧（或本机联调）：`python Tools/run_stereo_left_viewer.py`（默认监听 `:8888`）
2. 菜单 `PicoTest/Robot Stream Left Preview/Build Demo Scene` → 打开场景，选中
   `RobotStreamLeftPreviewFeeder`，`useRealWebRtc=true`、`serverUrl=http://127.0.0.1:8888`，Play
3. 穹顶显示左目画面（左右眼同一张纹理），颜色应与浏览器版一致

### 编辑器纯冒烟（无 Python 服务）
`useRealWebRtc=false` → 假帧源，验证源→穹顶链路。

### 真机（PICO 与机器人/PC 同一局域网）

服务地址**不能用 127.0.0.1**（那是 PICO 自己）——必须指机器人/PC 的局域网 IP。地址是**运行时可配的**，
装一次包后换 IP 不用重打包：

1. 打包：菜单 `PicoTest/Build APK/Robot Stream Left Preview` → `Builds/PicoTest-RobotStreamLeftPreview.apk`
2. 装机：`PicoTest/Install APK/Robot Stream Left Preview`（签名冲突先 `adb uninstall com.wuyazishidi.picotest`）
3. **配服务 IP（二选一，免重打包）**：
   ```bash
   PKG=com.wuyazishidi.picotest
   # 启动前静态配（下次启动读）：
   adb shell "echo http://172.16.3.95:8888 > /sdcard/Android/data/$PKG/files/robotleftpreview/server.txt"
   # 或运行中热重连（立即换）：
   adb shell "echo server http://172.16.3.95:8888 > /sdcard/Android/data/$PKG/files/robotleftpreview/cmd.txt"
   ```
4. 机器人侧：`python Tools/run_stereo_left_viewer.py`（同一局域网可达）
5. 现场调参（免重打包，写 `files/robotleftpreview/cmd.txt`，一行一条）：
   `server <http://ip:port>` · `radius <m>` · `latency <ms>` · `mode worldlocked|captureproxy` ·
   `dome on|off` · `flip 0|1` · `colorfix on|off` · `cover <deg>` · `feather <deg>` ·
   `hud on|off` · `dump`
6. 手柄：**A**=隐藏穹顶对比原生透视，**B**=安全退出

## 位姿模式

- `worldlocked`（默认）：朝向世界锁、位置跟眼——静止机器人相机最优
- `captureproxy`：用观看者头位姿当机器人头位姿替身 + 固定 latency 回溯，演练 capture 补偿
- `external`：`PushRobotPose(ts,pos,rot)` 被调用即进入——面向真机器人 DataChannel 每帧位姿（本 demo 仅留 API seam）

## 接真机器人时要改的

1. 标定已是机器人真实 Double Sphere 参数（`RobotDsLeft.asset`）——换机器人/换镜头时重新
   `PicoTest/Robot DS Dome/Import Camchain` 生成新资产，`DsDomeRenderer`/`DsEyeCalibration` 不改
2. 视频源已是 `IWebRtcVideoSource`——机器人发端只需实现同一套 aiohttp `/offer` 端点即可
3. 机器人每帧位姿经 DataChannel → 调 `PushRobotPose` → 自动走 external capture 补偿
