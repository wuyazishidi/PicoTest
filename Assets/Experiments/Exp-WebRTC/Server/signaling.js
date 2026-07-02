// 零依赖信令中转（RFC6455）+ 静态文件服务（供 webrtc-sender.html 与 camera.mp4，带 Range）。
// 运行：node signaling.js   （默认端口 8765）
// 浏览器打开 http://<PC-IP>:8765/ 即加载发送端页面（循环播放 camera.mp4 → WebRTC 发流）。
const http = require('http');
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const PORT = process.env.PORT || 8765;
const GUID = '258EAFA5-E914-47DA-95CA-C5AB0DC85B11';
const MIME = { '.html': 'text/html; charset=utf-8', '.mp4': 'video/mp4', '.js': 'text/javascript', '.md': 'text/plain; charset=utf-8' };
const clients = new Set();

// ---- HTTP 静态服务（GET）----
const server = http.createServer((req, res) => {
  let p = decodeURIComponent((req.url || '/').split('?')[0]);
  if (p === '/') p = '/webrtc-sender.html';
  const safe = path.normalize(p).replace(/^([.][.][/\\])+/, '');
  const file = path.join(__dirname, safe);
  fs.stat(file, (err, st) => {
    if (err || !st.isFile()) { res.writeHead(404); res.end('not found'); return; }
    const type = MIME[path.extname(file).toLowerCase()] || 'application/octet-stream';
    const range = req.headers.range;
    if (range) {
      const m = /bytes=(\d+)-(\d*)/.exec(range);
      const start = parseInt(m[1], 10);
      const end = m[2] ? parseInt(m[2], 10) : st.size - 1;
      res.writeHead(206, {
        'Content-Type': type, 'Accept-Ranges': 'bytes', 'Access-Control-Allow-Origin': '*',
        'Content-Range': `bytes ${start}-${end}/${st.size}`, 'Content-Length': end - start + 1,
      });
      fs.createReadStream(file, { start, end }).pipe(res);
    } else {
      res.writeHead(200, { 'Content-Type': type, 'Content-Length': st.size, 'Accept-Ranges': 'bytes', 'Access-Control-Allow-Origin': '*' });
      fs.createReadStream(file).pipe(res);
    }
  });
});

// ---- WebSocket 信令（广播转发）----
server.on('upgrade', (req, socket) => {
  const key = req.headers['sec-websocket-key'];
  if (!key) { socket.destroy(); return; }
  const accept = crypto.createHash('sha1').update(key + GUID).digest('base64');
  socket.write('HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ' + accept + '\r\n\r\n');
  clients.add(socket);
  console.log('[sig] client+  total=' + clients.size);
  let buf = Buffer.alloc(0);
  socket.on('data', (d) => {
    buf = Buffer.concat([buf, d]);
    let f;
    while ((f = decodeFrame(buf))) {
      buf = buf.slice(f.total);
      if (f.opcode === 0x8) { socket.end(); return; }
      if (f.opcode === 0x1 || f.opcode === 0x2) {
        const text = f.payload.toString('utf8');
        for (const c of clients) if (c !== socket && !c.destroyed) c.write(encodeText(text));
      }
    }
  });
  socket.on('close', () => { clients.delete(socket); console.log('[sig] client-  total=' + clients.size); });
  socket.on('error', () => { clients.delete(socket); });
});

function decodeFrame(buf) {
  if (buf.length < 2) return null;
  const b1 = buf[1]; const opcode = buf[0] & 0x0f; const masked = (b1 & 0x80) !== 0;
  let len = b1 & 0x7f, off = 2;
  if (len === 126) { if (buf.length < 4) return null; len = buf.readUInt16BE(2); off = 4; }
  else if (len === 127) { if (buf.length < 10) return null; len = Number(buf.readBigUInt64BE(2)); off = 10; }
  let mask = null;
  if (masked) { if (buf.length < off + 4) return null; mask = buf.slice(off, off + 4); off += 4; }
  if (buf.length < off + len) return null;
  let payload = buf.slice(off, off + len);
  if (masked) { const o = Buffer.alloc(len); for (let i = 0; i < len; i++) o[i] = payload[i] ^ mask[i & 3]; payload = o; }
  return { opcode, payload, total: off + len };
}
function encodeText(str) {
  const p = Buffer.from(str, 'utf8'), len = p.length;
  let h;
  if (len < 126) h = Buffer.from([0x81, len]);
  else if (len < 65536) { h = Buffer.alloc(4); h[0] = 0x81; h[1] = 126; h.writeUInt16BE(len, 2); }
  else { h = Buffer.alloc(10); h[0] = 0x81; h[1] = 127; h.writeBigUInt64BE(BigInt(len), 2); }
  return Buffer.concat([h, p]);
}
server.listen(PORT, () => console.log('[sig] http+ws on :' + PORT + '  (open http://<PC-IP>:' + PORT + '/ )'));
