# run-e2e-local.ps1 — 本地全链路验收（前置：Unity 编辑器开着 + Server/npm install 已执行）
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$serverDir = Join-Path $root "Server"
$dataDir = Join-Path $env:TEMP ("ingest-e2e-" + (Get-Date -Format "HHmmss"))

# 1) 起 Node 接收服务
$env:INGEST_DATA_DIR = $dataDir; $env:INGEST_PORT = "8077"
$node = Start-Process node -ArgumentList "server.js" -WorkingDirectory $serverDir -PassThru -WindowStyle Hidden
try {
    $up = $false
    foreach ($i in 1..20) {
        Start-Sleep -Milliseconds 500
        try { if ((Invoke-RestMethod "http://127.0.0.1:8077/health" -TimeoutSec 2).status -eq "ok") { $up = $true; break } } catch {}
    }
    if (-not $up) { Write-Host "FAILED: ingest server didn't start" -ForegroundColor Red; exit 1 }

    # 2) RPC 触发 Editor 内 e2e
    function Rpc([string]$method, $params) {
        $body = @{ jsonrpc = "2.0"; method = $method; params = $params; id = 1 } | ConvertTo-Json -Depth 5 -Compress
        (Invoke-RestMethod "http://127.0.0.1:3212/rpc" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 120).result
    }
    $exec = Rpc "ExecuteMenu" @{ menuPath = "PicoTest/E2E/Run Local Pipeline"; ChangeTimeoutMs = $true; timeoutMs = 110000 }
    if (-not $exec.success) { Write-Host "FAILED to execute menu: $($exec.message)" -ForegroundColor Red; exit 1 }

    # 3) 断言 Console 有 PASS（keyword 为 AssertConsoleContains 的实际字段名）
    $assert = Rpc "AssertConsoleContains" @{ keyword = "[E2E] PASS" }
    if ($assert.success) {
        # 4) 服务端侧验证：存在 .complete 标记
        $complete = Get-ChildItem $dataDir -Recurse -Filter ".complete" -ErrorAction SilentlyContinue
        if ($complete) { Write-Host "E2E PASS (client + server verified)" -ForegroundColor Green; exit 0 }
        Write-Host "FAILED: client passed but server has no .complete marker" -ForegroundColor Red; exit 1
    }
    Write-Host "FAILED: $($assert.message)" -ForegroundColor Red; exit 1
} finally {
    Stop-Process -Id $node.Id -Force -ErrorAction SilentlyContinue
    Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue
}
