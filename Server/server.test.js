const test = require('node:test');
const assert = require('node:assert');
const { createApp, crc32 } = require('./server.js');
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');

function listen(app) {
  return new Promise(res => { const s = app.listen(0, () => res(s)); });
}
function req(port, method, p, body) {
  return new Promise((resolve, reject) => {
    const r = http.request({ port, method, path: p }, resp => {
      const chunks = [];
      resp.on('data', c => chunks.push(c));
      resp.on('end', () => resolve({ status: resp.statusCode, body: Buffer.concat(chunks), headers: resp.headers }));
    });
    r.on('error', reject);
    if (body) r.write(body);
    r.end();
  });
}

test('full upload flow: register -> head -> put -> complete', async () => {
  const dataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'ingest-'));
  const server = await listen(createApp(dataDir));
  const port = server.address().port;

  const manifest = JSON.stringify({ SessionId: 'abc-123' });
  assert.strictEqual((await req(port, 'POST', '/api/v1/sessions', manifest)).status, 201);

  const fileKey = encodeURIComponent('streams/body_pose/chunk_0001.ptc');
  const base = `/api/v1/sessions/abc-123/files/${fileKey}`;

  let head = await req(port, 'HEAD', base);
  assert.strictEqual(head.status, 200);
  assert.strictEqual(head.headers['content-length'], '0');

  const part1 = Buffer.from([1, 2, 3]);
  assert.strictEqual((await req(port, 'PUT', `${base}?offset=0`, part1)).status, 200);
  // 乱序拒绝
  assert.strictEqual((await req(port, 'PUT', `${base}?offset=99`, part1)).status, 409);
  const part2 = Buffer.from([4, 5]);
  assert.strictEqual((await req(port, 'PUT', `${base}?offset=3`, part2)).status, 200);

  // HEAD 现在应报 5
  head = await req(port, 'HEAD', base);
  assert.strictEqual(head.headers['content-length'], '5');

  const full = Buffer.from([1, 2, 3, 4, 5]);
  const good = JSON.stringify({ checksums: { 'streams/body_pose/chunk_0001.ptc': crc32(full) } });
  assert.strictEqual((await req(port, 'POST', '/api/v1/sessions/abc-123/complete', good)).status, 200);
  assert.ok(fs.existsSync(path.join(dataDir, 'abc-123', '.complete')));

  server.close();
});

test('complete rejects bad checksum with 422', async () => {
  const dataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'ingest-'));
  const server = await listen(createApp(dataDir));
  const port = server.address().port;
  await req(port, 'POST', '/api/v1/sessions', JSON.stringify({ SessionId: 's2' }));
  const fk = encodeURIComponent('a.bin');
  await req(port, 'PUT', `/api/v1/sessions/s2/files/${fk}?offset=0`, Buffer.from([9]));
  const bad = JSON.stringify({ checksums: { 'a.bin': 12345 } });
  assert.strictEqual((await req(port, 'POST', '/api/v1/sessions/s2/complete', bad)).status, 422);
  server.close();
});

test('crc32 matches known vector', () => {
  assert.strictEqual(crc32(Buffer.from('123456789')), 0xCBF43926);
});

test('register with SessionId="../evil" → 400, no file outside dataDir', async () => {
  const dataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'ingest-'));
  const server = await listen(createApp(dataDir));
  const port = server.address().port;
  const manifest = JSON.stringify({ SessionId: '../evil' });
  const r = await req(port, 'POST', '/api/v1/sessions', manifest);
  assert.strictEqual(r.status, 400);
  // dataDir 上一层目录不应产生 'evil' 目录
  assert.ok(!fs.existsSync(path.join(dataDir, '..', 'evil')));
  server.close();
});

test('PUT fileKey containing ".." → 400', async () => {
  const dataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'ingest-'));
  const server = await listen(createApp(dataDir));
  const port = server.address().port;
  await req(port, 'POST', '/api/v1/sessions', JSON.stringify({ SessionId: 's3' }));
  const evilKey = encodeURIComponent('../escape.bin');
  const r = await req(port, 'PUT', `/api/v1/sessions/s3/files/${evilKey}?offset=0`, Buffer.from([1]));
  assert.strictEqual(r.status, 400);
  server.close();
});

test('complete with checksums key "../x" → 400', async () => {
  const dataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'ingest-'));
  const server = await listen(createApp(dataDir));
  const port = server.address().port;
  await req(port, 'POST', '/api/v1/sessions', JSON.stringify({ SessionId: 's4' }));
  const bad = JSON.stringify({ checksums: { '../x': 0 } });
  const r = await req(port, 'POST', '/api/v1/sessions/s4/complete', bad);
  assert.strictEqual(r.status, 400);
  server.close();
});
