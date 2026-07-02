# Exp-WebRTC / Server —— 本地信令 + PC 环回测试

## 1) 起信令服务器（零依赖，无需 npm install）
```
cd Assets/Experiments/Exp-WebRTC/Server
node signaling.js            # ws://0.0.0.0:8765（PORT 可覆盖）
```
纯 Node（内置 http/crypto），把一方 JSON 信令广播给其他连接方。

## 2) PC 环回（浏览器发送端 → Unity 接收端）
1. **Unity**：打开 `Scenes/WebRtcDomeXRLive.unity`，选中 `WebRtcDomeFeeder`，勾选
   **Use Real Web Rtc**、`Signaling Url = ws://127.0.0.1:8765`，按 **Play**（Unity 作接收端，等待 offer）。
2. **浏览器**：打开 `webrtc-sender.html`（Chrome/Edge），
   点「1) 连接信令」→ Unity Play 后点「2) 开始呼叫」。浏览器把 canvas 的 SBS 画面
   （左红/右蓝 + 移动竖条）经 WebRTC 发给 Unity；穹顶应显示该画面、左右分眼、云台正常。

> 顺序：先 Unity Play（连上信令、就绪），再浏览器「开始呼叫」（发 offer），否则 offer 会广播给空房间。

## 信令协议
自定义 JSON（与 C# `SignalingMessage` 一致）：
`{"type":"offer|answer|candidate|bye","sdp":..,"candidate":..,"sdpMid":..,"sdpMLineIndex":..}`。
Unity 侧 `WebSocketSignaling` + `UnityWebRtcVideoSource`（recvonly，收 offer→answer）。

> `node_modules/` 无（零依赖）。真实部署可换 Ayame/Sora。
