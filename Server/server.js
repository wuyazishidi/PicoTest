// PicoTest 数据接收服务（契约见 openapi.yaml）
const express = require('express');
const fs = require('node:fs');
const path = require('node:path');

// 与 C# PicoTest.Core.Schema.Crc32 相同的 IEEE CRC-32
const CRC_TABLE = (() => {
  const t = new Uint32Array(256);
  for (let i = 0; i < 256; i++) {
    let c = i;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1;
    t[i] = c >>> 0;
  }
  return t;
})();
function crc32(buf) {
  let crc = 0xFFFFFFFF;
  for (const b of buf) crc = CRC_TABLE[(crc ^ b) & 0xFF] ^ (crc >>> 8);
  return (crc ^ 0xFFFFFFFF) >>> 0;
}

const SESSION_ID_RE = /^[A-Za-z0-9._-]+$/;
function validId(id) {
  // '..' 能匹配正则（两个 '.'），需额外拒绝；'/' 与 '\\' 已被正则排除
  return typeof id === 'string' && SESSION_ID_RE.test(id) && !id.includes('..');
}

function createApp(dataDir) {
  const app = express();

  const sessionDir = id => path.join(dataDir, id);

  // Express 已将 :fileKey 段解码一次（把 %2F 还原为 '/'），不再二次 decodeURIComponent。
  // 用路径包含性检查替代字符串黑名单，彻底堵死各类编码绕过。
  const fileOf = (id, fileKey) => {
    const rel = fileKey; // Express 已解码一次，不再二次 decodeURIComponent
    const base = path.resolve(sessionDir(id));
    const abs = path.resolve(base, rel);
    if (abs !== base && !abs.startsWith(base + path.sep)) throw new Error('path traversal');
    return abs;
  };

  app.get('/health', (_, res) => res.json({ status: 'ok' }));

  app.post('/api/v1/sessions', express.raw({ type: () => true, limit: '10mb' }), (req, res) => {
    const manifest = JSON.parse(req.body.toString('utf8'));
    const id = manifest.SessionId;
    if (!id) return res.status(400).json({ error: 'SessionId missing' });
    if (!validId(id)) return res.status(400).json({ error: 'SessionId invalid' });
    fs.mkdirSync(sessionDir(id), { recursive: true });
    fs.writeFileSync(path.join(sessionDir(id), 'manifest.json'), req.body);
    res.status(201).json({ id });
  });

  app.head('/api/v1/sessions/:id/files/:fileKey', (req, res) => {
    if (!validId(req.params.id)) return res.status(400).json({ error: 'SessionId invalid' });
    let f;
    try { f = fileOf(req.params.id, req.params.fileKey); }
    catch { return res.status(400).json({ error: 'path traversal' }); }
    const len = fs.existsSync(f) ? fs.statSync(f).size : 0;
    // HEAD 无 body —— offset 经 Content-Length 头返回（C# 端读这个头）
    res.set('Content-Length', String(len)).status(200).end();
  });

  app.put('/api/v1/sessions/:id/files/:fileKey',
    express.raw({ type: () => true, limit: '64mb' }), (req, res) => {
      if (!validId(req.params.id)) return res.status(400).json({ error: 'SessionId invalid' });
      let f;
      try { f = fileOf(req.params.id, req.params.fileKey); }
      catch { return res.status(400).json({ error: 'path traversal' }); }
      const offset = Number(req.query.offset);
      // 单进程事件循环内 statSync→appendFileSync 无 await 间隙，无 TOCTOU；若未来多进程共享 dataDir 需加锁
      fs.mkdirSync(path.dirname(f), { recursive: true });
      const current = fs.existsSync(f) ? fs.statSync(f).size : 0;
      if (current !== offset) return res.status(409).json({ expected: current });
      fs.appendFileSync(f, req.body);
      res.status(200).json({ received: req.body.length });
    });

  app.post('/api/v1/sessions/:id/complete',
    express.raw({ type: () => true, limit: '10mb' }), (req, res) => {
      if (!validId(req.params.id)) return res.status(400).json({ error: 'SessionId invalid' });
      const { checksums } = JSON.parse(req.body.toString('utf8'));
      for (const [rel, expected] of Object.entries(checksums)) {
        // 用路径包含性检查替代字符串黑名单
        const base = path.resolve(sessionDir(req.params.id));
        const abs = path.resolve(base, rel);
        if (abs === base || !abs.startsWith(base + path.sep))
          return res.status(400).json({ error: 'path traversal' });
        const f = abs;
        if (!fs.existsSync(f)) return res.status(422).json({ missing: rel });
        const actual = crc32(fs.readFileSync(f));
        if (actual !== Number(expected) >>> 0)
          return res.status(422).json({ mismatch: rel, expected, actual });
      }
      fs.writeFileSync(path.join(sessionDir(req.params.id), '.complete'), new Date().toISOString());
      res.status(200).json({ verified: Object.keys(checksums).length });
    });

  return app;
}

if (require.main === module) {
  const dataDir = process.env.INGEST_DATA_DIR || path.join(__dirname, 'data');
  const port = Number(process.env.INGEST_PORT || 8077);
  fs.mkdirSync(dataDir, { recursive: true });
  createApp(dataDir).listen(port, () => console.log(`[ingest] listening :${port}, data -> ${dataDir}`));
}

module.exports = { createApp, crc32 };
