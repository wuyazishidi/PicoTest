# run-tests.ps1 — AI 自主测试循环入口
# 直连 Unity 内 YIUIMCP JSON-RPC（跳过 UTO，轮询期间不反复拉起 node）。
# 注意：必须用 127.0.0.1（YIUIMCP 只绑 IPv4；localhost 可能解析为 ::1 导致连接失败）
# 用法：
#   powershell -ExecutionPolicy Bypass -File Tools\run-tests.ps1                # EditMode + PlayMode
#   powershell -ExecutionPolicy Bypass -File Tools\run-tests.ps1 -Mode EditMode
# 退出码：0 = 全绿（并写入 .gates\tests-green），1 = 失败/超时。
# 前置条件：Unity 编辑器已打开本项目（YIUIMCP HTTP 服务随编辑器启动）。

param(
    [ValidateSet("EditMode", "PlayMode", "All")]
    [string]$Mode = "All",
    [int]$TimeoutSec = 600
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$portFile = Join-Path $root "Packages\cn.etetet.yiuimcp\UTO\.port"
$port = 3212
if (Test-Path $portFile) { $port = [int](Get-Content $portFile -Raw).Trim() }
$rpcUri = "http://127.0.0.1:$port/rpc"
$healthUri = "http://127.0.0.1:$port/health"

function Invoke-UnityRpc([string]$method, $params) {
    $body = @{ jsonrpc = "2.0"; method = $method; params = $params; id = 1 } | ConvertTo-Json -Depth 5 -Compress
    $resp = Invoke-RestMethod -Uri $rpcUri -Method Post -Body $body -ContentType "application/json" -TimeoutSec 30
    return $resp.result
}

# 1) Unity 在线检查
try {
    $health = Invoke-RestMethod -Uri $healthUri -TimeoutSec 3
    Write-Host "Unity online (pid=$($health.pid), serverId=$($health.serverId))" -ForegroundColor Green
} catch {
    Write-Host "FAILED: Unity editor not reachable at $healthUri - open the project in Unity first." -ForegroundColor Red
    exit 1
}

$modes = if ($Mode -eq "All") { @("EditMode", "PlayMode") } else { @($Mode) }
$summary = @()

foreach ($m in $modes) {
    Write-Host ""
    Write-Host "=== Running $m tests ===" -ForegroundColor Cyan

    # 2) 删除旧结果文件，防止把上一轮的 finished 误读为本轮结果
    $resultFile = Join-Path $root "Logs\TestResults\latest.json"
    Remove-Item $resultFile -Force -ErrorAction SilentlyContinue

    # 3) 触发测试。PlayMode 会立即进 Play Mode + Domain Reload，
    #    HTTP 响应通道被杀是预期行为 —— 触发异常不视为失败，以结果文件为准
    try {
        $trigger = Invoke-UnityRpc "RunTests" @{ testMode = $m }
        if ($trigger -and -not $trigger.success) {
            Write-Host "FAILED to trigger ${m}: $($trigger.message)" -ForegroundColor Red
            exit 1
        }
        if ($trigger) { Write-Host $trigger.message }
    } catch {
        Write-Host "Trigger channel reset (expected for PlayMode) - polling result file..." -ForegroundColor Yellow
    }

    # 4) 轮询本地结果文件（由 [InitializeOnLoad] 回调写入，跨 Domain Reload 存活）
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $resultJson = $null
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 3
        if (Test-Path $resultFile) {
            $content = Get-Content $resultFile -Raw -ErrorAction SilentlyContinue
            if ($content -match '"status": "finished"') {
                $resultJson = $content
                break
            }
        }
    }

    if ($null -eq $resultJson) {
        Write-Host "TIMEOUT: $m tests did not finish within $TimeoutSec s" -ForegroundColor Red
        exit 1
    }

    $result = $resultJson | ConvertFrom-Json
    Write-Host ("{0}: passed={1} failed={2} skipped={3} duration={4}s" -f `
        $m, $result.passed, $result.failed, $result.skipped, [math]::Round($result.durationSeconds, 1))

    if ($result.failed -gt 0) {
        Write-Host ""
        Write-Host "=== FAILURES ($m) ===" -ForegroundColor Red
        foreach ($f in $result.failures) {
            Write-Host "FAIL: $($f.name)" -ForegroundColor Red
            Write-Host "  $($f.message)"
        }
        exit 1
    }

    $summary += "{0}: {1} passed" -f $m, $result.passed
}

# 4) 全绿后写门禁标记（hooks 依赖此文件放行 git commit）
$gatesDir = Join-Path $root ".gates"
New-Item -ItemType Directory -Force -Path $gatesDir | Out-Null
$head = ""
try { $head = (git -C $root rev-parse HEAD 2>$null) } catch {}
@{
    timestamp = (Get-Date).ToString("o")
    commit    = "$head"
    modes     = $modes -join "+"
    summary   = $summary -join "; "
} | ConvertTo-Json | Out-File (Join-Path $gatesDir "tests-green") -Encoding utf8

Write-Host ""
Write-Host "ALL GREEN - gate marker written to .gates\tests-green" -ForegroundColor Green
exit 0
