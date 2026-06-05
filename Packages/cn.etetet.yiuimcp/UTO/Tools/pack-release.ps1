<#
.SYNOPSIS
    Pack cn.etetet.yiuimcp as minimal release (DLL form)
.PARAMETER OutputPath
    Output directory, default is Desktop
#>
param(
    [string]$OutputPath = "$env:USERPROFILE\Desktop"
)

$ErrorActionPreference = "Stop"

$UtoRoot = Split-Path -Parent $PSScriptRoot
$PackageRoot = Split-Path -Parent $UtoRoot
$UnityProject = Split-Path -Parent (Split-Path -Parent $PackageRoot)
$DllSource = Join-Path $UnityProject "Library\ScriptAssemblies"
$ReleaseName = "cn.etetet.yiuimcp"
$ReleaseDir = Join-Path $OutputPath $ReleaseName
$ZipPath = Join-Path $OutputPath "$ReleaseName.zip"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  YIUI MCP Release Packer" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$EditorDll = Join-Path $DllSource "ET.YIUI.MCP.Editor.dll"
if (-not (Test-Path $EditorDll)) {
    Write-Host "[ERROR] DLL not found: $EditorDll" -ForegroundColor Red
    Write-Host "Please compile the project in Unity first" -ForegroundColor Yellow
    exit 1
}

if (Test-Path $ReleaseDir) {
    Write-Host "[CLEAN] Removing old release dir..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $ReleaseDir
}
if (Test-Path $ZipPath) {
    Remove-Item -Force $ZipPath
}

Write-Host "[CREATE] Directory structure..." -ForegroundColor Green
New-Item -ItemType Directory -Path $ReleaseDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $ReleaseDir "Editor") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $ReleaseDir "Config") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $ReleaseDir "UTO") -Force | Out-Null

Write-Host "[COPY] package.json..." -ForegroundColor Green
Copy-Item (Join-Path $PackageRoot "package.json") $ReleaseDir

Write-Host "[COPY] Editor DLL..." -ForegroundColor Green
Copy-Item $EditorDll (Join-Path $ReleaseDir "Editor")

Write-Host "[COPY] Config scripts..." -ForegroundColor Green
$ConfigSource = Join-Path $PackageRoot "Config"
$ConfigDest = Join-Path $ReleaseDir "Config"
Get-ChildItem -Path $ConfigSource -Filter "*.ps1" | ForEach-Object {
    Copy-Item $_.FullName $ConfigDest
}

Write-Host "[COPY] UTO runtime..." -ForegroundColor Green
$UtoDest = Join-Path $ReleaseDir "UTO"

Copy-Item -Recurse (Join-Path $UtoRoot "build") $UtoDest
Copy-Item (Join-Path $UtoRoot "package.json") $UtoDest

Write-Host "[COPY] UTO node_modules (prod only)..." -ForegroundColor Green
$NodeModulesDest = Join-Path $UtoDest "node_modules"
New-Item -ItemType Directory -Path $NodeModulesDest -Force | Out-Null

$devPatterns = @("typescript", "@types", ".bin")
Get-ChildItem (Join-Path $UtoRoot "node_modules") -Directory | ForEach-Object {
    $isDev = $false
    foreach ($p in $devPatterns) {
        if ($_.Name -like "*$p*") { $isDev = $true; break }
    }
    if (-not $isDev) {
        Copy-Item -Recurse $_.FullName $NodeModulesDest
    }
}

$ReadmeContent = "# cn.etetet.yiuimcp`n`nUnity MCP Server`n`n## Requirements`n- Unity 2022.3+`n- Odin Inspector`n`n## Install`nCopy this folder to Packages directory"
Set-Content -Path (Join-Path $ReleaseDir "README.md") -Value $ReadmeContent -Encoding UTF8

Write-Host ""
Write-Host "[STAT] Calculating size..." -ForegroundColor Green
$TotalSize = 0
Get-ChildItem -Recurse $ReleaseDir | Where-Object { -not $_.PSIsContainer } | ForEach-Object {
    $TotalSize += $_.Length
}
$SizeMB = [math]::Round($TotalSize / 1MB, 2)

Write-Host "[PACK] Creating ZIP..." -ForegroundColor Green
Compress-Archive -Path $ReleaseDir -DestinationPath $ZipPath -Force

$ZipSize = [math]::Round((Get-Item $ZipPath).Length / 1MB, 2)

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Pack Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Release Dir: $ReleaseDir" -ForegroundColor White
Write-Host "ZIP File:    $ZipPath" -ForegroundColor White
Write-Host ""
Write-Host "Uncompressed: $SizeMB MB" -ForegroundColor Yellow
Write-Host "ZIP Size:     $ZipSize MB" -ForegroundColor Yellow
Write-Host ""
Write-Host "[NOTE] Receiver needs Odin Inspector installed" -ForegroundColor Magenta
