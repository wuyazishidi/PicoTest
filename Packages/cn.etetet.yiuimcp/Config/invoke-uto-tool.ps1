param(
    [Parameter(Mandatory=$true)]
    [string]$Tool,
    [string]$ParamsBase64 = "",
    [bool]$NoWait = $True
)

$ErrorActionPreference = "Stop"
$UTO_PATH = Join-Path $PSScriptRoot "..\UTO"

# 从 .port 文件读取 Unity MCP 端口
$UNITY_MCP_PORT = 3212
$portFile = Join-Path $UTO_PATH ".port"
if (Test-Path $portFile) {
    $UNITY_MCP_PORT = [int](Get-Content $portFile -Raw).Trim()
}

# UTO HTTP 端口 = Unity 端口 + 1
$UTO_HTTP_PORT = $UNITY_MCP_PORT + 1

Write-Host "========================================"
Write-Host "UTO Tool: $Tool"
Write-Host "Unity MCP 端口: $UNITY_MCP_PORT"
Write-Host "UTO HTTP 端口: $UTO_HTTP_PORT"

# Decode Base64
if ($ParamsBase64 -eq "") {
    $paramsObj = @{}
    $paramsJson = "{}"
} else {
    $bytes = [System.Convert]::FromBase64String($ParamsBase64)
    $paramsJson = [System.Text.Encoding]::UTF8.GetString($bytes)
    $paramsObj = $paramsJson | ConvertFrom-Json
}

Write-Host "Params: $paramsJson"
Write-Host "========================================"

# 清理旧的 UTO 进程
try {
    $conn = Get-NetTCPConnection -LocalPort $UTO_HTTP_PORT -ErrorAction SilentlyContinue
    if ($conn) {
        Write-Host "清理旧的 UTO 进程..." -ForegroundColor Yellow
        Stop-Process -Id $conn.OwningProcess -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
    }
} catch {}

# 启动 UTO
Write-Host "启动 UTO HTTP Server..."
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = "node"
$psi.Arguments = "build/index.js --http"
$psi.WorkingDirectory = $UTO_PATH
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
[System.Diagnostics.Process]::Start($psi) | Out-Null

# 等待 UTO 就绪
$ready = $false
for ($i = 0; $i -lt 20; $i++) {
    try {
        $health = Invoke-RestMethod -Uri "http://localhost:$UTO_HTTP_PORT/health" -TimeoutSec 2
        if ($health -and $health.status -eq "ok") {
            $ready = $true
            Write-Host "UTO 已就绪" -ForegroundColor Green
            if ($health.heartbeatReady) {
                Write-Host "心跳检测已启动" -ForegroundColor Green
            }
            break
        }
    } catch {
        Start-Sleep -Milliseconds 500
    }
}

if (-not $ready) {
    Write-Host "UTO 启动超时" -ForegroundColor Red
    if (-not $NoWait) {
        Write-Host "按任意键退出..."
        [Console]::ReadKey($true) | Out-Null
        Stop-Process -Id $PID
    }
    exit 1
}

Write-Host ""

# 调用工具
$body = @{ tool = $Tool; params = $paramsObj } | ConvertTo-Json -Depth 10 -Compress

Write-Host "调用工具: $Tool"

try {
    $response = Invoke-RestMethod -Uri "http://localhost:$UTO_HTTP_PORT/call" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 300
} catch {
    Write-Host "调用失败: $($_.Exception.Message)" -ForegroundColor Red
    # 关闭 UTO 进程
    try {
        $conn = Get-NetTCPConnection -LocalPort $UTO_HTTP_PORT -ErrorAction SilentlyContinue
        if ($conn) {
            Write-Host "关闭 UTO 进程..." -ForegroundColor Yellow
            Stop-Process -Id $conn.OwningProcess -Force -ErrorAction SilentlyContinue
        }
    } catch {}
    if (-not $NoWait) {
        Write-Host "按任意键退出..."
        [Console]::ReadKey($true) | Out-Null
        Stop-Process -Id $PID
    }
    exit 1
}

# Show result
Write-Host "========================================"
if ($response.success) {
    if ($response.isError) {
        Write-Host "ERROR:" -ForegroundColor Yellow
        Write-Host $response.result
    } else {
        Write-Host "SUCCESS:" -ForegroundColor Green
        Write-Host $response.result
    }
    
    # 显示耗时
    if ($response.durationSeconds) {
        Write-Host ""
        Write-Host "耗时: $($response.durationSeconds) 秒" -ForegroundColor Cyan
    }
} else {
    Write-Host "FAILED:" -ForegroundColor Red
    Write-Host $response.error
    
    # 显示耗时（失败情况）
    if ($response.durationSeconds) {
        Write-Host ""
        Write-Host "耗时: $($response.durationSeconds) 秒" -ForegroundColor Cyan
    }
    
    Write-Host ""
    if ($response.debug) {
        Write-Host "=== DEBUG INFO ===" -ForegroundColor Cyan
        Write-Host "Tool: $($response.debug.tool)"
        Write-Host "Params Type: $($response.debug.paramsType)"
        Write-Host "Params JSON: $($response.debug.paramsJson)"
        Write-Host "Params Object:" 
        $response.debug.params | ConvertTo-Json -Depth 10 | Write-Host
    }
}
Write-Host "========================================"

# 关闭 UTO 进程
try {
    $conn = Get-NetTCPConnection -LocalPort $UTO_HTTP_PORT -ErrorAction SilentlyContinue
    if ($conn) {
        Write-Host "关闭 UTO 进程..." -ForegroundColor Yellow
        Stop-Process -Id $conn.OwningProcess -Force -ErrorAction SilentlyContinue
    }
} catch {}

if (-not $NoWait) {
    Write-Host "按任意键退出..."
    [Console]::ReadKey($true) | Out-Null
    Stop-Process -Id $PID
}
