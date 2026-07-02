# Exp-WebRTC / Server —— 本地信令测试服务器

最简 WebSocket 中转：把一方的 JSON 信令（offer/answer/candidate/bye）广播给其他连接方。仅本地测试。

## 运行
```
cd Assets/Experiments/Exp-WebRTC/Server
npm install ws
node signaling.js        # 默认 ws://0.0.0.0:8765（PORT 可覆盖）
```

## 联调
- Unity（`WebSocketSignaling.Connect("ws://<PC-IP>:8765")`）作接收端。
- 机器人/发送端连同一地址，交换 SDP/candidate。
- PC 环回：起两个 peer（发送端 + Unity 接收端）连同一服务器即可自协商。

> 注：`node_modules/` 勿提交（构建产物）。真实部署可换 Ayame/Sora，见计划开放项。
