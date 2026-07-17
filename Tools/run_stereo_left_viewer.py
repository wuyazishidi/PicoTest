#!/usr/bin/env python3
"""Standalone **WebRTC** viewer for the LEFT eye of the robot's stereo camera.

A low-latency sibling of ``run_camera_viewer.py``. That viewer forwards every
camera mount as MJPEG; this one cares about a single stream — the ``ego_stereo``
mount — and shows only its **left eye**, delivered over **WebRTC** for minimal
glass-to-glass latency. Point any browser on the LAN at
``http://<robot-ip>:8888``; the client needs nothing but a browser.

Why WebRTC instead of MJPEG
---------------------------
MJPEG ships a fresh JPEG per frame down an HTTP multipart stream — every frame
pays a full re-encode on the server and rides a TCP connection that buffers and
head-of-line-blocks under loss. WebRTC instead negotiates a real-time video
codec (VP8/H264) carried over SRTP/UDP: frames are pushed the instant they
arrive, the encoder runs in inter-frame (delta) mode, and packet loss degrades
quality instead of stalling. On a LAN this typically cuts end-to-end latency
from a few hundred ms (MJPEG) down to tens of ms.

``ego_stereo`` publishes a single left|right side-by-side composite JPEG (the
two fisheye eyes stitched horizontally — see ``sbs_stereo_camera.py``). To show
only the left eye we **decode** it, crop the left half (columns ``0:w//2``), and
hand the raw BGR ndarray to the WebRTC encoder — no JPEG re-encode on the wire.

Color note
----------
The publish path runs ``cv2.imencode`` on RGB arrays without a BGR conversion,
so the decoded ndarray has its red/blue channels swapped relative to true BGR.
Same as the MJPEG viewer, the page applies an SVG ``feColorMatrix`` filter that
swaps R/B in the browser (zero server cost). Feeding the ndarray to WebRTC as
``bgr24`` reproduces the exact channel ordering the MJPEG path had, so the
"修正颜色 (R/B)" checkbox keeps the same meaning (on by default).

Depends on aiortc + av + aiohttp + pyzmq + msgpack + opencv + numpy + stdlib
(no gear_sonic import). Install the WebRTC stack with: ``pip install aiortc``.

Usage (on the robot):
    cd ~/code/LHM-Robot/S0 && source .venv_data_collection/bin/activate
    python gear_sonic/scripts/run_stereo_left_viewer.py
    # then open http://<robot-ip>:8888 in any browser

    # Subscribe to a remote host / custom stereo port / a different web port:
    python gear_sonic/scripts/run_stereo_left_viewer.py --camera-host 192.168.3.96
    python gear_sonic/scripts/run_stereo_left_viewer.py --port 9000
    python gear_sonic/scripts/run_stereo_left_viewer.py --stereo-port 5571
"""

from __future__ import annotations

import argparse
import asyncio
import fractions
import json
import threading
import time

import cv2
import msgpack
import numpy as np
import zmq
from aiohttp import web
from aiortc import RTCPeerConnection, RTCSessionDescription, VideoStreamTrack
from av import VideoFrame

# ego_stereo PUB port — must match composed_camera.DEFAULT_PORT_BY_MOUNT.
# Hardcoded (not imported) so this stays a zero-gear_sonic-dependency script.
DEFAULT_STEREO_PORT: int = 5571
STEREO_MOUNT: str = "ego_stereo"

LIVE_TIMEOUT = 3.0  # the stream counts as "live" if it produced a frame this recently
VIDEO_CLOCK_RATE = 90000  # 90 kHz RTP video clock — pts are expressed in these ticks


class LeftEyeStream:
    """Holds the latest LEFT-eye BGR frame, cropped from the ego_stereo composite.

    A background ZMQ SUB thread keeps only the freshest frame (CONFLATE=1),
    decodes it, crops the left half, and notifies any waiting WebRTC tracks.
    Multiple browser clients share the one subscription. Unlike the old MJPEG
    viewer there is no re-encode here — the raw ndarray goes straight to the
    WebRTC video encoder.
    """

    def __init__(self, host: str, port: int, ctx: zmq.Context):
        self.host = host
        self.port = port
        self._ctx = ctx
        self._cond = threading.Condition()
        self._frame: np.ndarray | None = None  # contiguous BGR uint8, even dims
        self._seq = 0
        self._last_rx = 0.0
        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._run, name="sub-ego_stereo_left", daemon=True)

    def start(self) -> None:
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()

    def _run(self) -> None:
        sock = self._ctx.socket(zmq.SUB)
        sock.setsockopt(zmq.SUBSCRIBE, b"")
        sock.setsockopt(zmq.RCVHWM, 2)
        sock.setsockopt(zmq.CONFLATE, 1)  # keep only the most recent message
        sock.setsockopt(zmq.LINGER, 0)
        sock.connect(f"tcp://{self.host}:{self.port}")
        poller = zmq.Poller()
        poller.register(sock, zmq.POLLIN)
        try:
            while not self._stop.is_set():
                if not dict(poller.poll(timeout=500)):
                    continue
                try:
                    raw = sock.recv(flags=zmq.NOBLOCK)
                except zmq.Again:
                    continue
                frame = self._left_eye_frame(raw)
                if frame is None:
                    continue
                with self._cond:
                    self._frame = frame
                    self._seq += 1
                    self._last_rx = time.time()
                    self._cond.notify_all()
        finally:
            sock.close(0)

    def _left_eye_frame(self, raw: bytes) -> np.ndarray | None:
        """msgpack blob -> left|right composite JPEG -> contiguous left-half BGR ndarray."""
        try:
            msg = msgpack.unpackb(raw, raw=False)
        except Exception:
            return None
        if not isinstance(msg, dict):
            return None
        images = msg.get("images")
        if not isinstance(images, dict) or not images:
            return None
        value = images.get(STEREO_MOUNT)
        if value is None:
            value = next(iter(images.values()), None)
        if not isinstance(value, (bytes, bytearray)):
            return None  # non-JPEG payload (e.g. raw ndarray dict) — unsupported
        frame = cv2.imdecode(np.frombuffer(bytes(value), dtype=np.uint8), cv2.IMREAD_COLOR)
        if frame is None or frame.ndim != 3 or frame.shape[1] < 2:
            return None
        left = frame[:, : frame.shape[1] // 2]  # left|right SBS -> left half
        # Trim to even dimensions — VP8/H264 encode to 4:2:0 and dislike odd sizes.
        h = left.shape[0] & ~1
        w = left.shape[1] & ~1
        left = left[:h, :w]
        return np.ascontiguousarray(left)

    def wait_frame(self, last_seq: int, timeout: float) -> tuple[np.ndarray | None, int]:
        """Block until a frame newer than last_seq arrives (or timeout). Returns (frame, seq)."""
        with self._cond:
            if self._seq == last_seq:
                self._cond.wait(timeout)
            return self._frame, self._seq

    def is_live(self) -> bool:
        return self._frame is not None and (time.time() - self._last_rx) < LIVE_TIMEOUT


class LeftEyeTrack(VideoStreamTrack):
    """A WebRTC video track that emits the freshest left-eye frame as it arrives.

    Pacing follows the camera, not a fixed clock: ``recv`` blocks (off the event
    loop, in a worker thread) until the SUB thread publishes a frame newer than
    the one we last sent. Presentation timestamps come from the wall clock so the
    browser plays frames out at their true cadence. An optional ``min_interval``
    drops frames to cap the send rate.
    """

    kind = "video"

    def __init__(self, stream: LeftEyeStream, min_interval: float = 0.0):
        super().__init__()
        self._stream = stream
        self._min_interval = min_interval
        self._last_seq = -1
        self._start: float | None = None
        self._last_sent = 0.0

    async def recv(self) -> VideoFrame:
        loop = asyncio.get_event_loop()
        while True:
            arr, seq = await loop.run_in_executor(
                None, self._stream.wait_frame, self._last_seq, 1.0
            )
            if arr is None or seq == self._last_seq:
                continue  # no new frame yet; loop so cancellation can still fire
            now = time.monotonic()
            if self._min_interval and (now - self._last_sent) < self._min_interval:
                self._last_seq = seq  # consume but skip to honour --max-fps
                continue
            self._last_seq = seq
            self._last_sent = now
            if self._start is None:
                self._start = now
            frame = VideoFrame.from_ndarray(arr, format="bgr24")
            frame.pts = int((now - self._start) * VIDEO_CLOCK_RATE)
            frame.time_base = fractions.Fraction(1, VIDEO_CLOCK_RATE)
            return frame


class ViewerState:
    """Shared server state: the single left-eye stream + render options."""

    def __init__(self, stream: LeftEyeStream, camera_host: str, max_fps: float):
        self.stream = stream
        self.camera_host = camera_host
        self.min_interval = (1.0 / max_fps) if max_fps and max_fps > 0 else 0.0
        self.pcs: set[RTCPeerConnection] = set()


INDEX_HTML = """<!doctype html>
<html lang="zh">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>立体相机左目预览 (WebRTC) — __HOST__</title>
<style>
  :root { color-scheme: dark; }
  body { margin: 0; background: #111; color: #ddd; font: 14px/1.4 system-ui, sans-serif; }
  header { display: flex; align-items: center; gap: 16px; flex-wrap: wrap;
           padding: 10px 16px; background: #1b1b1b; border-bottom: 1px solid #333;
           position: sticky; top: 0; z-index: 10; }
  header h1 { font-size: 16px; margin: 0; font-weight: 600; }
  header .host { color: #8ab; font-family: monospace; }
  header label { display: inline-flex; align-items: center; gap: 6px; cursor: pointer; user-select: none; }
  #wrap { display: flex; justify-content: center; padding: 12px; }
  .tile { background: #000; border: 1px solid #333; border-radius: 8px; overflow: hidden;
          display: flex; flex-direction: column; max-width: 960px; width: 100%; }
  .tile .bar { display: flex; justify-content: space-between; align-items: center;
               padding: 6px 10px; background: #1b1b1b; font-size: 13px; }
  .tile .name { font-weight: 600; }
  .tile .fps { color: #8c8; font-family: monospace; font-size: 12px; }
  .tile video { width: 100%; height: auto; display: block; background: #000; }
  .tile.offline { opacity: .45; }
  .tile.offline .fps { color: #c66; }
  .swap video { filter: url(#swapRB); }
  /* 纯显示旋转：0/90/180/270°（点 ⟳）。不影响转发的视频流。 */
  .tile.r90  video { transform: rotate(90deg); }
  .tile.r180 video { transform: rotate(180deg); }
  .tile.r270 video { transform: rotate(270deg); }
  .tile .barright { display: inline-flex; align-items: center; gap: 10px; }
  .rotbtn { background: #2a2a2a; color: #ddd; border: 1px solid #444; border-radius: 6px;
            font: 12px/1 monospace; padding: 3px 8px; cursor: pointer; }
  .rotbtn:hover { background: #333; }
  .empty { padding: 40px; text-align: center; color: #888; }
</style>
</head>
<body>
<svg width="0" height="0" style="position:absolute" aria-hidden="true">
  <filter id="swapRB" color-interpolation-filters="sRGB">
    <feColorMatrix type="matrix" values="0 0 1 0 0  0 1 0 0 0  1 0 0 0 0  0 0 0 1 0"/>
  </filter>
</svg>
<header>
  <h1>立体相机左目预览 · WebRTC</h1>
  <span class="host">__HOST__</span>
  <label><input type="checkbox" id="swap" checked> 修正颜色 (R/B 交换)</label>
  <span id="status" style="color:#888"></span>
</header>
<div id="wrap" class="swap"></div>
<div id="empty" class="empty">正在等待 ego_stereo 数据… 确认 composed_camera 已在运行。</div>

<script>
const grid = document.getElementById('wrap');
const emptyEl = document.getElementById('empty');
const swapBox = document.getElementById('swap');
const statusEl = document.getElementById('status');
let tile = null;   // {el, video, fpsEl, rot, count, t0}

swapBox.addEventListener('change', () => {
  grid.classList.toggle('swap', swapBox.checked);
});

function makeTile() {
  const el = document.createElement('div');
  el.className = 'tile';
  const bar = document.createElement('div');
  bar.className = 'bar';
  const name = document.createElement('span');
  name.className = 'name'; name.textContent = 'ego_stereo · 左目';
  const right = document.createElement('span');
  right.className = 'barright';
  const rotBtn = document.createElement('button');
  rotBtn.className = 'rotbtn'; rotBtn.title = '旋转 90°（仅预览）';
  const fps = document.createElement('span');
  fps.className = 'fps'; fps.textContent = '— fps';
  right.appendChild(rotBtn); right.appendChild(fps);
  bar.appendChild(name); bar.appendChild(right);
  const video = document.createElement('video');
  video.autoplay = true; video.muted = true; video.playsInline = true;
  const t = {el, video, fpsEl: fps, rot: 0, count: 0, t0: performance.now()};
  function applyRot() {
    el.classList.remove('r90', 'r180', 'r270');
    if (t.rot) el.classList.add('r' + t.rot);
    rotBtn.textContent = '⟳ ' + t.rot + '°';
  }
  rotBtn.addEventListener('click', () => { t.rot = (t.rot + 90) % 360; applyRot(); });
  applyRot();
  // Count decoded frames for a real fps readout (Chrome/Edge/Safari).
  if ('requestVideoFrameCallback' in HTMLVideoElement.prototype) {
    const onFrame = () => {
      t.count++;
      const dt = (performance.now() - t.t0) / 1000;
      if (dt >= 1.0) { t.fpsEl.textContent = (t.count / dt).toFixed(1) + ' fps'; t.count = 0; t.t0 = performance.now(); }
      video.requestVideoFrameCallback(onFrame);
    };
    video.requestVideoFrameCallback(onFrame);
  } else {
    t.fpsEl.textContent = 'WebRTC';
  }
  el.appendChild(bar); el.appendChild(video);
  grid.appendChild(el);
  return t;
}

async function negotiate() {
  const pc = new RTCPeerConnection();
  pc.addTransceiver('video', {direction: 'recvonly'});
  pc.addEventListener('track', (e) => {
    if (!tile) tile = makeTile();
    tile.video.srcObject = e.streams[0];
    tile.el.classList.remove('offline');
    emptyEl.style.display = 'none';
  });
  pc.addEventListener('connectionstatechange', () => {
    if (['failed', 'disconnected', 'closed'].includes(pc.connectionState)) {
      statusEl.textContent = '连接中断，重连中…';
      if (tile) tile.el.classList.add('offline');
      try { pc.close(); } catch (_) {}
      setTimeout(start, 1500);
    } else if (pc.connectionState === 'connected') {
      statusEl.textContent = '';
    }
  });
  const offer = await pc.createOffer();
  await pc.setLocalDescription(offer);
  // Wait for ICE gathering to finish so we can post one complete SDP (no trickle).
  await new Promise((resolve) => {
    if (pc.iceGatheringState === 'complete') return resolve();
    const check = () => {
      if (pc.iceGatheringState === 'complete') {
        pc.removeEventListener('icegatheringstatechange', check);
        resolve();
      }
    };
    pc.addEventListener('icegatheringstatechange', check);
  });
  const resp = await fetch('/offer', {
    method: 'POST',
    headers: {'Content-Type': 'application/json'},
    body: JSON.stringify({sdp: pc.localDescription.sdp, type: pc.localDescription.type}),
  });
  const answer = await resp.json();
  await pc.setRemoteDescription(answer);
  return pc;
}

let starting = false;
async function start() {
  if (starting) return;
  starting = true;
  try {
    statusEl.textContent = '正在建立 WebRTC 连接…';
    await negotiate();
  } catch (e) {
    statusEl.textContent = '连接失败，重试中…';
    setTimeout(() => { starting = false; start(); }, 2000);
    return;
  }
  starting = false;
}
start();
</script>
</body>
</html>
"""


async def index(request: web.Request) -> web.Response:
    state: ViewerState = request.app["state"]
    html = INDEX_HTML.replace("__HOST__", state.camera_host)
    return web.Response(text=html, content_type="text/html")


async def status(request: web.Request) -> web.Response:
    state: ViewerState = request.app["state"]
    return web.json_response({"live": state.stream.is_live()})


async def healthz(request: web.Request) -> web.Response:
    return web.Response(text="ok")


async def offer(request: web.Request) -> web.Response:
    state: ViewerState = request.app["state"]
    params = await request.json()
    desc = RTCSessionDescription(sdp=params["sdp"], type=params["type"])

    pc = RTCPeerConnection()
    state.pcs.add(pc)

    @pc.on("connectionstatechange")
    async def _on_state() -> None:
        if pc.connectionState in ("failed", "closed", "disconnected"):
            await pc.close()
            state.pcs.discard(pc)

    pc.addTrack(LeftEyeTrack(state.stream, state.min_interval))

    await pc.setRemoteDescription(desc)
    answer = await pc.createAnswer()
    await pc.setLocalDescription(answer)
    return web.json_response(
        {"sdp": pc.localDescription.sdp, "type": pc.localDescription.type}
    )


async def on_shutdown(app: web.Application) -> None:
    state: ViewerState = app["state"]
    await asyncio.gather(*(pc.close() for pc in state.pcs), return_exceptions=True)
    state.pcs.clear()


def main() -> int:
    parser = argparse.ArgumentParser(
        description="独立 WebRTC 预览：只取 ego_stereo 立体相机的左目，浏览器零依赖、超低延迟观看。"
    )
    parser.add_argument(
        "--camera-host", default="localhost",
        help="ego_stereo PUB 流所在主机。默认 localhost（本脚本就跑在机器人上）。",
    )
    parser.add_argument(
        "--port", type=int, default=8888,
        help="web 服务监听端口（默认 8888，浏览器访问 http://<机器人IP>:8888）。",
    )
    parser.add_argument(
        "--bind", default="0.0.0.0",
        help="web 服务绑定地址（默认 0.0.0.0，对外可见）。",
    )
    parser.add_argument(
        "--stereo-port", type=int, default=DEFAULT_STEREO_PORT,
        help=f"ego_stereo 相机的 PUB 端口（默认 {DEFAULT_STEREO_PORT}）。",
    )
    parser.add_argument(
        "--max-fps", type=float, default=0.0,
        help="WebRTC 流的最大发送帧率，0=不限（默认，跟随相机原生帧率）。",
    )
    args = parser.parse_args()

    ctx = zmq.Context.instance()
    stream = LeftEyeStream(args.camera_host, args.stereo_port, ctx)
    stream.start()

    state = ViewerState(stream, args.camera_host, args.max_fps)

    app = web.Application()
    app["state"] = state
    app.router.add_get("/", index)
    app.router.add_get("/index.html", index)
    app.router.add_get("/status", status)
    app.router.add_get("/healthz", healthz)
    app.router.add_post("/offer", offer)
    app.on_shutdown.append(on_shutdown)

    print(f"相机源：tcp://{args.camera_host}:{args.stereo_port}  挂载点：{STEREO_MOUNT}（仅左目）")
    print(f"WebRTC 预览已启动：http://<机器人IP>:{args.port}  （本机：http://localhost:{args.port}）")
    print(f"最大发送帧率：{'不限' if args.max_fps <= 0 else f'{args.max_fps} fps'}")
    print("按 Ctrl-C 退出。")
    try:
        web.run_app(app, host=args.bind, port=args.port, print=None)
    except KeyboardInterrupt:
        print("\n正在关闭…")
    finally:
        stream.stop()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
