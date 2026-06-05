$path = "D:\Unity\Project\IGG\ie\trunk\Client\UnityProject\Packages\cn.etetet.yiuimcp\UTO\node_modules"
$devPatterns = @("typescript", "@types", ".bin")
$devSize = 0
$prodSize = 0

Get-ChildItem $path -Directory | ForEach-Object {
    $isDev = $false
    foreach ($p in $devPatterns) {
        if ($_.Name -like "*$p*") { $isDev = $true; break }
    }
    $size = (Get-ChildItem $_.FullName -Recurse -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
    if ($isDev) {
        $devSize += $size
        Write-Host "DEV: $($_.Name) = $([math]::Round($size/1MB, 2)) MB"
    } else {
        $prodSize += $size
    }
}
Write-Host ""
Write-Host "Dev dependencies: $([math]::Round($devSize/1MB, 2)) MB"
Write-Host "Prod dependencies: $([math]::Round($prodSize/1MB, 2)) MB"
