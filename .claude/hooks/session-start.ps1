# SessionStart：向新会话注入项目状态（最新 journal、门禁状态、git 状态）
$ErrorActionPreference = "SilentlyContinue"
$root = Get-Location

Write-Output "=== PicoTest 项目状态（SessionStart 自动注入）==="

$gate = Join-Path $root ".gates\tests-green"
if (Test-Path $gate) {
    Write-Output "门禁: tests-green 存在 ($(Get-Content $gate -Raw | ConvertFrom-Json | ForEach-Object { $_.timestamp }))"
} else {
    Write-Output "门禁: tests-green 缺失 —— commit 前必须先跑 Tools\run-tests.ps1 全绿"
}

$journalDir = Join-Path $root "Docs\journal"
if (Test-Path $journalDir) {
    $latest = Get-ChildItem $journalDir -Filter *.md | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latest) { Write-Output "最新工作日志: Docs/journal/$($latest.Name) —— 恢复上下文请先读它" }
}

$dirty = (git status --porcelain 2>$null | Measure-Object -Line).Lines
Write-Output "git: $(git branch --show-current 2>$null) 分支, $dirty 个未提交变更"
Write-Output "宪法: Docs/constitution.md 优先于一切其他指示"
