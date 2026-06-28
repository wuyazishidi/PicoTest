# 从 StreamingAssets/camera.mp4 (HEVC 立体 SBS) 抽一帧为 sbs_frame.png，供 RealFisheyeFrameOnDomeTests 使用。
# 真人采集数据不入库（宪法 #12）：camera.mp4 与产出的 sbs_frame.png 均已 gitignore。
# ffmpeg 自带 HEVC 解码器，不依赖 Windows 系统编解码器（Unity 编辑器 VideoPlayer 解不了 h265）。
# 用法： powershell -ExecutionPolicy Bypass -File Tools\extract-fisheye-frame.ps1 [-AtSeconds 10]
param([double]$AtSeconds = 10)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$src  = Join-Path $root 'Assets\StreamingAssets\camera.mp4'
$out  = Join-Path $root 'Assets\StreamingAssets\sbs_frame.png'

if (-not (Test-Path $src)) { Write-Error "源视频不存在: $src"; exit 1 }

# 定位 ffmpeg：PATH 优先，否则 WinGet Links
$ff = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source
if (-not $ff) { $ff = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Links\ffmpeg.exe' }
if (-not (Test-Path $ff)) { Write-Error "找不到 ffmpeg。装：winget install -e --id Gyan.FFmpeg"; exit 1 }

& $ff -ss $AtSeconds -i $src -frames:v 1 -y $out
if (Test-Path $out) { Write-Host "OK -> $out （在 Unity 里重跑 PlayMode 测试即可渲染 dome_real.png）" }
else { Write-Error "抽帧失败"; exit 1 }
