# 2026-07-16 Robot Stream Left Preview Demo（Exp-RobotStreamLeftPreview）

## 背景

`Tools/run_stereo_left_viewer.py`（机器人侧，本次新增）是一个 aiortc WebRTC 服务：只推 `ego_stereo`
双目相机的**左目单目画面**（无立体对），信令走**一次性 HTTP offer/answer**（浏览器/客户端等本端 ICE
收集完成 → `POST /offer` → 收 JSON `{sdp,type}` answer，无 trickle、无中继服务器），定位是
"低延迟连通性验证工具"（README 原话）。

工程里已有三个可参考的 demo：
- `Exp-VstPassthrough`：穹顶 + capture 位姿补偿 + cmd 调参 + A/B 对比 + HUD + 安全退出——视觉/交互效果的目标基准。
- `Exp-RobotStream`：把 VstPassthrough 的整套显示方案搬到 WebRTC 传输路径（`IWebRtcVideoSource` + 穹顶），但信令走
  Node `signaling.js` 中继 + 浏览器发送端，画面是 SBS 双目。
- `Exp-WebRTC/WebRtcDomeXRLive`：WebRTC 传输 + 穹顶显示的最初打通版本（信令/穹顶基础设施来源）。

## 目标

新增独立实验 `Exp-RobotStreamLeftPreview`，直连 `run_stereo_left_viewer.py`，用与 VstPassthrough
同等的穹顶质量呈现左目画面（左右眼贴同一张单目纹理，无立体视差——因为源本身就是单目）。

## 方案

**另起炉灶**：不改 Exp-WebRTC / Exp-RobotStream / Exp-VstPassthrough / Main，只引用复用。

- 传输：新写 `HttpOfferVideoSource`（`IWebRtcVideoSource` 新实现）——逐字复刻
  `run_stereo_left_viewer.py` 内嵌浏览器 JS 客户端的握手：`RTCPeerConnection` 只加
  `recvonly` video transceiver → `CreateOffer`/`SetLocalDescription` → 轮询
  `GatheringState == Complete`（无 trickle）→ `UnityWebRequest POST {baseUrl}/offer`
  （body `{"sdp":..,"type":"offer"}`）→ 解析 answer JSON → `SetRemoteDescription`。
  不复用 `ISignaling`/`WebSocketSignaling`（协议形状不同：那是双向 WS 消息流，这是单次 HTTP 往返）。
- 标定：复用 `Experiment.RobotStream` 的 `RobotCalib.BuildEyeCalibrations`（Pico `cam_calib.json`
  当机器人相机的既有换算，已单测），左右眼标定资产都用其 `left`（因为只有一路真实画面）。
- 颜色：源文档记录 `cv2.imencode` 未做 BGR 转换、R/B 天生互换（浏览器端用 `feColorMatrix` 补偿，
  默认开）。Unity 侧新增一个自包含的 `SwapRB.shader` + Blit 步骤复现同一补偿（cmd 可关）。
- 显示：`FisheyeDomeRenderer`（Main，只引用不改），`leftTex=rightTex=`（同一张、已配色）纹理，
  `leftUVRect=rightUVRect=(0,0,1,1)`（无 SBS 分半，因为源本身单目）。
- 交互：capture/worldlocked 位姿模式、cmd 调参、A 键对比原生透视、B 键安全退出、HUD——照抄
  RobotStreamFeeder（同款流程，只换视频源与 UV）。

## 验收标准

1. 编译 0 错误；EditMode（新 offer/answer JSON 编解码单测）+ PlayMode（假帧源 smoke：feeder 产出非黑帧）全绿。
2. 编辑器假源冒烟（`useRealWebRtc=false`）验证 源→穹顶 链路打通。
3. PC 环回：跑 `python Tools/run_stereo_left_viewer.py`（或其上游 `composed_camera`），编辑器
   `serverUrl=http://127.0.0.1:8888`，Play 后穹顶显示左目画面，颜色与浏览器版一致。
4. 真机：局域网连机器人侧 aiortc 服务，画面在穹顶正常显示，A/B 对比、cmd 调参可用。
