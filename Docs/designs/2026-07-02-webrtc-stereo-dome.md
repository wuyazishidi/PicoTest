# 设计：WebRTC 机器人双目鱼眼 → 鱼眼穹顶接收显示

> 可执行分解见 `.claude/plans/2026-07-02-webrtc-stereo-dome.md`（含探索结论、文件清单、里程碑）。本文为宪法要求的设计文档（目标/原则/验收）。

## 目标
接收远端机器人双目鱼眼相机（SBS 2560×720，每眼 1280×720）经 WebRTC 的实时流，在 PICO 上以鱼眼穹顶 + 云台方案显示（遥操作）。**显示/云台/透视/退出全部复用 FisheyeDomeXRLive**，只替换数据源为 WebRTC。

## 原则
- **源无关复用**：`FisheyeDomeRenderer` / `FisheyeDome.shader` / `RobotHeadPoseDriver` / `GazeServo` / `FisheyeCalibration` 不改；feeder 只需按双缓冲产出一张 RGBA32 SBS 纹理（照 `VstCameraDomeFeeder`）。
- **原生线程纪律**：WebRTC 解码帧在原生线程回调 → 仅 Marshal.Copy + 双缓冲 swap，禁 JNI/Unity 调用；主线程 pump 上传纹理。
- **技术栈（指定）**：shiguredo/webrtc-build 预编译 libwebrtc + 自写 C wrapper（`extern "C"`，libyuv 做 I420→RGBA）+ Unity P/Invoke；信令首版自定义 JSON/WebSocket（可换 HTTP/Ayame）。**首个原生插件**。
- **宪法**：先进 `Assets/Experiments/Exp-WebRTC/`，tests-green + REPORT + 人审 → 晋升。依赖/权限变更记 decisions.md。

## 验收
- **M0（已完成，本次）**：纯 C# 骨架 + 假帧源 → 穹顶；EditMode（假帧源尺寸/幂等 + 信令 JSON 编解码）+ PlayMode（源→双缓冲→纹理冒烟）全绿。
- **M1–M2**：C wrapper + interop + 信令端到端（PC 环回）收真实帧到穹顶。
- **M3**：arm64 .so 入 APK，PICO 真机联调（硬解、INTERNET 权限）。
- **M4**：机器人相机每眼 1280×720 鱼眼标定接入。

## 关键决策 / 遗留
- YUV→RGBA 在原生 wrapper（libyuv）；信令可换抽象 `ISignaling`；标定为外部依赖（机器人相机内参未知，先占位）。
- 原生 WebRTC 路径需 `WEBRTC_NATIVE` 宏 + 预编译 .so 才编入调用；缺库时默认走假帧源（M0 不受影响）。
