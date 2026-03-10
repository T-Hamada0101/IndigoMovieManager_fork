param(
    [string]$SchemaBuildDir = "",
    [string]$DbPath = "",
    [string]$InputFolder = "",
    [string]$ThumbFolder = "",
    [string]$BookmarkFolder = "",
    [switch]$Recreate,
    [switch]$ResetArtifacts
)

$ErrorActionPreference = "Stop"
$helper = Join-Path $PSScriptRoot "prepare_upstream_current_bench_db.ps1"

# 既定値はスクリプト相対とプレースホルダ前提に寄せ、ローカル固有パスを持たない。
if ([string]::IsNullOrWhiteSpace($SchemaBuildDir)) {
    $SchemaBuildDir = "<schema-build-dir>"
}
if ([string]::IsNullOrWhiteSpace($DbPath)) {
    $DbPath = Join-Path $PSScriptRoot "..\..\bench\709a137_hdd_bench.wb"
}
if ([string]::IsNullOrWhiteSpace($InputFolder)) {
    $InputFolder = "<input-video-root>"
}
if ([string]::IsNullOrWhiteSpace($ThumbFolder)) {
    $ThumbFolder = Join-Path $PSScriptRoot "..\..\bench_output\709a137_hdd\Thumb"
}
if ([string]::IsNullOrWhiteSpace($BookmarkFolder)) {
    $BookmarkFolder = Join-Path $PSScriptRoot "..\..\bench_output\709a137_hdd\Bookmark"
}

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
