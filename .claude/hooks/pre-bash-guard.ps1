# PreToolUse(Bash) 守卫：危险命令拦截 + commit 门禁（宪法第 3/10 条）
# 退出码 2 = 阻止该工具调用（stderr 信息会反馈给 AI）

$ErrorActionPreference = "SilentlyContinue"
$raw = [Console]::In.ReadToEnd()
try { $payload = $raw | ConvertFrom-Json } catch { exit 0 }
$cmd = "$($payload.tool_input.command)"
if ([string]::IsNullOrWhiteSpace($cmd)) { exit 0 }

# --- 危险命令黑名单 ---
$dangerPatterns = @(
    'git\s+push\s+.*(--force|-f)\b',
    'git\s+reset\s+--hard',
    'git\s+clean\s+-[a-z]*f',
    'rm\s+-rf\s+/',
    'Remove-Item\s+.*-Recurse.*(ProjectSettings|Packages|Assets\\Main)',
    'git\s+checkout\s+--\s+\.'
)
foreach ($p in $dangerPatterns) {
    if ($cmd -match $p) {
        [Console]::Error.WriteLine("BLOCKED by constitution #10: command matches dangerous pattern '$p'. Ask the user explicitly if this is truly required.")
        exit 2
    }
}

# --- commit 门禁：必须有 tests-green 标记 ---
if ($cmd -match 'git\s+commit') {
    $gate = Join-Path (Get-Location) ".gates\tests-green"
    if (-not (Test-Path $gate)) {
        [Console]::Error.WriteLine("BLOCKED by constitution #3: .gates/tests-green missing. Run 'powershell -ExecutionPolicy Bypass -File Tools\run-tests.ps1' and get ALL GREEN before committing. (Bootstrap exception: if the test toolchain itself is not yet functional, ask the user for a one-time override.)")
        exit 2
    }
}
exit 0
