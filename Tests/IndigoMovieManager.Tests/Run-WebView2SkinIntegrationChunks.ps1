<#
.SYNOPSIS
実 WebView2 を使う MainWindow skin integration test を安全な chunk に分けて実行する。

.EXAMPLE
pwsh -NoProfile -ExecutionPolicy Bypass -File Tests\IndigoMovieManager.Tests\Run-WebView2SkinIntegrationChunks.ps1 -NoBuild

.EXAMPLE
pwsh -NoProfile -ExecutionPolicy Bypass -File Tests\IndigoMovieManager.Tests\Run-WebView2SkinIntegrationChunks.ps1 -NoBuild -Chunk HostBasics,TutorialCallback

.EXAMPLE
pwsh -NoProfile -ExecutionPolicy Bypass -File Tests\IndigoMovieManager.Tests\Run-WebView2SkinIntegrationChunks.ps1 -ListChunks
#>
param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [switch]$NoBuild,
    [switch]$ListOnly,
    [switch]$ListChunks,
    [string[]]$Chunk = @()
)

$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "IndigoMovieManager.Tests.csproj"

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

# 実WebView2 fixture は一括実行時に testhost shutdown 側で落ちることがあるため、
# 既存の日本語テスト名のまとまりで小分けにして、失敗箇所を追いやすくする。
$chunks = [ordered]@{
    "HostBasics" = 'TestCategory=MainWindowWebViewSkin&Name~外部skin|TestCategory=MainWindowWebViewSkin&Name~host|TestCategory=MainWindowWebViewSkin&Name~fallback|TestCategory=MainWindowWebViewSkin&Name~MinimalChrome'
    "TutorialCallback" = 'TestCategory=MainWindowWebViewSkin&Name~TutorialCallbackGrid'
    "DefaultList" = 'TestCategory=MainWindowWebViewSkin&Name~WhiteBrowserDefault'
    "SimpleGrid" = 'TestCategory=MainWindowWebViewSkin&Name~SimpleGridWB'
    "TagInputSmoke" = 'TestCategory=MainWindowWebViewSkin&Name=TagInputRelationをMainWindow経由でchangeSkinしても入力と候補表示を次skinへ持ち越さない|TestCategory=MainWindowWebViewSkin&Name=TagInputRelationをMainWindow経由でchangeSkinしてもtree_footerを次skinへ持ち越さない|TestCategory=MainWindowWebViewSkin&Name=TagInputRelationをMainWindow経由でchangeSkin失敗しても入力と候補表示を現在skinへ維持できる|TestCategory=MainWindowWebViewSkin&Name=TagInputRelationをMainWindow経由でGetから候補拡張まで進められる|TestCategory=MainWindowWebViewSkin&Name=TagInputRelationをMainWindow経由でGet後にchangeSkinしてもtree_footerを次skinへ持ち越さない|TestCategory=MainWindowWebViewSkin&Name=TagInputRelationをMainWindow経由でGet後にchangeSkin失敗しても候補拡張状態を維持できる'
    "TreeSmoke" = 'TestCategory=MainWindowWebViewSkin&Name=umiFindTreeEveをMainWindow経由でonRegistedFile後にRefreshしてtreeへ反映できる|TestCategory=MainWindowWebViewSkin&Name=umiFindTreeEveをMainWindow経由でonModifyTags後にRefreshするとtag_treeへ反映できる|TestCategory=MainWindowWebViewSkin&Name=umiFindTreeEveをMainWindow経由でchangeSkinしてもtree_footerを次skinへ持ち越さない'
    "BuildOutputSkins" = 'TestCategory=MainWindowWebViewSkin&Name~Search_table|TestCategory=MainWindowWebViewSkin&Name~Chappy|TestCategory=MainWindowWebViewSkin&Name~Alpha2|TestCategory=MainWindowWebViewSkin&Name~DefaultSmallWB|TestCategory=MainWindowWebViewSkin&Name~build出力skin'
}

if ($ListChunks) {
    Write-Host "利用可能な WebView2 chunk:"
    foreach ($name in $chunks.Keys) {
        Write-Host "  $name"
    }
    Write-Host ""
    Write-Host "例: pwsh -NoProfile -ExecutionPolicy Bypass -File Tests\IndigoMovieManager.Tests\Run-WebView2SkinIntegrationChunks.ps1 -NoBuild -Chunk HostBasics,TutorialCallback"
    return
}

$selectedChunks = if ($Chunk.Count -gt 0) { $Chunk } else { $chunks.Keys }
$selectedChunks = $selectedChunks |
    ForEach-Object { $_ -split "," } |
    ForEach-Object { $_.Trim() } |
    Where-Object { $_.Length -gt 0 }

foreach ($name in $selectedChunks) {
    if (-not $chunks.Contains($name)) {
        throw "未知の chunk です: $name。利用可能: $($chunks.Keys -join ', ')"
    }
}

if (-not $NoBuild) {
    Invoke-DotNet @(
        "build",
        $projectPath,
        "-c",
        $Configuration,
        "-p:Platform=$Platform",
        "-p:UseSharedCompilation=false"
    )
}

foreach ($name in $selectedChunks) {
    $filter = $chunks[$name]
    Write-Host "=== WebView2 chunk: $name ==="
    if ($ListOnly) {
        Invoke-DotNet @(
            "test",
            $projectPath,
            "-c",
            $Configuration,
            "-p:Platform=$Platform",
            "-p:UseSharedCompilation=false",
            "--no-build",
            "--list-tests",
            "--filter",
            $filter
        )
    } else {
        Invoke-DotNet @(
            "test",
            $projectPath,
            "-c",
            $Configuration,
            "-p:Platform=$Platform",
            "-p:UseSharedCompilation=false",
            "--no-build",
            "--filter",
            $filter
        )
    }
}
