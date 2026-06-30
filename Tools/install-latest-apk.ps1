# install-latest-apk.ps1 — 用 adb 把"最近构建的 APK"安装到 PICO 设备
# 在标准产物目录里挑出修改时间最新的 .apk，adb install -r -d 安装（可选启动）。
# 用法：
#   powershell -ExecutionPolicy Bypass -File Tools\install-latest-apk.ps1                 # 装最新 APK
#   powershell -ExecutionPolicy Bypass -File Tools\install-latest-apk.ps1 -Launch         # 装完并启动
#   powershell -ExecutionPolicy Bypass -File Tools\install-latest-apk.ps1 -Path Build\X.apk   # 指定 APK 或目录
#   powershell -ExecutionPolicy Bypass -File Tools\install-latest-apk.ps1 -Serial <设备序列号>  # 多设备时指定
# 退出码：0 = 安装成功，1 = 失败。
# 前置：adb 可用（Unity Android 模块自带 platform-tools，无则回退）。

param(
    [string]$Path = "",          # 指定 .apk 文件或要搜索的目录；留空则搜索标准产物目录
    [string]$Serial = "",        # 多设备时指定目标设备序列号
    [switch]$Launch,             # 安装后启动应用
    [string]$PackageName = "com.wuyazishidi.picotest"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

# 1) 定位 adb：PATH 优先，回退到 Unity 自带
$adb = "adb"
if (-not (Get-Command adb -ErrorAction SilentlyContinue)) {
    $unityAdb = "D:\Unity\UnityEditor\Unity 2022.3.16f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
    if (Test-Path $unityAdb) { $adb = $unityAdb }
    else { Write-Host "FAILED: adb not found (install Unity Android module or add adb to PATH)" -ForegroundColor Red; exit 1 }
}

# 2) 选出要安装的 APK
function Find-LatestApk([string]$dir) {
    if (-not (Test-Path $dir)) { return $null }
    Get-ChildItem $dir -Filter *.apk -File -Recurse -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

$apk = $null
if ($Path -ne "") {
    $resolved = if ([System.IO.Path]::IsPathRooted($Path)) { $Path } else { Join-Path $root $Path }
    if (Test-Path $resolved -PathType Leaf) {
        $apk = Get-Item $resolved
    } elseif (Test-Path $resolved -PathType Container) {
        $apk = Find-LatestApk $resolved
        if (-not $apk) { Write-Host "FAILED: no .apk under $resolved" -ForegroundColor Red; exit 1 }
    } else {
        Write-Host "FAILED: path not found: $resolved" -ForegroundColor Red; exit 1
    }
} else {
    # 标准产物目录：Builds\（Builder 规范输出）、Build\、项目根（非递归，避开 Library/Temp 中间产物）
    $candidates = @()
    foreach ($d in @("Builds", "Build")) { $c = Find-LatestApk (Join-Path $root $d); if ($c) { $candidates += $c } }
    $rootApk = Get-ChildItem $root -Filter *.apk -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($rootApk) { $candidates += $rootApk }
    if ($candidates.Count -eq 0) {
        Write-Host "FAILED: no APK in Builds\, Build\, or project root — run Tools\build-apk.ps1 first" -ForegroundColor Red
        exit 1
    }
    $apk = $candidates | Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

$relApk = $apk.FullName.Replace("$root\", "")
Write-Host ("Latest APK: {0}  ({1:N1} MB, built {2})" -f $relApk, ($apk.Length / 1MB), $apk.LastWriteTime) -ForegroundColor Cyan

# 3) 设备检查（多设备需 -Serial）
$deviceLines = & $adb devices | Select-String "\tdevice$"
if (-not $deviceLines) { Write-Host "FAILED: no adb device connected/authorized" -ForegroundColor Red; exit 1 }
$serials = @($deviceLines | ForEach-Object { ($_.Line.Trim() -split "\s+")[0] })
$adbTarget = @()
if ($Serial -ne "") {
    if ($serials -notcontains $Serial) { Write-Host "FAILED: device '$Serial' not found. Connected: $($serials -join ', ')" -ForegroundColor Red; exit 1 }
    $adbTarget = @("-s", $Serial)
} elseif ($serials.Count -gt 1) {
    Write-Host "FAILED: multiple devices — pass -Serial <one of: $($serials -join ', ')>" -ForegroundColor Red; exit 1
}

# 4) 安装（-r 覆盖升级，-d 允许版本号回退）
Write-Host "Installing to $(if ($Serial) { $Serial } else { $serials[0] }) ..."
& $adb @adbTarget install -r -d $apk.FullName
if ($LASTEXITCODE -ne 0) { Write-Host "FAILED: adb install (exit $LASTEXITCODE)" -ForegroundColor Red; exit 1 }
Write-Host "INSTALLED: $relApk" -ForegroundColor Green

# 5) 可选启动
if ($Launch) {
    $activity = "$PackageName/com.unity3d.player.UnityPlayerActivity"
    & $adb @adbTarget shell am force-stop $PackageName
    & $adb @adbTarget shell am start -n $activity | Out-Null
    Write-Host "Launched $PackageName" -ForegroundColor Green
}
exit 0
