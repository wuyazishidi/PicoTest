// 零依赖 WebSocket 信令中转（RFC6455 自实现，无需 npm install）。
// 运行：node signaling.js   （默认端口 8765，PORT 可覆盖）
// 把任一客户端发来的文本(JSON: {type,sdp,candidate,sdpMid,sdpMLineIndex})广播给其他客户端。
const http = require('http');
const crypto = require('crypto');

const PORT = process.env.PORT || 8765;
const GUID = '258EAFA5-E914-47DA-95CA-C5AB0DC85B11';
const clients = new Set();

const server = http.createServer((req, res) => { res.writeHead(200); res.end('signaling relay'); });

server.on('upgrade', (req, socket) => {
  const key = req.headers['sec-websocket-key'];
  if (!key) { socket.destroy(); return; }
  const accept = crypto.createHash('sha1').update(key + GUID).digest('base64');
  socket.write(
    'HTTP/1.1 101 Switching Protocols\r\n' +
    'Upgrade: websocket\r\n' +
    'Connection: Upgrade\r\n' +
    'Sec-WebSocket-Accept: ' + accept + '\r\n\r\n'
  );
  clients.add(socket);
  console.log('[sig] client+  total=' + clients.size);

  let buf = Buffer.alloc(0);
  socket.on('data', (d) => {
    buf = Buffer.concat([buf, d]);
    let f;
    while ((f = decodeFrame(buf))) {
      buf = buf.slice(f.total);
      if (f.opcode === 0x8) { socket.end(); return; }         // close
      if (f.opcode === 0x1 || f.opcode === 0x2) {              // text/binary
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
  const b1 = buf[1];
  const opcode = buf[0] & 0x0f;
  const masked = (b1 & 0x80) !== 0;
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

server.listen(PORT, () => console.log('[sig] relay listening on ws://0.0.0.0:' + PORT));
