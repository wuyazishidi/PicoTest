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

function createApp(dataDir) {
  const app = express();

  const sessionDir = id => path.join(dataDir, id);
  const fileOf = (id, fileKey) => {
    const rel = decodeURIComponent(fileKey);
    if (rel.includes('..')) throw new Error('path traversal');
    return path.join(sessionDir(id), rel);
  };

  app.get('/health', (_, res) => res.json({ status: 'ok' }));

  app.post('/api/v1/sessions', express.raw({ type: () => true, limit: '10mb' }), (req, res) => {
    const manifest = JSON.parse(req.body.toString('utf8'));
    const id = manifest.SessionId;
    if (!id) return res.status(400).json({ error: 'SessionId missing' });
    fs.mkdirSync(sessionDir(id), { recursive: true });
    fs.writeFileSync(path.join(sessionDir(id), 'manifest.json'), req.body);
    res.status(201).json({ id });
  });

  app.head('/api/v1/sessions/:id/files/:fileKey', (req, res) => {
    const f = fileOf(req.params.id, req.params.fileKey);
    const len = fs.existsSync(f) ? fs.statSync(f).size : 0;
    // HEAD 无 body —— offset 经 Content-Length 头返回（C# 端读这个头）
    res.set('Content-Length', String(len)).status(200).end();
  });

  app.put('/api/v1/sessions/:id/files/:fileKey',
    express.raw({ type: () => true, limit: '64mb' }), (req, res) => {
      const f = fileOf(req.params.id, req.params.fileKey);
      const offset = Number(req.query.offset);
      fs.mkdirSync(path.dirname(f), { recursive: true });
      const current = fs.existsSync(f) ? fs.statSync(f).size : 0;
      if (current !== offset) return res.status(409).json({ expected: current });
      fs.appendFileSync(f, req.body);
      res.status(200).json({ received: req.body.length });
    });

  app.post('/api/v1/sessions/:id/complete',
    express.raw({ type: () => true, limit: '10mb' }), (req, res) => {
      const { checksums } = JSON.parse(req.body.toString('utf8'));
      for (const [rel, expected] of Object.entries(checksums)) {
        if (rel.includes('..')) return res.status(400).json({ error: 'path traversal' });
        const f = path.join(sessionDir(req.params.id), rel);
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
