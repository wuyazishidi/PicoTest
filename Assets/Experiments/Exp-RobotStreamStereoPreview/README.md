# Exp-RobotStreamStereoPreview — HTTP Offer WebRTC 双目预览（机器人画面预演）

设计：`Docs/designs/2026-07-16-robot-stream-stereo-preview-demo.md`。**另起炉灶**：不改 Exp-WebRTC /
Exp-RobotStream / Exp-RobotStreamLeftPreview / Exp-VstPassthrough / Main，只引用复用。

## 目标

直连 `Tools/run_stereo_viewer.py`（`run_stereo_left_viewer.py` 的姊妹脚本，转发 `ego_stereo`
双目相机的**完整 SBS 画面**、不裁剪，端口 8889），用 VstPassthroughDemo 同款穹顶显示方案呈现，
左右眼**真正分半显示**（同 `Exp-RobotStream` 的 SBS 用法，不像 `Exp-RobotStreamLeftPreview` 那样
左右眼贴同一张单目纹理）。

## 与另外两个 demo 的关系

- `Exp-RobotStream`：同样是 SBS 双目 + 穹顶，但信令走 Node `signaling.js` 中继 + 浏览器发送端。
- `Exp-RobotStreamLeftPreview`：同样的 HTTP offer/answer 传输（`run_stereo_left_viewer.py`，端口
  8888），但那边画面是单目（源本身裁了右目）。

本 demo = `Exp-RobotStreamLeftPreview` 的传输方式（HTTP offer/answer） + `Exp-RobotStream` 的
双目分眼显示方式。

## 复用关系（最大化复用，未复制代码）

- 传输：**直接引用** `Experiment.RobotStreamLeftPreview` 的 `HttpOfferVideoSource`（同一套 HTTP
  offer/answer 握手，双目/单目对它没有区别，只是喂的画面宽了一倍，无需改动或复制）
- 颜色修正：**直接复用** `Exp-RobotStreamLeftPreview/Resources/SwapRB.shader`（`Resources.Load`
  按虚拟路径合并全项目 `Resources/` 目录，跨实验目录也能找到，无需复制文件）
- 标定：`Experiment.RobotDsDome` 的 `DsEyeCalibration`（Double Sphere 模型，机器人真实相机——不是
  Pico 参数占位；`RobotDsLeft.asset`/`RobotDsRight.asset`，来自真实 `3-camchain.yaml`，左右眼各用各的）
- 穹顶：`Experiment.RobotDsDome` 的 `DsDomeRenderer` + `RobotDsDome.shader`（DS 前向投影，同
  `Exp-RobotDsDome`；不是 Main 的等距鱼眼 `FisheyeDomeRenderer`）

## 用法

### PC 环回（编辑器接收本机跑的 Python 双目 WebRTC 服务）
1. 机器人侧（或本机联调）：`python Tools/run_stereo_viewer.py`（默认监听 `:8889`）
2. 菜单 `PicoTest/Robot Stream Stereo Preview/Build Demo Scene` → 打开场景，选中
   `RobotStreamStereoPreviewFeeder`，`useRealWebRtc=true`、`serverUrl=http://127.0.0.1:8889`，Play
3. 穹顶显示双目画面，左右眼真正分半，颜色应与浏览器版一致

### 编辑器纯冒烟（无 Python 服务）
`useRealWebRtc=false` → 假帧源（SBS 左红/右蓝），验证源→穹顶链路、左右眼确实是两块不同画面。

### 真机（PICO 与机器人/PC 同一局域网）

服务地址**不能用 127.0.0.1**（那是 PICO 自己）——必须指机器人/PC 的局域网 IP。地址是**运行时可配的**，
装一次包后换 IP 不用重打包：

1. 打包：菜单 `PicoTest/Build APK/Robot Stream Stereo Preview` → `Builds/PicoTest-RobotStreamStereoPreview.apk`
2. 装机：`PicoTest/Install APK/Robot Stream Stereo Preview`（签名冲突先 `adb uninstall com.wuyazishidi.picotest`）
3. **配服务 IP（二选一，免重打包）**：
   ```bash
   PKG=com.wuyazishidi.picotest
   # 启动前静态配（下次启动读）：
   adb shell "echo http://172.16.3.95:8889 > /sdcard/Android/data/$PKG/files/robotstereopreview/server.txt"
   # 或运行中热重连（立即换）：
   adb shell "echo server http://172.16.3.95:8889 > /sdcard/Android/data/$PKG/files/robotstereopreview/cmd.txt"
   ```
4. 机器人侧：`python Tools/run_stereo_viewer.py`（同一局域网可达）
5. 现场调参（免重打包，写 `files/robotstereopreview/cmd.txt`，一行一条）：
   `server <http://ip:port>` · `radius <m>` · `latency <ms>` · `mode worldlocked|captureproxy` ·
   `dome on|off` · `flip 0|1` · `colorfix on|off` · `cover <deg>` · `feather <deg>` ·
   `hud on|off` · `dump`
6. 手柄：**A**=隐藏穹顶对比原生透视，**B**=安全退出

## 位姿模式

- `worldlocked`（默认）：朝向世界锁、位置跟眼——静止机器人相机最优
- `captureproxy`：用观看者头位姿当机器人头位姿替身 + 固定 latency 回溯，演练 capture 补偿
- `external`：`PushRobotPose(ts,pos,rot)` 被调用即进入——面向真机器人 DataChannel 每帧位姿（本 demo 仅留 API seam）

## 接真机器人时要改的

1. 标定已是机器人真实 Double Sphere 参数（`RobotDsLeft/Right.asset`）——换机器人/换镜头时重新
   `PicoTest/Robot DS Dome/Import Camchain` 生成新资产，`DsDomeRenderer`/`DsEyeCalibration` 不改
2. 视频源已是 `IWebRtcVideoSource`——机器人发端只需实现同一套 aiohttp `/offer` 端点即可
3. 机器人每帧位姿经 DataChannel → 调 `PushRobotPose` → 自动走 external capture 补偿
