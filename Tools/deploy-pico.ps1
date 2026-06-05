# deploy-pico.ps1 — 部署 APK 到 PICO 头显并启动（M3 真机层）
# 用法：powershell -ExecutionPolicy Bypass -File Tools\deploy-pico.ps1 [-Apk Builds\PicoTest-dev.apk] [-AutoTest <scenario>]
# 前置：adb 可用（Unity Android 模块自带：<UnityEditor>\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools）

param(
    [string]$Apk = "",
    [string]$AutoTest = "",
    [string]$PackageName = "com.wuyazishidi.picotest"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

# 定位 adb：PATH 优先，回退到 Unity 自带
$adb = "adb"
if (-not (Get-Command adb -ErrorAction SilentlyContinue)) {
    $unityAdb = "D:\Unity\UnityEditor\Unity 2022.3.16f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
    if (Test-Path $unityAdb) { $adb = $unityAdb }
    else { Write-Host "FAILED: adb not found (install Unity Android module or add adb to PATH)" -ForegroundColor Red; exit 1 }
}

# preflight：设备在线 + 电量
$devices = & $adb devices | Select-String "device$"
if (-not $devices) { Write-Host "FAILED: no adb device connected/authorized" -ForegroundColor Red; exit 1 }
$battery = (& $adb shell dumpsys battery | Select-String "level:") -replace '\D', ''
if ([int]$battery -lt 15) { Write-Host "FAILED: battery $battery% < 15% — charge the headset" -ForegroundColor Red; exit 1 }
Write-Host "Device OK (battery $battery%)" -ForegroundColor Green

# 防休眠 + 清场
& $adb shell svc power stayon true
& $adb shell am force-stop $PackageName
& $adb logcat -c

# 安装
if ($Apk -eq "") {
    $found = Get-ChildItem (Join-Path $root "Builds") -Filter *.apk -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $found) { Write-Host "FAILED: no APK in Builds\ — run Tools\build-apk.ps1 first" -ForegroundColor Red; exit 1 }
    $Apk = $found.FullName
}
Write-Host "Installing $Apk ..."
& $adb install -r -d $Apk
if ($LASTEXITCODE -ne 0) { Write-Host "FAILED: adb install" -ForegroundColor Red; exit 1 }

# 启动（可带 AutoTest 场景参数）
$activity = "$PackageName/com.unity3d.player.UnityPlayerActivity"
if ($AutoTest -ne "") {
    & $adb shell am start -n $activity -e autotest $AutoTest
    Write-Host "Launched in AutoTest mode: $AutoTest" -ForegroundColor Cyan
} else {
    & $adb shell am start -n $activity
    Write-Host "Launched." -ForegroundColor Green
}
exit 0
