param(
    [ValidateSet("Import", "Export")]
    [string]$Mode = "Import",
    [string]$RepoRoot = "",
    [string]$MyLabRoot = "",
    [string]$ExternalSourceRoot = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    $scriptDir = Split-Path -Parent $PSCommandPath
    return (Resolve-Path (Join-Path $scriptDir "..")).Path
}

function Convert-ToInternalUsnMftNamespace {
    param([string]$Text)
    # 取り込み後は技術名ベースの名前空間へ統一する。
    return $Text -replace "namespace\s+[A-Za-z0-9_.]+", "namespace IndigoMovieManager.FileIndex.UsnMft"
}

function Convert-ToExternalUsnMftNamespace {
    param([string]$Text)
    # 再分離時は外部プロジェクト側の既存名前空間へ戻す。
    return $Text -replace "namespace\s+IndigoMovieManager\.FileIndex\.UsnMft", "namespace UsnMft"
}

function Test-HasSyncFiles {
    param(
        [string]$DirectoryPath,
        [string[]]$Files
    )

    foreach ($relative in $Files) {
        if (-not (Test-Path (Join-Path $DirectoryPath $relative))) {
            return $false
        }
    }

    return $true
}

function Resolve-ExternalSourceRoot {
    param(
        [string]$MyLabRootPath,
        [string[]]$Files
    )

    $preferred = Join-Path $MyLabRootPath "UsnMft"
    if (Test-Path $preferred) {
        return (Resolve-Path $preferred).Path
    }

    # プロジェクト名変更中でも動くよう、同期対象ファイルセットが揃うディレクトリを探索する。
    $candidates = Get-ChildItem -Path $MyLabRootPath -Directory -ErrorAction SilentlyContinue
    foreach ($candidate in $candidates) {
        if (Test-HasSyncFiles -DirectoryPath $candidate.FullName -Files $Files) {
            return (Resolve-Path $candidate.FullName).Path
        }
    }

    throw "外部同期元ディレクトリが見つかりません: $MyLabRootPath"
}

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Resolve-RepoRoot
}

if ([string]::IsNullOrWhiteSpace($MyLabRoot)) {
    $MyLabRoot = Join-Path (Split-Path -Parent $RepoRoot) "MyLab"
}

$targetRoot = Join-Path $RepoRoot "src\IndigoMovieManager.FileIndex.UsnMft"

$syncFiles = @(
    "AdminUsnMftIndexBackend.cs",
    "AppStructuredLog.cs",
    "FileIndexService.cs",
    "FileIndexServiceOptions.cs",
    "IFileIndexService.cs",
    "IIndexBackend.cs",
    "IndexProgress.cs",
    "SearchResultItem.cs",
    "StandardFileSystemIndexBackend.cs"
)

if ([string]::IsNullOrWhiteSpace($ExternalSourceRoot)) {
    $sourceRoot = Resolve-ExternalSourceRoot -MyLabRootPath $MyLabRoot -Files $syncFiles
}
else {
    $sourceRoot = (Resolve-Path $ExternalSourceRoot).Path
}

if ($Mode -eq "Import") {
    if (-not (Test-Path $sourceRoot)) {
        throw "ソースが見つかりません: $sourceRoot"
    }

    New-Item -ItemType Directory -Force -Path $targetRoot | Out-Null
    foreach ($relative in $syncFiles) {
        $src = Join-Path $sourceRoot $relative
        if (-not (Test-Path $src)) {
            throw "取り込み元ファイルが見つかりません: $src"
        }

        $dst = Join-Path $targetRoot $relative
        $raw = Get-Content -Encoding UTF8 -Raw $src
        $converted = Convert-ToInternalUsnMftNamespace -Text $raw
        $normalized = $converted -replace "`r`n", "`n"
        Set-Content -Encoding utf8 -NoNewline -Path $dst -Value $normalized
        Write-Host "[sync:import] $relative"
    }
}
else {
    if (-not (Test-Path $targetRoot)) {
        throw "エクスポート元が見つかりません: $targetRoot"
    }

    New-Item -ItemType Directory -Force -Path $sourceRoot | Out-Null
    foreach ($relative in $syncFiles) {
        $src = Join-Path $targetRoot $relative
        if (-not (Test-Path $src)) {
            throw "エクスポート元ファイルが見つかりません: $src"
        }

        $dst = Join-Path $sourceRoot $relative
        $raw = Get-Content -Encoding UTF8 -Raw $src
        $converted = Convert-ToExternalUsnMftNamespace -Text $raw
        $normalized = $converted -replace "`r`n", "`n"
        Set-Content -Encoding utf8 -NoNewline -Path $dst -Value $normalized
        Write-Host "[sync:export] $relative"
    }
}

Write-Host "[sync] completed mode=$Mode repo=$RepoRoot mylab=$MyLabRoot"
