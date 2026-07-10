[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CopiedDbPath,

    [switch]$AcknowledgeCopiedDb,

    [string]$ExecutablePath,

    [switch]$Wait
)

# ユーザーDBを直接扱わず、明示的に用意されたコピーだけを診断へ渡す。
if (-not $AcknowledgeCopiedDb) {
    throw "安全確認が必要です。コピー済みDBだけを指定する意味で -AcknowledgeCopiedDb を指定してください。ユーザーDBの自動コピーは行いません。"
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$defaultExecutablePath = Join-Path $repoRoot 'bin\x64\Release\net8.0-windows10.0.19041.0\IndigoMovieManager.exe'

# 入力DBは既存の単一ファイルであることと、WhiteBrowser形式であることだけを確認する。
if (-not (Test-Path -LiteralPath $CopiedDbPath -PathType Leaf)) {
    throw "コピー済みDBが見つからないか、ファイルではありません: $CopiedDbPath"
}

$resolvedDb = Resolve-Path -LiteralPath $CopiedDbPath -ErrorAction Stop
$resolvedDbPath = $resolvedDb.Path
if ([System.IO.Path]::GetExtension($resolvedDbPath) -ine '.wb') {
    throw "診断対象は .wb ファイルに限定されます: $resolvedDbPath"
}

$requestedExecutablePath = if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $defaultExecutablePath
}
else {
    $ExecutablePath
}

if (-not (Test-Path -LiteralPath $requestedExecutablePath -PathType Leaf)) {
    throw "Release x64アプリが見つからないか、ファイルではありません: $requestedExecutablePath"
}

$resolvedExecutable = Resolve-Path -LiteralPath $requestedExecutablePath -ErrorAction Stop
$resolvedExecutablePath = $resolvedExecutable.Path
if ([System.IO.Path]::GetFileName($resolvedExecutablePath) -ine 'IndigoMovieManager.exe') {
    throw "起動対象は IndigoMovieManager.exe に限定されます: $resolvedExecutablePath"
}

# 診断用の値は子プロセスだけへ渡し、launcher自身の環境は変更しない。
$childEnvironment = @{
    INDIGO_DIAGNOSTIC_NO_PERSIST = '1'
    INDIGO_DIAGNOSTIC_STARTUP_DB = $resolvedDbPath
    INDIGO_RELEASE_LOG_MODE      = '1'
}

$process = Start-Process `
    -FilePath $resolvedExecutablePath `
    -WorkingDirectory (Split-Path -Parent $resolvedExecutablePath) `
    -Environment $childEnvironment `
    -PassThru

Write-Host "Phase 0診断アプリを起動しました。PID: $($process.Id)"
Write-Host "コピー済みDB: $resolvedDbPath"
Write-Host @'
主要8シナリオ:
1. cold startから first-page shown、input ready、heavy services started まで。
2. 検索入力中にsortを変更し、直後にscroll / PageUp / PageDownする。
3. 上側タブ、Logタブ、選択、ページを連続で切り替える。
4. 検索中にwatch 1件追加とrenameを発生させる。
5. Playerを開始、停止し、音量を変更する。
6. visible thumbnail、進捗、ERROR一覧、詳細画像を表示する。
7. コピーDB + no-persistでskin通常切り替えとHeader Reloadを行う。
8. 設定変更、bookmark / score / tag保存後に終了する。

操作終了後は scripts/Invoke-Phase0LiveAudit.ps1 を実行してログを監査してください。
'@

if ($Wait) {
    # 対話採取を妨げないまま、要求された時だけ終了を待つ。
    Wait-Process -Id $process.Id
}

Write-Output $process
