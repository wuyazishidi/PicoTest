# 计划：WebRTC 机器人双目鱼眼 → 穹顶接收显示场景

## Context（背景 / 为什么）
现有 `FisheyeDomeXRLive` 用**本机 PICO VST 相机**（`VstCameraDomeFeeder`）把 SBS 鱼眼投到穹顶。下一步需要接收**远端机器人双目鱼眼相机**画面，经 WebRTC 实时传输，在 PICO 上以相同的鱼眼穹顶 + 云台方案显示（遥操作场景）。

技术栈（用户指定）：`shiguredo/webrtc-build` 预编译 libwebrtc + 自写 C API Wrapper（`extern "C"`）+ Unity P/Invoke；本地搭信令测试服务器；云台/显示复用 `FisheyeDomeXRLive`。

用户已定的关键点：
- 视频格式：**双目鱼眼 SBS 2560×720**（每眼 1280×720）。
- 信令：暂定**自定义 JSON/WebSocket**（也可能 HTTP）→ 做成可换抽象。
- YUV→RGBA：在**原生 C wrapper**里用 libyuv 转。
- 落位：**先进 `Assets/Experiments/Exp-WebRTC/`**，验证后 `/promote-experiment` 晋升。

探索确认（三处代理）：
- **显示 + 云台完全源无关、可复用**：`FisheyeDomeRenderer` / `FisheyeDome.shader` / `InvertedSphereMesh` / `RobotHeadPoseDriver` / `GazeServo` / `FisheyeCalibration` / `FisheyeProjection` / `CamCalib`。WebRTC feeder 只需按双缓冲产出**一张 RGBA32 SBS 纹理**即可。dome 与分辨率无关（`_ImgSize` = 每眼尺寸，UV rect 分半）。
- 这是项目**首个原生插件 / P-Invoke**（此前仅 YIUIMCP 包内有一处，非项目代码）。
- 原生 .so 放 `Assets/Plugins/Android/libs/arm64-v8a/`，IL2CPP/ARM64 自动并入 APK；C 包装须 `extern "C"`。
- 现有 `IHttpTransport` 仅 HTTP、无 WebSocket → 信令需新抽象。
- 依赖/权限变更须记 `Docs/decisions.md`；宪法 dev-loop：design→experiment→tests-green→REPORT→promote→journal。

## 目标 & 验收
- PICO 上运行新场景，经本地信令连上测试发送端（或环回），收到机器人双目鱼眼流 → 解码 → RGBA → 投到鱼眼穹顶，云台/透视/退出行为与 `FisheyeDomeXRLive` 一致。
- 编辑器/PC 端用"假帧源"或本地环回即可跑通渲染冒烟（不依赖真机相机）。
- 全程符合宪法门禁（`.gates/tests-green` 才能 commit）。

## 方案（推荐）

### 分层
1. **原生 C wrapper（`extern "C"`）** — `Exp-WebRTC/Plugins/`
   - 基于 shiguredo/webrtc-build，封装最小 C API：`wrtc_create/close`、SDP/candidate 收发钩子、注册视频帧回调。
   - 帧回调内用 **libyuv** 把 I420→RGBA（SBS 2560×720），把 RGBA 指针+尺寸+时间戳交给 C#（原生线程）。
   - 产物：`libwebrtc.so` + `libpicowebrtc.so`（wrapper），arm64-v8a；PC 联调可另出 windows x86_64 版（首版可只做其一）。

2. **P/Invoke 绑定（纯 C#）** — `Exp-WebRTC/Scripts/Native/WebRtcInterop.cs`
   - `[DllImport("picowebrtc")]`；帧回调用 `MonoPInvokeCallback`（AOT/IL2CPP 安全）；回调在原生线程**只做纯 C#**（Marshal.Copy），遵守"原生线程禁 JNI/Unity API"（照 `VstCamera` 纪律）。

3. **信令客户端（可换抽象）** — `Exp-WebRTC/Scripts/Signaling/`
   - `ISignaling`（offer/answer/candidate 收发）；首版 `WebSocketSignaling`（自定义 JSON），预留 `HttpSignaling`。
   - 本地测试服务器（最简 Node/Python WebSocket 中转）+ 环回发送端放 `Exp-WebRTC/Server/`（仅测试，不打包）。

4. **Feeder（镜像 `VstCameraDomeFeeder`）** — `Exp-WebRTC/Scripts/WebRtcDomeFeeder.cs`
   - `Texture2D(2560,720,RGBA32)`；帧回调（原生线程）Marshal.Copy→`_back`→双缓冲 swap；`Update()` 主线程 `LoadRawTextureData+Apply`。
   - 复用 `FisheyeDomeRenderer`（`leftUVRect=(0,0,.5,1)` / `rightUVRect=(.5,0,.5,1)`，`flipV` 视源朝向）、`RobotHeadPoseDriver`（死区/回停/速率沿用当前值）、see-through 反射开启、B 键 `killProcess` 退出、URP 关 HDR（已生效）—— **只把"源"从 VST 换成 WebRTC**。
   - 标定：机器人相机每眼 **1280×720** 鱼眼标定（占位 `RobotLeft/RobotRight` 资产，或运行时用 `CamCalib` 解析机器人下发的 JSON）。

5. **场景 + 构建入口（编辑器菜单，代码生成）** — `Exp-WebRTC/Editor/WebRtcDomeSceneBuilder.cs`
   - 仿 `FisheyeXRLiveSceneBuilder`：XR Origin(VR) + `WebRtcDomeFeeder` + 标定 + 信令配置；菜单 `PicoTest/Build WebRTC Dome Scene` → 生成 `Exp-WebRTC/Scenes/WebRtcDomeXRLive.unity`（场景极简，内容代码实例化）。

### 新增文件（关键）
- `Assets/Experiments/Exp-WebRTC/Experiment.WebRTC.asmdef`（references: `Main`, `Main.Core`；`allowUnsafeCode` 视 P/Invoke 需要）
- `.../Scripts/Native/WebRtcInterop.cs`、`.../Scripts/Signaling/ISignaling.cs` + `WebSocketSignaling.cs`
- `.../Scripts/WebRtcDomeFeeder.cs`、`.../Editor/WebRtcDomeSceneBuilder.cs`
- `.../Plugins/Android/arm64-v8a/{libwebrtc.so, libpicowebrtc.so}`（预编译；wrapper 源另放，构建产物入此）
- `.../Server/`（本地信令服务器 + 环回发送端样例）
- `.../Tests/EditMode/`（信令 JSON 编解码、interop marshaling mock）+ `.../Tests/PlayMode/`（假帧→穹顶冒烟）
- `.../REPORT.md`
- `Assets/Plugins/Android/AndroidManifest.xml`：加 `<uses-permission android:name="android.permission.INTERNET"/>`
- `Docs/designs/2026-07-02-webrtc-stereo-dome.md`（设计，人审）
- `Docs/decisions.md`：记 WebRTC 选型（shiguredo/webrtc-build + C wrapper + 首个原生插件）+ INTERNET 权限

### 复用（不改）
`FisheyeDomeRenderer` / `FisheyeDome.shader`（已在 Always Included Shaders）/ `InvertedSphereMesh` / `RobotHeadPoseDriver` / `GazeServo` / `FisheyeCalibration` / `CamCalib` / `FisheyeProjection`；双缓冲+`LoadRawTextureData` 模式（照抄 `VstCameraDomeFeeder`）。

### 线程纪律（照 `VstCamera`）
WebRTC 帧回调在原生线程：仅 Marshal.Copy + 双缓冲 swap，禁 `Debug.Log`/Unity API；主线程 `Update` pump 上传纹理。

## 分阶段实施（建议顺序）
- **M0 骨架（PC/编辑器，无真机）**：Exp-WebRTC asmdef + `WebRtcDomeFeeder` 用"假 RGBA 帧源"（读一张 2560×720 SBS 测试图 / 生成渐变）驱动穹顶 → PlayMode 冒烟。**先证明 dome+云台复用无误**。
- **M1 原生 wrapper + P/Invoke（PC）**：webrtc-build(windows) + C wrapper + libyuv；本地环回（自发自收）在 PC 通；信令走本地 WebSocket 服务器。
- **M2 信令端到端（PC）**：offer/answer/candidate 打通，真实 WebRTC 帧渲染到穹顶。
- **M3 Android/PICO**：arm64 .so 入 APK，真机联调（硬解、权限）；`Tools\install-latest-apk.ps1` 装、adb logcat 看 `[WebRtc]` 首帧。
- **M4 标定接入**：机器人每眼 1280×720 鱼眼标定填入（占位→真值）。
- **收尾**：tests 全绿 → `REPORT.md` → `/promote-experiment` → journal。

## 验证
- **EditMode**：信令 JSON offer/answer/candidate 编解码单测；interop marshaling（mock 回调）；`GazeServo` 已覆盖。
- **PlayMode**：假帧源 → 穹顶渲染冒烟（仿 `RealFisheyeFrameOnDomeTests` / `FisheyeDomeRenderSmokeTests`，断言中心像素非黑、SBS 左右分眼正确）。
- **PC 端到端**：本地信令服务器 + 环回发送端 → feeder 收帧、穹顶显示。
- **真机**：装 APK，logcat 看 `[WebRtc]` 连接/首帧；肉眼看穹顶 + 云台 + 透视。
- 门禁：`Tools\run-tests.ps1` 全绿写 `.gates/tests-green`（commit 前置）。

## 开放项 / 外部依赖（需确认）
- 机器人相机**鱼眼标定**（每眼 1280×720 的 fx,fy,cx,cy,k1–k6,外参）——无则先占位，畸变还原不准。
- 信令最终形态（自定义 WS / HTTP）——首版自定义 WS，抽象可换。
- 编解码（H.264/H.265）与 PICO 硬解可用性。
- 云台目标角是否由机器人云台遥测下发（现用本地头偏航；遥操作可接遥测到 `RobotHeadPoseDriver.targetYawDeg`）。
- shiguredo/webrtc-build 版本、授权、体积（libwebrtc.so 较大，影响 APK）。
- SBS 左右眼顺序 + 图像行序（`flipV`/`_Mirror` 需按真实源标定）。
