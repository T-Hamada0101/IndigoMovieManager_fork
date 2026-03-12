[CmdletBinding()]
param(
    [int]$TimeoutSec = 15,
    [int]$TailLineCount = 200,
    [string]$AppExePath = "",
    [switch]$LaunchApp,
    [switch]$NoFollow
)

$ErrorActionPreference = "Stop"

if ($TimeoutSec -lt 1 -or $TimeoutSec -gt 600) {
    throw "TimeoutSec は 1 から 600 の範囲で指定してください。"
}
if ($TailLineCount -lt 10 -or $TailLineCount -gt 5000) {
    throw "TailLineCount は 10 から 5000 の範囲で指定してください。"
}

# スクリプト配置場所からリポジトリルートへ移動する。
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($AppExePath)) {
    $AppExePath = Join-Path $repoRoot "bin\x64\Debug\net8.0-windows\IndigoMovieManager_fork.exe"
}

$logsDir = Join-Path $env:LOCALAPPDATA "IndigoMovieManager_fork\logs"
$runtimeLogPath = Join-Path $logsDir "debug-runtime.log"
$keywords = @(
    "thumbnail-timeout",
    "thumbnail-recovery",
    "consumer dispatch begin",
    "consumer lane entered",
    "consumer processing watchdog start",
    "repair start",
    "repair success",
    "repair failed"
)

New-Item -ItemType Directory -Path $logsDir -Force | Out-Null

# このセッションだけ normal lane timeout とファイルログを有効にする。
[Environment]::SetEnvironmentVariable(
    "IMM_THUMB_NORMAL_LANE_TIMEOUT_SEC",
    $TimeoutSec.ToString([System.Globalization.CultureInfo]::InvariantCulture),
    "Process"
)
[Environment]::SetEnvironmentVariable("IMM_THUMB_FILE_LOG", "1", "Process")

Write-Host "normal lane timeout 実機確認を開始します。"
Write-Host "RepoRoot : $repoRoot"
Write-Host "AppExe   : $AppExePath"
Write-Host "LogPath  : $runtimeLogPath"
Write-Host "Timeout  : ${TimeoutSec}s"
Write-Host "Keywords : $($keywords -join ', ')"
Write-Host ""
Write-Host "この PowerShell セッションでは次を設定済みです。"
Write-Host "  IMM_THUMB_NORMAL_LANE_TIMEOUT_SEC=$TimeoutSec"
Write-Host "  IMM_THUMB_FILE_LOG=1"
Write-Host ""
Write-Host "期待する並び:"
Write-Host "  thumbnail-timeout -> thumbnail-recovery -> consumer lane entered"
Write-Host ""

if ($LaunchApp) {
    if (-not (Test-Path -LiteralPath $AppExePath -PathType Leaf)) {
        throw "アプリ実行ファイルが見つかりません: $AppExePath"
    }

    # 同じ環境変数を引き継いだまま本体を起動する。
    $process = Start-Process -FilePath $AppExePath -PassThru
    Write-Host "アプリを起動しました。Pid=$($process.Id)"
}
else {
    Write-Host "必要なら同じセッションからアプリを起動してください。"
}

Write-Host ""
Write-Host "難動画を投入したら、下のログに thumbnail-timeout / thumbnail-recovery が出るか確認してください。"
Write-Host "停止は Ctrl+C です。"
Write-Host ""

if (-not (Test-Path -LiteralPath $runtimeLogPath -PathType Leaf)) {
    Write-Host "まだ debug-runtime.log はありません。アプリ起動後に生成されます。"
}

while (-not (Test-Path -LiteralPath $runtimeLogPath -PathType Leaf)) {
    Start-Sleep -Milliseconds 500
}

if ($NoFollow) {
    Get-Content -LiteralPath $runtimeLogPath -Encoding UTF8 -Tail $TailLineCount |
    Select-String -Pattern ($keywords -join "|") -SimpleMatch:$false
}
else {
    Get-Content -LiteralPath $runtimeLogPath -Encoding UTF8 -Tail $TailLineCount -Wait |
    Select-String -Pattern ($keywords -join "|") -SimpleMatch:$false
}
