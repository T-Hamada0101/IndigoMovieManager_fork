param(
    [string]$SchemaBuildDir = "C:\Users\na6ce\source\repos\IndigoMovieManager\bin\x64\Debug\net8.0-windows",
    [string]$DbPath = "C:\Users\na6ce\source\repos\IndigoMovieManager_fork\bench\709a137_hdd_bench.wb",
    [string]$InputFolder = "D:\BentchItem_HDD",
    [string]$ThumbFolder = "C:\Users\na6ce\source\repos\IndigoMovieManager_fork\bench_output\709a137_hdd\Thumb",
    [string]$BookmarkFolder = "C:\Users\na6ce\source\repos\IndigoMovieManager_fork\bench_output\709a137_hdd\Bookmark",
    [switch]$Recreate,
    [switch]$ResetArtifacts
)

$ErrorActionPreference = "Stop"
$helper = "C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\Test\prepare_upstream_current_bench_db.ps1"

if ($ResetArtifacts) {
    $logPath = Join-Path $env:LOCALAPPDATA "IndigoMovieManager_bench_709a137\logs\bench-runtime.log"
    if (Test-Path -LiteralPath $logPath -PathType Leaf) {
        Remove-Item -LiteralPath $logPath -Force
    }
}

& $helper `
    -UpstreamBuildDir $SchemaBuildDir `
    -DbPath $DbPath `
    -InputFolder $InputFolder `
    -ThumbFolder $ThumbFolder `
    -BookmarkFolder $BookmarkFolder `
    -Recreate:$Recreate `
    -ResetArtifacts:$ResetArtifacts

$benchLogPath = Join-Path $env:LOCALAPPDATA "IndigoMovieManager_bench_709a137\logs\bench-runtime.log"
Write-Host "709a137ログ : $benchLogPath"