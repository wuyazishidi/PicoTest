# 2026-07-16 Robot Stream Left Preview Demo（Exp-RobotStreamLeftPreview）

分支：`fisheye-stereo-dome`。设计 `Docs/designs/2026-07-16-robot-stream-left-preview-demo.md`。
需求：`Tools/run_stereo_left_viewer.py`（机器人侧新增的 aiortc WebRTC 左目单目预览服务，HTTP
offer/answer、无信令中继、无 ICE trickle）需要一个 Unity 端 demo 来对接，参考已有的
Exp-VstPassthrough / Exp-RobotStream / Exp-WebRTC/WebRtcDomeXRLive，目标是达到与 VstPassthrough
同等的穹顶显示质量。

## 关键决策（与用户确认）

- 显示：穹顶（左右眼贴同一张单目纹理，无立体视差——源本身单目），而非简单预览面板，理由是用户
  要求"达到和 Exp-VstPassthrough 效果相同的结果"。
- 代码位置：新建独立实验 `Exp-RobotStreamLeftPreview`（不扩展 Exp-RobotStream），遵循既有"另起炉灶"惯例。

## 做了什么

全部在 `Assets/Experiments/Exp-RobotStreamLeftPreview/`，只引用 Main / Experiment.WebRTC /
Experiment.RobotStream，不改其他 demo：

| 产出 | 说明 |
|---|---|
| `OfferPayload`（纯 C#） | `{sdp,type}` JSON 编解码，与 aiohttp `/offer` 端点约定一致，可单测 |
| `HttpOfferVideoSource`（`IWebRtcVideoSource`） | 新信令实现：recvonly transceiver → CreateOffer → 等 `GatheringState==Complete`（无 trickle）→ `UnityWebRequest POST {url}/offer` → SetRemoteDescription。不复用 `ISignaling`（协议形状不同：单次 HTTP 往返 vs 双向 WS 消息流） |
| `Resources/SwapRB.shader` | R/B 互换 Blit，复现源端色彩 bug 的浏览器端补偿（`cv2.imencode` 未转 BGR） |
| `RobotStreamLeftPreviewFeeder` | 照抄 RobotStreamFeeder 的 capture 位姿补偿/cmd 调参/A 对比/B 退出/HUD；换视频源、UV 整图（非 SBS 分半）、标定取 RobotCalib 左眼 |
| 场景生成器 + Builder 注册 `robotleft` | Build/Install APK 菜单自动出现 |
| EditMode（OfferPayload JSON）+ PlayMode（假源冒烟）测试 | 照抄 Exp-RobotStream 测试惯例 |

## 为何这么做

- **不复用 WebSocketSignaling**：`run_stereo_left_viewer.py` 的握手是"一次性 HTTP offer/answer、
  无 trickle"（逐字复刻其内嵌浏览器 JS：等本端 ICE 收集完成再一次性 POST），与既有 `ISignaling`
  （双向 WS 消息流，offer/answer/candidate 都走消息）协议形状不同，硬凑会两头不像，故新写。
- **颜色修正自包含**：不改 Main 的 FisheyeDomeRenderer/FisheyeDome.shader，新增一个独立的
  Resources 下 Blit shader（避免要在 ProjectSettings/GraphicsSettings.asset 手工登记
  Always-Included——新资产此时还没有 GUID）。
- **标定只取左眼**：画面来源就是机器人左目相机，用其真实内参做去畸变；左右眼贴同一张纹理
  （无立体，源本身单目），比强行伪造双目更诚实。

## 测试结果

编译 Success 0 错误；**EditMode 119（+3：OfferPayload JSON 编解码）/ PlayMode 12（11 passed +1
skip，+1：假源冒烟）全过**，1 skip 为既有 HEVC 探针。`.gates/tests-green` 已写。
`PicoTest/Robot Stream Left Preview/Build Demo Scene` 菜单已跑，场景文件已生成。

踩坑记录：
- 初版 `Experiment.RobotStreamLeftPreview.asmdef` 漏引用 `Unity.WebRTC`——虽然引用了
  `Experiment.WebRTC`（它引用 `Unity.WebRTC`），但 asmdef 引用**不传递**，`HttpOfferVideoSource.cs`
  直接用到 `RTCPeerConnection` 等类型必须显式加 `Unity.WebRTC` 引用，否则 CS0234/CS0246。
- 本机同时开着两个 Unity 项目（`PicoTest` + `PicoHumanCollect/YC-Ego`），YIUIMCP 的端口在两者间
  **不固定**——同一次会话里见过 PicoTest 绑 3232、YC-Ego 绑 3212，重启后又反过来。不能直接假设
  端口，要么看 Editor.log 的"[YIUIMCP] 启动成功，端口:"行，要么用 GetConsoleLog 内容确认是不是
  当前项目（比如看到 `[YCEgo]` 字样就说明连错实例了）。
- `Tools/run-tests.ps1` 假设默认端口 3212（`Packages/cn.etetet.yiuimcp/UTO/.port` 不存在时的
  fallback），这次因为端口临时错位改用直连 RPC（`RunTests`/轮询 `Logs/TestResults/latest.json`）
  手动跑测试并手写 `.gates/tests-green`（格式与脚本一致）。

## 遗留 / 下一步

1. 编辑器假源冒烟已过（PlayMode 测试）；**PC 环回**需实跑 `python Tools/run_stereo_left_viewer.py`
   （及其上游 `composed_camera`/`ego_stereo` 推流服务，本次未涉及）肉眼验证穹顶画面与颜色。
2. **真机**：`Build APK/Robot Stream Left Preview` → 装机 → 局域网连机器人侧服务、A/B 对比、cmd 调参。
3. 颜色修正（SwapRB）目前只在假源/未来真实源下未经肉眼验证，需 PC 环回时确认 `修正颜色` 默认开
   的效果与 python 网页版一致。
