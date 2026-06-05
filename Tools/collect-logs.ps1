# collect-logs.ps1 — 抓取 PICO 真机运行日志（M3 真机层）
# 用法：powershell -ExecutionPolicy Bypass -File Tools\collect-logs.ps1 [-Seconds 30] [-Output Logs\device-run.log]

param(
    [int]$Seconds = 30,
    [string]$Output = "Logs\device-run.log"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$outPath = Join-Path $root $Output

$adb = "adb"
if (-not (Get-Command adb -ErrorAction SilentlyContinue)) {
    $unityAdb = "D:\Unity\UnityEditor\Unity 2022.3.16f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
    if (Test-Path $unityAdb) { $adb = $unityAdb }
    else { Write-Host "FAILED: adb not found" -ForegroundColor Red; exit 1 }
}

Write-Host "Capturing logcat for $Seconds seconds (Unity/AndroidRuntime/CRASH)..."
$job = Start-Job -ScriptBlock {
    param($adb, $outPath)
    & $adb logcat -v time -s Unity:V AndroidRuntime:E CRASH:E DEBUG:V *:S > $outPath
} -ArgumentList $adb, $outPath
Start-Sleep -Seconds $Seconds
Stop-Job $job; Remove-Job $job -Force

$errors = Select-String -Path $outPath -Pattern "Exception|Error|FATAL" -ErrorAction SilentlyContinue
Write-Host "Saved to $outPath ($((Get-Item $outPath).Length) bytes)"
if ($errors) {
    Write-Host "=== $($errors.Count) error lines detected ===" -ForegroundColor Yellow
    $errors | Select-Object -First 10 | ForEach-Object { Write-Host $_.Line }
    exit 1
}
Write-Host "No errors detected in capture window." -ForegroundColor Green
exit 0
