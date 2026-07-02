# Exp-WebRTC — 机器人双目鱼眼 WebRTC 接收 → 鱼眼穹顶显示

见计划 `.claude/plans/2026-07-02-webrtc-stereo-dome.md`。

复用 `FisheyeDomeXRLive` 的显示 + 云台;只替换"数据源"为 WebRTC 视频。视频格式：双目鱼眼 SBS 2560×720（每眼 1280×720）。

## 里程碑
- **M0（本目录当前内容）**：纯 C# 骨架 —— `IWebRtcVideoSource` 抽象 + `FakeStereoVideoSource`（后台线程生成 SBS 测试图，模拟原生解码线程投帧）+ `WebRtcDomeFeeder`（镜像 `VstCameraDomeFeeder`：双缓冲→纹理→穹顶+云台+透视+B键退出）+ 场景构建菜单 + 测试。**不依赖原生库/网络，可在编辑器直接跑通与测试**。
- **M1–M4**：真实 WebRTC（shiguredo/webrtc-build libwebrtc + C wrapper + libyuv）、信令、Android/PICO、机器人标定。需外部预编译库 + 原生构建工具链，见计划。

## 构建/运行 M0
- 菜单 `PicoTest/Build WebRTC Dome Scene` 生成并打开 `Scenes/WebRtcDomeXRLive.unity`。
- 编辑器 Play：假帧源驱动穹顶（左半红/右半蓝 + 移动渐变，验证 SBS 分眼 + 动态刷新 + 云台）。
- 测试：`Tools\run-tests.ps1`（假帧源单测 + feeder 冒烟）。

## 晋升门禁（宪法）
tests 全绿 + `REPORT.md` + 人审 → `/promote-experiment` 进 `Assets/Main/`。
