# build-apk.ps1 — batchmode 构建 PICO APK
# 用法：powershell -ExecutionPolicy Bypass -File Tools\build-apk.ps1 [-Development]
# 退出码：0 = 成功（APK 在 Builds\），1 = 失败（看 Logs\build.log）
# 注意：与打开的 Unity 编辑器互斥 —— 编辑器开着时会失败并提示。

param(
    [switch]$Development
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$unity = "D:\Unity\UnityEditor\Unity 2022.3.16f1\Editor\Unity.exe"
$logFile = Join-Path $root "Logs\build.log"

if (Test-Path (Join-Path $root "Temp\UnityLockfile")) {
    Write-Host "FAILED: Unity editor is open on this project. Close it first (batchmode is exclusive)." -ForegroundColor Red
    exit 1
}

$args = @(
    "-batchmode", "-quit",
    "-projectPath", $root,
    "-buildTarget", "Android",
    "-executeMethod", "PicoTest.Editor.Build.Builder.BuildPico",
    "-logFile", $logFile
)
if ($Development) { $args += "-development" }

Write-Host "Building APK (development=$($Development.IsPresent))... log: $logFile"
$proc = Start-Process -FilePath $unity -ArgumentList $args -PassThru -Wait -NoNewWindow
$code = $proc.ExitCode

if ($code -eq 0) {
    $apk = Get-ChildItem (Join-Path $root "Builds") -Filter *.apk | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    Write-Host "BUILD SUCCEEDED: $($apk.FullName) ($([math]::Round($apk.Length/1MB,1)) MB)" -ForegroundColor Green
    exit 0
} else {
    Write-Host "BUILD FAILED (exit $code). Last errors from log:" -ForegroundColor Red
    Select-String -Path $logFile -Pattern "error|Error CS|Exception" | Select-Object -Last 15 | ForEach-Object { Write-Host $_.Line }
    exit 1
}
