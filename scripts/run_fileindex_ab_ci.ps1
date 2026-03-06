param(
    [string]$Configuration = "Debug",
    [string]$Platform = "x64",
    [string]$MyLabRoot = "",
    [switch]$UseExternalUsnMft
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-MsBuildPath {
    # まず開発環境固定パスを優先し、見つからない場合はvswhereで探索する。
    $preferred = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
    if (Test-Path $preferred) {
        return $preferred
    }

    $vsWhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vsWhere) {
        $found = & $vsWhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if (-not [string]::IsNullOrWhiteSpace($found) -and (Test-Path $found)) {
            return $found
        }
    }

    throw "MSBuild が見つかりません。Visual Studio Build Tools を確認してください。"
}

function Resolve-RepoRoot {
    $scriptDir = Split-Path -Parent $PSCommandPath
    return (Resolve-Path (Join-Path $scriptDir "..")).Path
}

function Resolve-ExternalUsnMftCsprojPath {
    param([string]$MyLabRootPath)

    # プロジェクト名変更中でも動くよう、USN/MFTコア実装ファイルで候補を特定する。
    $projectFiles = Get-ChildItem -Path $MyLabRootPath -Filter *.csproj -Recurse -File -ErrorAction SilentlyContinue
    foreach ($projectFile in $projectFiles) {
        $projectDir = Split-Path -Parent $projectFile.FullName
        if (
            (Test-Path (Join-Path $projectDir "FileIndexService.cs")) -and
            (Test-Path (Join-Path $projectDir "AdminUsnMftIndexBackend.cs")) -and
            (Test-Path (Join-Path $projectDir "IFileIndexService.cs"))
        ) {
            return $projectFile.FullName
        }
    }

    throw "外部UsnMftプロジェクトが見つかりません: $MyLabRootPath"
}

$repoRoot = Resolve-RepoRoot

$msbuildArgs = @(
    ".\IndigoMovieManager_fork.sln",
    "/restore",
    "/t:Build",
    "/p:Configuration=$Configuration",
    "/p:Platform=$Platform",
    "/m"
)

if ($UseExternalUsnMft) {
    if ([string]::IsNullOrWhiteSpace($MyLabRoot)) {
        $MyLabRoot = Join-Path (Split-Path -Parent $repoRoot) "MyLab"
    }

    $usnMftCsproj = Resolve-ExternalUsnMftCsprojPath -MyLabRootPath $MyLabRoot

    # 外部版を使うときだけプロジェクト参照先を上書きする。
    $msbuildArgs += "/p:UseExternalUsnMft=true"
    $msbuildArgs += "/p:ExternalUsnMftProjectPath=$usnMftCsproj"
}

$msbuildPath = Resolve-MsBuildPath
Write-Host "[AB-CI] RepoRoot=$repoRoot"
Write-Host "[AB-CI] UseExternalUsnMft=$UseExternalUsnMft"
if ($UseExternalUsnMft) {
    Write-Host "[AB-CI] MyLabRoot=$MyLabRoot"
}
Write-Host "[AB-CI] MSBuild=$msbuildPath"

Push-Location $repoRoot
try {
    # COM参照やWPFを含むため、先にMSBuildでソリューションをビルドする。
    & $msbuildPath @msbuildArgs
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild が失敗しました。exit code: $LASTEXITCODE"
    }

    # Provider差分の回帰対象だけを抽出して実行する。
    $filter = "FullyQualifiedName~UsnMftProviderTests|FullyQualifiedName~StandardFileSystemProviderTests|FullyQualifiedName~FileIndexProviderAbDiffTests|FullyQualifiedName~FileIndexReasonTableTests|FullyQualifiedName~FileIndexProviderFactoryTests"
    & dotnet test ".\Tests\IndigoMovieManager_fork.Tests\IndigoMovieManager_fork.Tests.csproj" -c $Configuration --no-build --filter $filter
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test が失敗しました。exit code: $LASTEXITCODE"
    }

    Write-Host "[AB-CI] Completed successfully."
}
finally {
    Pop-Location
}
