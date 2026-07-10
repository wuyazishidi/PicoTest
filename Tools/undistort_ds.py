#!/usr/bin/env python3
"""Split side-by-side stereo videos and undistort with a Kalibr Double-Sphere calibration.

Source layout (LeRobot-style):
    <input_dir>/episode_XXXXXX.mp4         # each frame = [ left | right ] side by side

Outputs (mp4 mirroring the source structure, one file per episode):
    <input_dir>_left/                      # left half, raw (still distorted)
    <input_dir>_right/                     # right half, raw
    <input_dir>_left_no_distortion/        # left half, undistorted
    <input_dir>_right_no_distortion/       # right half, undistorted

The cameras are calibrated with the Double Sphere (ds) model (Usenko et al. 2018),
intrinsics [xi, alpha, fx, fy, cx, cy], no separate distortion coefficients.
cam0 -> left, cam1 -> right.

Undistortion reprojects every output pixel through a virtual pinhole camera whose
horizontal FOV is set by --fov (default 140 deg).

When ffmpeg is available (pip install imageio-ffmpeg), the script uses ffmpeg's
native remap filter for the entire pipeline — this is much faster (no Python
per-frame loop, h264 encoding) and preserves VFR timestamps exactly.

Examples
--------
# Preview: dump sample frames (raw + undistorted) as PNGs
python3 undistort_ds.py --episodes 0 --preview 4

# Process one episode
python3 undistort_ds.py --episodes 0 \\
    --camchain machine_01_0-camchain.yaml \\
    --input-dir /path/to/videos/chunk-000/observation.images.ego_stereo

# Process everything
python3 undistort_ds.py --episodes all \\
    --camchain machine_01_0-camchain.yaml \\
    --input-dir /path/to/videos/chunk-000/observation.images.ego_stereo
"""
import argparse
import os
import sys
import glob
import json
import copy
import shutil
import subprocess
import tempfile
import multiprocessing as mp

import numpy as np
import cv2
import yaml

DEFAULT_INPUT = os.path.expanduser(
    "/inspire/hdd2/project/roboticsystem2/public/LHM-Data"
)

FOURCC_TO_CODEC = {
    "mp4v": "mpeg4", "fmp4": "mpeg4", "divx": "mpeg4", "xvid": "mpeg4",
    "avc1": "h264", "h264": "h264", "x264": "h264",
    "hev1": "hevc", "hvc1": "hevc", "hevc": "hevc",
    "vp09": "vp9", "vp80": "vp8", "av01": "av1", "mjpg": "mjpeg",
}


# --------------------------------------------------------------------------- #
# ffmpeg discovery
# --------------------------------------------------------------------------- #
def _find_ffmpeg():
    
    """Return path to ffmpeg binary, or None."""
    for name in ("ffmpeg",):
        try:
            subprocess.run([name, "-version"], stdout=subprocess.DEVNULL,
                           stderr=subprocess.DEVNULL, check=True)
            return name
        except (FileNotFoundError, subprocess.CalledProcessError):
            pass
    try:
        import imageio_ffmpeg
        return imageio_ffmpeg.get_ffmpeg_exe()
    except Exception:
        return None


def _probe_timescale(path, ffmpeg_exe):
    """Return the video stream's timescale (time_base denominator).

    Tries ffprobe first; falls back to parsing 'NNk tbn' from ``ffmpeg -i``.
    """
    import re
    ffprobe = ffmpeg_exe.replace("ffmpeg", "ffprobe") if "ffmpeg" in ffmpeg_exe else "ffprobe"
    try:
        r = subprocess.run(
            [ffprobe, "-v", "quiet", "-select_streams", "v:0",
             "-show_entries", "stream=time_base", "-of", "csv=p=0", path],
            capture_output=True, text=True, timeout=10,
        )
        if r.returncode == 0 and "/" in r.stdout.strip():
            return int(r.stdout.strip().split("/")[1])
    except Exception:
        pass
    try:
        r = subprocess.run([ffmpeg_exe, "-i", path],
                           capture_output=True, text=True, timeout=10)
        m = re.search(r'([\d.]+k?)\s+tbn', r.stderr)
        if m:
            v = m.group(1)
            return int(float(v[:-1]) * 1000) if v.endswith("k") else int(float(v))
    except Exception:
        pass
    return None


# --------------------------------------------------------------------------- #
# Double Sphere model
# --------------------------------------------------------------------------- #
def ds_project(X, Y, Z, xi, alpha, fx, fy, cx, cy):
    """3D ray (X,Y,Z) -> DS pixel coordinates (u,v), vectorised."""
    d1 = np.sqrt(X * X + Y * Y + Z * Z)
    k = xi * d1 + Z
    d2 = np.sqrt(X * X + Y * Y + k * k)
    norm = alpha * d2 + (1.0 - alpha) * k
    w1 = alpha / (1.0 - alpha) if alpha <= 0.5 else (1.0 - alpha) / alpha
    w2 = (w1 + xi) / np.sqrt(2.0 * w1 * xi + xi * xi + 1.0)
    valid = (norm > 1e-6) & (Z > -w2 * d1)
    norm_safe = np.where(valid, norm, 1.0)
    u = fx * X / norm_safe + cx
    v = fy * Y / norm_safe + cy
    return u, v, valid


def build_ds_maps(intr, cal_res, src_size, out_size, fov_deg):
    """Build remap tables: DS camera -> virtual pinhole.

    Returns (map_x, map_y) as float32 arrays.
    """
    xi, alpha, fx, fy, cx, cy = intr
    cal_w, cal_h = cal_res
    src_w, src_h = src_size
    sx, sy = src_w / cal_w, src_h / cal_h
    fx, fy, cx, cy = fx * sx, fy * sy, cx * sx, cy * sy
    W, H = out_size
    uu, vv = np.meshgrid(np.arange(W), np.arange(H))

    f_new = (W / 2.0) / np.tan(np.radians(fov_deg) / 2.0)
    X = (uu - W / 2.0) / f_new
    Y = (vv - H / 2.0) / f_new
    Z = np.ones_like(X)

    src_u, src_v, valid = ds_project(X, Y, Z, xi, alpha, fx, fy, cx, cy)
    map_x = np.where(valid, src_u, -1).astype(np.float32)
    map_y = np.where(valid, src_v, -1).astype(np.float32)
    return map_x, map_y


# --------------------------------------------------------------------------- #
# Calibration
# --------------------------------------------------------------------------- #
def load_calib(camchain_path):
    """Load a Kalibr camchain yaml -> {'cam0': {...}, 'cam1': {...}}."""
    with open(camchain_path) as f:
        data = yaml.safe_load(f)
    cams = {}
    for key in ("cam0", "cam1"):
        c = data[key]
        if c["camera_model"] != "ds":
            raise ValueError(
                f"{key}: this tool expects ds, got {c['camera_model']}"
            )
        intr = [float(x) for x in c["intrinsics"]]
        if len(intr) != 6:
            raise ValueError(
                f"{key}: ds intrinsics must be [xi, alpha, fx, fy, cx, cy], "
                f"got {len(intr)} values"
            )
        cams[key] = dict(
            intr=np.array(intr, float),
            res=tuple(int(x) for x in c["resolution"]),
        )
    return cams


def build_maps(cams, src_size, args):
    """Build {'left': (map_x, map_y), 'right': (map_x, map_y)} as float32."""
    ud_size = (args.out_width, args.out_height)
    return dict(
        left=build_ds_maps(cams["cam0"]["intr"], cams["cam0"]["res"], src_size, ud_size, args.fov),
        right=build_ds_maps(cams["cam1"]["intr"], cams["cam1"]["res"], src_size, ud_size, args.fov),
    )


# --------------------------------------------------------------------------- #
# I/O helpers
# --------------------------------------------------------------------------- #
def list_episodes(input_dir, spec):
    """spec: 'all' | '0' | '0,2,5' | '0-9'. Returns sorted list of mp4 paths."""
    files = sorted(glob.glob(os.path.join(input_dir, "episode_*.mp4")))
    if spec == "all":
        return files
    wanted = set()
    for tok in spec.split(","):
        tok = tok.strip()
        if "-" in tok:
            a, b = tok.split("-")
            wanted.update(range(int(a), int(b) + 1))
        else:
            wanted.add(int(tok))
    out = []
    for f in files:
        idx = int(os.path.basename(f).split("_")[1].split(".")[0])
        if idx in wanted:
            out.append(f)
    return out


def probe_src_size(eps):
    """Open the first episode to determine the split-half image size (w, h)."""
    cap = cv2.VideoCapture(eps[0])
    w = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    h = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    cap.release()
    if w <= 0 or h <= 0:
        raise RuntimeError(f"cannot read frame dimensions from {eps[0]}")
    return (w // 2, h)


# --------------------------------------------------------------------------- #
# Core: directory layout
# --------------------------------------------------------------------------- #
def derive_dirs(input_dir, args):
    parent = os.path.dirname(os.path.normpath(input_dir))
    name = os.path.basename(os.path.normpath(input_dir))
    split_dirs = dict(
        left=args.out_left or os.path.join(parent, name + "_left"),
        right=args.out_right or os.path.join(parent, name + "_right"),
    )
    undist_dirs = dict(
        left=args.out_left_undist or os.path.join(parent, name + "_left_no_distortion"),
        right=args.out_right_undist or os.path.join(parent, name + "_right_no_distortion"),
    )
    return split_dirs, undist_dirs


def collect_produced(split_dirs, undist_dirs, stages):
    produced = []
    if stages in ("split", "both"):
        for side in ("left", "right"):
            produced.append((os.path.basename(split_dirs[side]), split_dirs[side]))
    if stages in ("undistort", "both"):
        for side in ("left", "right"):
            produced.append((os.path.basename(undist_dirs[side]), undist_dirs[side]))
    return produced


def discover_produced(input_dir):
    parent = os.path.dirname(os.path.normpath(input_dir))
    src = os.path.basename(os.path.normpath(input_dir))
    out = []
    for d in sorted(glob.glob(os.path.join(parent, src + "_*"))):
        if os.path.isdir(d) and glob.glob(os.path.join(d, "episode_*.mp4")):
            out.append((os.path.basename(d), d))
    return out


# --------------------------------------------------------------------------- #
# LeRobot metadata (meta/info.json, meta/modality.json)
# --------------------------------------------------------------------------- #
def find_dataset_root(input_dir):
    d = os.path.abspath(input_dir)
    for _ in range(8):
        if os.path.isfile(os.path.join(d, "meta", "info.json")):
            return d
        parent = os.path.dirname(d)
        if parent == d:
            break
        d = parent
    return None


def probe_video(dir_path):
    files = sorted(glob.glob(os.path.join(dir_path, "episode_*.mp4")))
    if not files:
        return None
    cap = cv2.VideoCapture(files[0])
    w = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    h = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    fcc_int = int(cap.get(cv2.CAP_PROP_FOURCC))
    cap.release()
    if w <= 0 or h <= 0:
        return None
    fcc = "".join(chr((fcc_int >> 8 * i) & 0xFF) for i in range(4)).strip("\x00 ").lower()
    codec = FOURCC_TO_CODEC.get(fcc, fcc or "h264")
    return w, h, codec, len(files)


def _video_feature_entry(template, height, width, codec, fps):
    if template is not None and template.get("dtype") == "video":
        feat = copy.deepcopy(template)
    else:
        feat = {"dtype": "video", "info": {}}
    feat["dtype"] = "video"
    feat["shape"] = [height, width, 3]
    feat["names"] = ["height", "width", "channel"]
    info = feat.setdefault("info", {})
    info["video.height"] = height
    info["video.width"] = width
    info["video.channels"] = 3
    info["video.codec"] = codec
    info.setdefault("video.pix_fmt", "yuv420p")
    info.setdefault("video.is_depth_map", False)
    info["video.fps"] = fps
    info.setdefault("has_audio", False)
    return feat


def _dump_json(path, obj):
    bak = path + ".bak"
    if not os.path.exists(bak):
        shutil.copy2(path, bak)
    with open(path, "w") as f:
        json.dump(obj, f, indent=4, ensure_ascii=False)
        f.write("\n")


def update_dataset_metadata(input_dir, produced):
    root = find_dataset_root(input_dir)
    if root is None:
        print(f"  [meta] WARNING: no meta/info.json found at or above {input_dir}; "
              f"skipping metadata update", file=sys.stderr)
        return
    info_path = os.path.join(root, "meta", "info.json")
    with open(info_path) as f:
        info = json.load(f)
    features = info.setdefault("features", {})
    src_key = os.path.basename(os.path.normpath(input_dir))
    template = features.get(src_key)
    fps = int(info.get("fps") or 0) or 50
    total_eps = info.get("total_episodes")

    added, updated, codecs = [], [], set()
    for video_key, d in produced:
        probed = probe_video(d)
        if probed is None:
            print(f"  [meta] WARNING: no readable videos in {d}; skipping {video_key}",
                  file=sys.stderr)
            continue
        w, h, codec, nfiles = probed
        codecs.add(codec)
        if total_eps is not None and nfiles < total_eps:
            print(f"  [meta] NOTE: {video_key} has {nfiles} videos < total_episodes "
                  f"({total_eps}); re-run with --episodes all for a complete dataset")
        (updated if video_key in features else added).append(video_key)
        features[video_key] = _video_feature_entry(template, h, w, codec, fps)

    if not added and not updated:
        print("  [meta] no video keys to register (nothing produced).")
        return

    n_video_feats = sum(1 for v in features.values() if v.get("dtype") == "video")
    if total_eps is not None:
        info["total_videos"] = total_eps * n_video_feats

    _dump_json(info_path, info)

    mod_path = os.path.join(root, "meta", "modality.json")
    if os.path.isfile(mod_path):
        with open(mod_path) as f:
            mod = json.load(f)
        vid = mod.setdefault("video", {})
        prefix = "observation.images."
        for video_key, _ in produced:
            if video_key not in features:
                continue
            short = video_key[len(prefix):] if video_key.startswith(prefix) else video_key
            vid[short] = {"original_key": video_key}
        _dump_json(mod_path, mod)
        mod_note = " + modality.json"
    else:
        mod_note = ""

    print(f"  [meta] info.json{mod_note}: +{len(added)} new, {len(updated)} updated "
          f"video keys; total_videos={info.get('total_videos')}; codec={sorted(codecs)}")
    if added:
        print("         added: " + ", ".join(added))


# --------------------------------------------------------------------------- #
# ffmpeg-native episode processing (fast, preserves VFR timestamps)
# --------------------------------------------------------------------------- #
def save_maps_gray16(maps, tmpdir):
    """Save float32 maps as GRAY16LE raw files for ffmpeg remap filter.

    Returns {'left': (xmap_path, ymap_path), 'right': ...}.
    """
    paths = {}
    for side, (mx, my) in maps.items():
        xp = os.path.join(tmpdir, f"xmap_{side}.bin")
        yp = os.path.join(tmpdir, f"ymap_{side}.bin")
        mx16 = np.where(mx >= 0, np.clip(np.round(mx), 0, 65534), 65535).astype(np.uint16)
        my16 = np.where(my >= 0, np.clip(np.round(my), 0, 65534), 65535).astype(np.uint16)
        mx16.tofile(xp)
        my16.tofile(yp)
        paths[side] = (xp, yp)
    return paths


def process_episode_ffmpeg(path, split_dirs, undist_dirs, map_paths, out_size,
                           args, ffmpeg_exe):
    """Process one episode entirely in ffmpeg (crop + remap filter)."""
    base = os.path.basename(path)
    do_split = args.stages in ("split", "both")
    do_ud = args.stages in ("undistort", "both")
    ow, oh = out_size

    # Probe source fps so map streams use a matching timebase
    _cap = cv2.VideoCapture(path)
    src_fps = _cap.get(cv2.CAP_PROP_FPS) or args.fps
    _cap.release()

    # Probe source timescale so output MP4 uses the same PTS precision
    src_ts = _probe_timescale(path, ffmpeg_exe)
    ts_args = (["-enc_time_base", f"1/{src_ts}", "-video_track_timescale", str(src_ts)]
               if src_ts else [])

    inputs = ["-copyts", "-i", path]
    filter_parts = []
    output_args = []
    enc = ["-c:v", "libx264", "-preset", args.preset, "-crf", "18", "-bf", "0", "-pix_fmt", "yuv420p"]

    # Map file inputs (only needed for undistort)
    if do_ud:
        for side in ("left", "right"):
            xp, yp = map_paths[side]
            inputs += ["-f", "rawvideo", "-pix_fmt", "gray16le",
                       "-s", f"{ow}x{oh}", "-r", str(src_fps), "-i", xp]
            inputs += ["-f", "rawvideo", "-pix_fmt", "gray16le",
                       "-s", f"{ow}x{oh}", "-r", str(src_fps), "-i", yp]
        # input indices: 0=video, 1=xmap_left, 2=ymap_left, 3=xmap_right, 4=ymap_right

    # Build filter_complex
    if do_split and do_ud:
        filter_parts = [
            "[0]split=2[a][b]",
            "[a]crop=iw/2:ih:0:0,split=2[left_raw][left_src]",
            "[b]crop=iw/2:ih:iw/2:0,split=2[right_raw][right_src]",
            "[1]loop=-1:size=1,settb=AVTB[xl]", "[2]loop=-1:size=1,settb=AVTB[yl]",
            "[3]loop=-1:size=1,settb=AVTB[xr]", "[4]loop=-1:size=1,settb=AVTB[yr]",
            "[left_src][xl][yl]remap[left_ud]",
            "[right_src][xr][yr]remap[right_ud]",
        ]
        for side, label in [("left", "left_raw"), ("right", "right_raw")]:
            p = os.path.join(split_dirs[side], base)
            os.makedirs(os.path.dirname(p), exist_ok=True)
            output_args += ["-map", f"[{label}]", "-vsync", "0"] + enc + ts_args + [p]
        for side, label in [("left", "left_ud"), ("right", "right_ud")]:
            p = os.path.join(undist_dirs[side], base)
            os.makedirs(os.path.dirname(p), exist_ok=True)
            output_args += ["-map", f"[{label}]", "-vsync", "0"] + enc + ts_args + [p]
    elif do_split:
        filter_parts = [
            "[0]crop=iw/2:ih:0:0[left_raw]",
            "[0]crop=iw/2:ih:iw/2:0[right_raw]",
        ]
        for side, label in [("left", "left_raw"), ("right", "right_raw")]:
            p = os.path.join(split_dirs[side], base)
            os.makedirs(os.path.dirname(p), exist_ok=True)
            output_args += ["-map", f"[{label}]", "-vsync", "0"] + enc + ts_args + [p]
    elif do_ud:
        filter_parts = [
            "[0]split=2[a][b]",
            "[a]crop=iw/2:ih:0:0[left_src]",
            "[b]crop=iw/2:ih:iw/2:0[right_src]",
            "[1]loop=-1:size=1,settb=AVTB[xl]", "[2]loop=-1:size=1,settb=AVTB[yl]",
            "[3]loop=-1:size=1,settb=AVTB[xr]", "[4]loop=-1:size=1,settb=AVTB[yr]",
            "[left_src][xl][yl]remap[left_ud]",
            "[right_src][xr][yr]remap[right_ud]",
        ]
        for side, label in [("left", "left_ud"), ("right", "right_ud")]:
            p = os.path.join(undist_dirs[side], base)
            os.makedirs(os.path.dirname(p), exist_ok=True)
            output_args += ["-map", f"[{label}]", "-vsync", "0"] + enc + ts_args + [p]

    fc = "; ".join(filter_parts)
    cmd = [ffmpeg_exe, "-y", "-loglevel", "error"] + inputs + [
        "-filter_complex", fc,
    ] + output_args

    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode != 0:
        print(f"  [error] {base}: ffmpeg failed:\n{r.stderr.strip()}", file=sys.stderr)
        return
    print(f"  [done] {base}")


# --------------------------------------------------------------------------- #
# cv2 fallback episode processing
# --------------------------------------------------------------------------- #
def make_writer(path, size, fps, fourcc="mp4v"):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    w = cv2.VideoWriter(path, cv2.VideoWriter_fourcc(*fourcc), fps, size, isColor=True)
    if not w.isOpened():
        raise RuntimeError(f"cannot open VideoWriter for {path}")
    return w


def process_episode_cv2(path, split_dirs, undist_dirs, maps_cv2, args):
    """Fallback: per-frame processing with cv2 (slower, forces CFR output)."""
    base = os.path.basename(path)
    cap = cv2.VideoCapture(path)
    if not cap.isOpened() or cap.get(cv2.CAP_PROP_FRAME_COUNT) <= 0:
        print(f"  [skip] {base}: empty / unreadable")
        cap.release()
        return

    fps = cap.get(cv2.CAP_PROP_FPS) or args.fps
    w = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    h = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    half = w // 2
    if 2 * half != w:
        print(f"  [skip] {base}: width {w} not even, cannot split")
        cap.release()
        return
    split_size = (half, h)
    ud_size = (args.out_width, args.out_height)

    do_split = args.stages in ("split", "both")
    do_ud = args.stages in ("undistort", "both")

    writers = {}
    if do_split:
        writers["split_left"] = make_writer(os.path.join(split_dirs["left"], base), split_size, fps, args.fourcc)
        writers["split_right"] = make_writer(os.path.join(split_dirs["right"], base), split_size, fps, args.fourcc)
    if do_ud:
        writers["ud_left"] = make_writer(os.path.join(undist_dirs["left"], base), ud_size, fps, args.fourcc)
        writers["ud_right"] = make_writer(os.path.join(undist_dirs["right"], base), ud_size, fps, args.fourcc)

    n = 0
    while True:
        ok, frame = cap.read()
        if not ok:
            break
        left = frame[:, :half]
        right = frame[:, half:]
        if do_split:
            writers["split_left"].write(left)
            writers["split_right"].write(right)
        if do_ud:
            lu = cv2.remap(left, maps_cv2["left"][0], maps_cv2["left"][1],
                           cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=0)
            ru = cv2.remap(right, maps_cv2["right"][0], maps_cv2["right"][1],
                           cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=0)
            writers["ud_left"].write(lu)
            writers["ud_right"].write(ru)
        n += 1
    cap.release()
    for wtr in writers.values():
        wtr.release()
    print(f"  [done] {base}: {n} frames @ {fps:.1f}fps (cv2 fallback)")


# --------------------------------------------------------------------------- #
# Preview
# --------------------------------------------------------------------------- #
def preview_episode(path, cams, src_size, args):
    out = os.path.expanduser(args.preview_dir)
    os.makedirs(out, exist_ok=True)
    cap = cv2.VideoCapture(path)
    total = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    w = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    half = w // 2
    idxs = np.linspace(0, max(total - 1, 0), args.preview, dtype=int)

    maps = build_maps(cams, src_size, args)

    saved = []
    for i in idxs:
        cap.set(cv2.CAP_PROP_POS_FRAMES, i)
        ok, frame = cap.read()
        if not ok:
            continue
        left, right = frame[:, :half], frame[:, half:]
        cv2.imwrite(os.path.join(out, f"f{i:05d}_left_raw.png"), left)
        cv2.imwrite(os.path.join(out, f"f{i:05d}_right_raw.png"), right)
        for side, img, (mx, my) in [("left", left, maps["left"]),
                                     ("right", right, maps["right"])]:
            m1, m2 = cv2.convertMaps(mx, my, cv2.CV_16SC2, nninterpolation=False)
            ud = cv2.remap(img, m1, m2, cv2.INTER_LINEAR,
                           borderMode=cv2.BORDER_CONSTANT, borderValue=0)
            cv2.imwrite(os.path.join(out, f"f{i:05d}_{side}_undist.png"), ud)
        saved.append(int(i))
    cap.release()
    print(f"preview frames {saved} -> {out}")


# --------------------------------------------------------------------------- #
# Parallel execution
# --------------------------------------------------------------------------- #
_WORK = {}


def _init_worker_ffmpeg(map_paths, out_size, args, split_dirs, undist_dirs, ffmpeg_exe):
    _WORK.update(map_paths=map_paths, out_size=out_size, args=args,
                 split_dirs=split_dirs, undist_dirs=undist_dirs, ffmpeg_exe=ffmpeg_exe)


def _process_one_ffmpeg(path):
    process_episode_ffmpeg(path, _WORK["split_dirs"], _WORK["undist_dirs"],
                           _WORK["map_paths"], _WORK["out_size"],
                           _WORK["args"], _WORK["ffmpeg_exe"])
    return os.path.basename(path)


def _init_worker_cv2(cams, src_size, args, split_dirs, undist_dirs):
    cv2.setNumThreads(1)
    maps = build_maps(cams, src_size, args)
    maps_cv2 = {side: cv2.convertMaps(mx, my, cv2.CV_16SC2, nninterpolation=False)
                for side, (mx, my) in maps.items()}
    _WORK.update(maps_cv2=maps_cv2, args=args, split_dirs=split_dirs, undist_dirs=undist_dirs)


def _process_one_cv2(path):
    process_episode_cv2(path, _WORK["split_dirs"], _WORK["undist_dirs"],
                        _WORK["maps_cv2"], _WORK["args"])
    return os.path.basename(path)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--input-dir", default=DEFAULT_INPUT)
    ap.add_argument("--camchain", required=True, help="Kalibr camchain.yaml with ds model for cam0/cam1")
    ap.add_argument("--episodes", default="all", help="'all' | '0' | '0,2,5' | '0-9'")
    ap.add_argument("--stages", choices=["split", "undistort", "both"], default="both")
    ap.add_argument("--fov", type=float, default=140.0,
                    help="virtual pinhole horizontal FOV (deg); larger sees more edge distortion")
    ap.add_argument("--out-width", type=int, default=960)
    ap.add_argument("--out-height", type=int, default=540,
                    help="output height; 0 = auto from source aspect ratio")
    ap.add_argument("--preset", default="veryfast",
                    help="libx264 encode preset (ffmpeg backend). Encoding dominates "
                         "runtime; with CRF the quality is held constant, so faster "
                         "presets mainly trade a slightly larger file for more speed. "
                         "ultrafast/superfast/veryfast/faster/fast/medium/slow/...")
    ap.add_argument("--fourcc", default="mp4v",
                    help="VideoWriter fourcc (cv2 fallback only)")
    ap.add_argument("--fps", type=float, default=50.0, help="fallback fps if source has none")
    ap.add_argument("--workers", type=int, default=0,
                    help="parallel episode workers; 0 = auto (min(cpu, #episodes))")
    ap.add_argument("--no-meta-update", action="store_true",
                    help="do NOT register the new video keys in meta/info.json")
    ap.add_argument("--meta-only", action="store_true",
                    help="skip video processing; only (re)write metadata for output dirs "
                         "already on disk (auto-discovers <source_key>_* siblings)")
    ap.add_argument("--out-left", default=None)
    ap.add_argument("--out-right", default=None)
    ap.add_argument("--out-left-undist", default=None)
    ap.add_argument("--out-right-undist", default=None)
    ap.add_argument("--preview", type=int, default=0,
                    help="if >0, dump N sample frames (PNG) instead of videos")
    ap.add_argument("--preview-dir", default="/tmp/stereo_preview")
    args = ap.parse_args()

    if args.meta_only:
        produced = discover_produced(args.input_dir)
        if not produced:
            print(f"--meta-only: no output dirs found next to {args.input_dir}",
                  file=sys.stderr)
            sys.exit(1)
        print(f"--meta-only: registering {len(produced)} video keys "
              f"(no re-encoding):\n  " + "\n  ".join(k for k, _ in produced))
        update_dataset_metadata(args.input_dir, produced)
        return

    cams = load_calib(args.camchain)
    print(f"calibration: {args.camchain}  (fov={args.fov})")
    for k in ("cam0", "cam1"):
        intr = cams[k]["intr"]
        print(f"  {k}: xi={intr[0]:.4f} alpha={intr[1]:.4f} "
              f"f=({intr[2]:.1f},{intr[3]:.1f}) c=({intr[4]:.1f},{intr[5]:.1f})  "
              f"res={cams[k]['res']}")

    eps = list_episodes(args.input_dir, args.episodes)
    if not eps:
        print(f"no episodes matched '{args.episodes}' in {args.input_dir}", file=sys.stderr)
        sys.exit(1)

    src_size = probe_src_size(eps)
    cal_res = cams["cam0"]["res"]
    if src_size != cal_res:
        sx, sy = src_size[0] / cal_res[0], src_size[1] / cal_res[1]
        print(f"  NOTE: source half {src_size[0]}x{src_size[1]} != "
              f"camchain {cal_res[0]}x{cal_res[1]}; "
              f"scaling intrinsics by ({sx:.3f}, {sy:.3f})")

    if args.out_height <= 0:
        args.out_height = int(round(args.out_width * src_size[1] / src_size[0]))
        if args.out_height % 2 != 0:
            args.out_height += 1
    print(f"  output: {args.out_width}x{args.out_height} "
          f"(source half: {src_size[0]}x{src_size[1]})")

    if args.preview > 0:
        for p in eps:
            print(f"preview: {os.path.basename(p)}")
            preview_episode(p, cams, src_size, args)
        return

    split_dirs, undist_dirs = derive_dirs(args.input_dir, args)
    print(f"stages={args.stages}  outputs:")
    if args.stages in ("split", "both"):
        print(f"  split-left : {split_dirs['left']}")
        print(f"  split-right: {split_dirs['right']}")
    if args.stages in ("undistort", "both"):
        print(f"  undist-left : {undist_dirs['left']}")
        print(f"  undist-right: {undist_dirs['right']}")

    workers = args.workers if args.workers > 0 else min(os.cpu_count() or 1, len(eps))
    workers = max(1, min(workers, len(eps)))

    ffmpeg_exe = _find_ffmpeg()
    use_ffmpeg = ffmpeg_exe is not None

    if use_ffmpeg:
        print(f"backend: ffmpeg ({ffmpeg_exe})")
        maps = build_maps(cams, src_size, args)
        tmpdir = tempfile.mkdtemp(prefix="ds_undist_")
        map_paths = save_maps_gray16(maps, tmpdir)
        out_size = (args.out_width, args.out_height)

        try:
            if workers == 1:
                for p in eps:
                    print(f"episode: {os.path.basename(p)}")
                    process_episode_ffmpeg(p, split_dirs, undist_dirs,
                                           map_paths, out_size, args, ffmpeg_exe)
            else:
                print(f"parallel: {workers} workers over {len(eps)} episodes")
                ctx = mp.get_context("fork")
                with ctx.Pool(workers, initializer=_init_worker_ffmpeg,
                              initargs=(map_paths, out_size, args,
                                        split_dirs, undist_dirs, ffmpeg_exe)) as pool:
                    for _ in pool.imap_unordered(_process_one_ffmpeg, eps):
                        pass
        finally:
            shutil.rmtree(tmpdir, ignore_errors=True)
    else:
        print("backend: cv2 (install imageio-ffmpeg for faster processing)")
        maps = build_maps(cams, src_size, args)
        maps_cv2 = {side: cv2.convertMaps(mx, my, cv2.CV_16SC2, nninterpolation=False)
                    for side, (mx, my) in maps.items()}

        if workers == 1:
            cv2.setNumThreads(0)
            for p in eps:
                print(f"episode: {os.path.basename(p)}")
                process_episode_cv2(p, split_dirs, undist_dirs, maps_cv2, args)
        else:
            print(f"parallel: {workers} workers over {len(eps)} episodes")
            ctx = mp.get_context("fork")
            with ctx.Pool(workers, initializer=_init_worker_cv2,
                          initargs=(cams, src_size, args,
                                    split_dirs, undist_dirs)) as pool:
                for _ in pool.imap_unordered(_process_one_cv2, eps):
                    pass

    if not args.no_meta_update:
        update_dataset_metadata(args.input_dir, collect_produced(split_dirs, undist_dirs, args.stages))


if __name__ == "__main__":
    main()
