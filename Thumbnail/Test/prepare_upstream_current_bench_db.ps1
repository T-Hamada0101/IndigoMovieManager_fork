param(
    [string]$UpstreamBuildDir = "",
    [string]$DbPath = "",
    [string]$InputFolder = "",
    [string]$ThumbFolder = "",
    [string]$BookmarkFolder = "",
    [switch]$Recreate,
    [switch]$ResetArtifacts
)

$ErrorActionPreference = "Stop"

# 既定値はスクリプト相対とプレースホルダ前提に寄せ、ローカル固有パスを持たない。
if ([string]::IsNullOrWhiteSpace($UpstreamBuildDir)) {
    $UpstreamBuildDir = "<upstream-build-dir>"
}
if ([string]::IsNullOrWhiteSpace($DbPath)) {
    $DbPath = Join-Path $PSScriptRoot "..\..\bench\upstream_current_bench.wb"
}
if ([string]::IsNullOrWhiteSpace($InputFolder)) {
    $InputFolder = "<input-video-root>"
}
if ([string]::IsNullOrWhiteSpace($ThumbFolder)) {
    $ThumbFolder = Join-Path $PSScriptRoot "..\..\bench_output\upstream_current_bench\Thumb"
}
if ([string]::IsNullOrWhiteSpace($BookmarkFolder)) {
    $BookmarkFolder = Join-Path $PSScriptRoot "..\..\bench_output\upstream_current_bench\Bookmark"
}

# ベンチ前提を毎回そろえるため、入力・出力・依存DLLを先に検証する。
if (-not (Test-Path -LiteralPath $InputFolder -PathType Container)) {
    throw "入力フォルダが見つかりません: $InputFolder"
}
if (-not (Test-Path -LiteralPath $UpstreamBuildDir -PathType Container)) {
    throw "上流ビルド出力が見つかりません: $UpstreamBuildDir"
}

$mainDll = Join-Path $UpstreamBuildDir "IndigoMovieManager.dll"
$sqliteDll = Join-Path $UpstreamBuildDir "System.Data.SQLite.dll"
if (-not (Test-Path -LiteralPath $mainDll -PathType Leaf)) {
    throw "IndigoMovieManager.dll が見つかりません: $mainDll"
}
if (-not (Test-Path -LiteralPath $sqliteDll -PathType Leaf)) {
    throw "System.Data.SQLite.dll が見つかりません: $sqliteDll"
}

$dbDir = Split-Path -Path $DbPath -Parent
if ([string]::IsNullOrWhiteSpace($dbDir)) {
    throw "DbPath に親フォルダが必要です: $DbPath"
}

$logPath = Join-Path $env:LOCALAPPDATA "IndigoMovieManager_bench_upstream_current\logs\bench-runtime.log"
New-Item -ItemType Directory -Path $dbDir -Force | Out-Null
New-Item -ItemType Directory -Path $ThumbFolder -Force | Out-Null
New-Item -ItemType Directory -Path $BookmarkFolder -Force | Out-Null

if ($ResetArtifacts) {
    if (Test-Path -LiteralPath $ThumbFolder -PathType Container) {
        Get-ChildItem -Path $ThumbFolder -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $BookmarkFolder -PathType Container) {
        Get-ChildItem -Path $BookmarkFolder -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $logPath -PathType Leaf) {
        Remove-Item -LiteralPath $logPath -Force
    }
}

if ($Recreate -and (Test-Path -LiteralPath $DbPath -PathType Leaf)) {
    Remove-Item -LiteralPath $DbPath -Force
}

# 既存のCreateDatabase実装をそのまま呼び、スキーマ差異を避ける。
[void][System.Reflection.Assembly]::LoadFrom($mainDll)
[void][System.Reflection.Assembly]::LoadFrom($sqliteDll)
$mainAssembly = [System.AppDomain]::CurrentDomain.GetAssemblies() |
    Where-Object { $_.Location -eq $mainDll } |
    Select-Object -First 1
$type = $mainAssembly.GetType("IndigoMovieManager.DB.SQLite", $false)
if ($null -eq $type) {
    throw "IndigoMovieManager.DB.SQLite を解決できません。"
}
$createMethod = $type.GetMethod("CreateDatabase", [System.Reflection.BindingFlags]"Public,Static")
if ($null -eq $createMethod) {
    throw "CreateDatabase を解決できません。"
}

if (-not (Test-Path -LiteralPath $DbPath -PathType Leaf)) {
    [void]$createMethod.Invoke($null, @($DbPath))
}

$connection = [System.Data.SQLite.SQLiteConnection]::new("Data Source=$DbPath")
$connection.Open()
try {
    $transaction = $connection.BeginTransaction()
    try {
        $commands = @(
            "delete from watch",
            "delete from system where attr in ('thum','bookmark')",
            "insert into watch(dir, auto, watch, sub) values(@dir, 1, 1, 1)",
            "insert into system(attr, value) values('thum', @thum)",
            "insert into system(attr, value) values('bookmark', @bookmark)"
        )

        foreach ($sql in $commands) {
            $cmd = $connection.CreateCommand()
            $cmd.Transaction = $transaction
            $cmd.CommandText = $sql
            if ($sql.Contains("@dir")) {
                [void]$cmd.Parameters.Add([System.Data.SQLite.SQLiteParameter]::new("@dir", $InputFolder))
            }
            if ($sql.Contains("@thum")) {
                [void]$cmd.Parameters.Add([System.Data.SQLite.SQLiteParameter]::new("@thum", $ThumbFolder))
            }
            if ($sql.Contains("@bookmark")) {
                [void]$cmd.Parameters.Add([System.Data.SQLite.SQLiteParameter]::new("@bookmark", $BookmarkFolder))
            }
            [void]$cmd.ExecuteNonQuery()
            $cmd.Dispose()
        }

        $transaction.Commit()
    }
    catch {
        $transaction.Rollback()
        throw
    }
}
finally {
    $connection.Dispose()
}

$dbName = [System.IO.Path]::GetFileNameWithoutExtension($DbPath)
Write-Host "準備完了"
Write-Host "DB          : $DbPath"
Write-Host "入力フォルダ: $InputFolder"
Write-Host "Thumb       : $ThumbFolder"
Write-Host "Bookmark    : $BookmarkFolder"
Write-Host "ログ        : $logPath"
Write-Host "次の操作    : 上流アプリでこの .wb を開き、初回走査を待つ"
Write-Host "DB名        : $dbName"
