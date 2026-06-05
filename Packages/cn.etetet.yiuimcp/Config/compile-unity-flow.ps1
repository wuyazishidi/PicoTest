param(
    [bool]$Force = $False,
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
Write-Host "Unity 智能编译 (Force: $Force)"
Write-Host "========================================"
Write-Host "Unity MCP 端口: $UNITY_MCP_PORT"
Write-Host "UTO HTTP 端口: $UTO_HTTP_PORT"
Write-Host ""

# 清理旧的 UTO 进程
try {
    $conn = Get-NetTCPConnection -LocalPort $UTO_HTTP_PORT -ErrorAction SilentlyContinue
    if ($conn) {
        Write-Host "清理旧的 UTO 进程..." -ForegroundColor Yellow
        Stop-Process -Id $conn.OwningProcess -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
    }
} catch {}

# 启动 UTO（无需传递端口参数，自动从 .port 文件读取）
Write-Host "启动 UTO HTTP Server..."
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = "node"
$psi.Arguments = "build/index.js --http"
$psi.WorkingDirectory = $UTO_PATH
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$utoProcess = [System.Diagnostics.Process]::Start($psi)

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

$tools = @(
    @{ name = "StopPlayMode"; arguments = @{} },
    @{ name = "TriggerCompile"; arguments = @{ Force = $Force } },
    @{ name = "GetCompileResult"; arguments = @{} }
)

$body = @{ tools = $tools } | ConvertTo-Json -Depth 10 -Compress

Write-Host "执行编译流程..."
Write-Host "  1. 退出 PlayMode（如果在运行）"
Write-Host "  2. 触发编译"
Write-Host "  3. 获取编译结果"
Write-Host ""

try {
    $response = Invoke-RestMethod -Uri "http://localhost:$UTO_HTTP_PORT/batch" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 600
} catch {
    Write-Host "调用失败: $($_.Exception.Message)" -ForegroundColor Red
    # 清理 UTO 进程
    if ($utoProcess -and -not $utoProcess.HasExited) {
        Write-Host "关闭 UTO 进程..." -ForegroundColor Yellow
        try {
            $utoProcess.Kill()
            $utoProcess.WaitForExit(3000)
        } catch {}
    }
    if (-not $NoWait) {
        Write-Host "按任意键退出..."
        [Console]::ReadKey($true) | Out-Null
        Stop-Process -Id $PID
    }
    exit 1
}

Write-Host "========================================"
if ($response.success) {
    Write-Host "编译流程完成!" -ForegroundColor Green
    Write-Host "总耗时: $($response.totalDurationSeconds) 秒" -ForegroundColor Cyan
    Write-Host ""
    foreach ($result in $response.results) {
        $stepDurationText = ""
        if ($null -ne $result.duration -and "$($result.duration)" -ne "") {
            $stepDurationText = " ($($result.duration) ms)"
        }

        Write-Host "✓ $($result.tool)$stepDurationText" -ForegroundColor Green
        if ($result.result) {
            Write-Host "  $($result.result)" -ForegroundColor Gray
        }
    }
} else {
    Write-Host "编译流程失败!" -ForegroundColor Red
    Write-Host "总耗时: $($response.totalDurationSeconds) 秒" -ForegroundColor Cyan
    Write-Host "失败位置: 第 $($response.failedAt + 1) 个工具"
    Write-Host "错误: $($response.error)"
    Write-Host ""
    foreach ($result in $response.results) {
        $stepDurationText = ""
        if ($null -ne $result.duration -and "$($result.duration)" -ne "") {
            $stepDurationText = " ($($result.duration) ms)"
        }

        if ($result.success) {
            Write-Host "✓ $($result.tool)$stepDurationText" -ForegroundColor Green
        } else {
            Write-Host "✗ $($result.tool)$stepDurationText" -ForegroundColor Red
            if ($result.error) {
                Write-Host "  错误: $($result.error)" -ForegroundColor Red
            }
        }
    }
}
Write-Host "========================================"

# 清理 UTO 进程
if ($utoProcess -and -not $utoProcess.HasExited) {
    Write-Host "关闭 UTO 进程..." -ForegroundColor Yellow
    try {
        $utoProcess.Kill()
        $utoProcess.WaitForExit(3000)
    } catch {}
}

if (-not $NoWait) {
    Write-Host "按任意键退出..."
    [Console]::ReadKey($true) | Out-Null
    Stop-Process -Id $PID
}
exit 0

