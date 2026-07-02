// 最简本地信令服务器：把任一客户端发来的 JSON 原样广播给其他客户端（offer/answer/candidate/bye）。
// 仅本地测试用。运行：`npm install ws && node signaling.js`（默认端口 8765）。
// C# 端连 ws://<PC-IP>:8765（WebSocketSignaling.Connect）。
const http = require('http');
const { WebSocketServer } = require('ws');

const PORT = process.env.PORT || 8765;
const server = http.createServer();
const wss = new WebSocketServer({ server });

wss.on('connection', (ws) => {
  console.log(`[signaling] client connected (total=${wss.clients.size})`);
  ws.on('message', (data, isBinary) => {
    const text = isBinary ? data : data.toString();
    for (const c of wss.clients) {
      if (c !== ws && c.readyState === 1) c.send(text);
    }
  });
  ws.on('close', () => console.log(`[signaling] client left (total=${wss.clients.size})`));
  ws.on('error', (e) => console.warn('[signaling] ws error', e.message));
});

server.listen(PORT, () => console.log(`[signaling] relay on ws://0.0.0.0:${PORT}`));
