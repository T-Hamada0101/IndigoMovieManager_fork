[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CopiedDbPath,

    [Parameter(Mandatory = $true)]
    [string]$ManualReviewPath,

    [switch]$AcknowledgeCopiedDb,

    [string]$ExecutablePath,

    [switch]$Wait
)

# Phase 0で同じrunに束縛できる、未使用の目視確認テンプレートだけを定義する。
$expectedManualReviewScenarios = @(
    [ordered]@{ key = 'startup'; checks = @('first-useful-display', 'input-ready', 'blank') },
    [ordered]@{ key = 'search-sort-scroll'; checks = @('input-continuity', 'selection', 'focus', 'scroll', 'blank', 'multi-selection', 'scroll-anchor', 'operation-feedback', 'continued-input-during-feedback') },
    [ordered]@{ key = 'tab-selection-page'; checks = @('selection', 'focus', 'page-or-scroll-position', 'blank', 'focus-not-stolen') },
    [ordered]@{ key = 'watch-small-diff'; checks = @('selection', 'scroll', 'blank', 'stale-rollback') },
    [ordered]@{ key = 'player'; checks = @('playback-continuity', 'selection', 'focus', 'blank', 'operation-feedback') },
    [ordered]@{ key = 'image'; checks = @('visible-first', 'stale-image', 'blank') },
    [ordered]@{ key = 'skin'; checks = @('content-visible', 'focus', 'blank', 'flicker') },
    [ordered]@{ key = 'persistence-shutdown'; checks = @('setting-retained', 'shutdown-completes', 'blank') }
)

function Get-Phase0UnusedManualReviewTemplate
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf))
    {
        throw "目視確認テンプレートが見つからないか、ファイルではありません: $Path"
    }

    try
    {
        $resolvedPath = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
        $document = Get-Content -LiteralPath $resolvedPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    }
    catch
    {
        throw "目視確認テンプレートは有効なJSONである必要があります: $Path"
    }

    if ($null -eq $document -or $document.schema -ne 'phase0-manual-review-v1')
    {
        throw "目視確認テンプレートのschemaが不正です: $resolvedPath"
    }

    if ($document.PSObject.Properties.Match('created_utc').Count -ne 1)
    {
        throw "目視確認テンプレートのcreated_utcがありません: $resolvedPath"
    }

    if ($document.PSObject.Properties.Match('session').Count -ne 1 -or $null -eq $document.session)
    {
        throw "目視確認テンプレートにsessionがありません: $resolvedPath"
    }

    if (
        $document.session.PSObject.Properties.Match('id').Count -ne 1 -or
        $document.session.PSObject.Properties.Match('started_local').Count -ne 1 -or
        $document.session.id -isnot [string] -or
        $document.session.started_local -isnot [string] -or
        -not [string]::IsNullOrEmpty($document.session.id) -or
        -not [string]::IsNullOrEmpty($document.session.started_local)
    )
    {
        throw "目視確認テンプレートは未使用sessionだけを受け付けます: $resolvedPath"
    }

    $actualScenariosByKey = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    foreach ($scenario in @($document.scenarios))
    {
        $scenarioKey = if ($null -eq $scenario) { '' } else { [string]$scenario.key }
        if ([string]::IsNullOrWhiteSpace($scenarioKey) -or $actualScenariosByKey.ContainsKey($scenarioKey))
        {
            throw "目視確認テンプレートのscenarioが不正です: $resolvedPath"
        }

        $actualScenariosByKey[$scenarioKey] = $scenario
    }

    if ($actualScenariosByKey.Count -ne $expectedManualReviewScenarios.Count)
    {
        throw "目視確認テンプレートのscenario数が不正です: $resolvedPath"
    }

    $pendingCount = 0
    foreach ($expectedScenario in $expectedManualReviewScenarios)
    {
        if (-not $actualScenariosByKey.ContainsKey($expectedScenario.key))
        {
            throw "目視確認テンプレートのscenarioが不足しています: $resolvedPath"
        }

        $actualChecksByKey = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
        foreach ($check in @($actualScenariosByKey[$expectedScenario.key].checks))
        {
            $checkKey = if ($null -eq $check) { '' } else { [string]$check.key }
            if ([string]::IsNullOrWhiteSpace($checkKey) -or $actualChecksByKey.ContainsKey($checkKey))
            {
                throw "目視確認テンプレートのcheckが不正です: $resolvedPath"
            }

            $actualChecksByKey[$checkKey] = $check
        }

        if ($actualChecksByKey.Count -ne $expectedScenario.checks.Count)
        {
            throw "目視確認テンプレートのcheck数が不正です: $resolvedPath"
        }

        foreach ($expectedCheckKey in $expectedScenario.checks)
        {
            if (
                -not $actualChecksByKey.ContainsKey($expectedCheckKey) -or
                $actualChecksByKey[$expectedCheckKey].status -cne 'pending' -or
                $actualChecksByKey[$expectedCheckKey].PSObject.Properties.Match('notes').Count -ne 1 -or
                $actualChecksByKey[$expectedCheckKey].notes -isnot [string] -or
                -not [string]::IsNullOrEmpty($actualChecksByKey[$expectedCheckKey].notes)
            )
            {
                throw "目視確認テンプレートは未記入の全36項目がpendingである必要があります: $resolvedPath"
            }

            $pendingCount++
        }
    }

    if ($pendingCount -ne 36)
    {
        throw "目視確認テンプレートは未記入の全36項目がpendingである必要があります: $resolvedPath"
    }

    return [pscustomobject]@{
        Path     = $resolvedPath
        Document = $document
    }
}

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
    INDIGO_DEBUG_RUNTIME_LOG_MAX_BYTES = '134217728'
}

# 起動直前に同じJSONへrun固有のsessionを記録し、再利用できない証跡にする。
$manualReviewTemplate = Get-Phase0UnusedManualReviewTemplate -Path $ManualReviewPath
$resolvedManualReviewPath = $manualReviewTemplate.Path
$manualReviewDocument = $manualReviewTemplate.Document
$manualReviewDocument.session.id = [Guid]::NewGuid().ToString('D')
$manualReviewDocument.session.started_local = [DateTime]::Now.ToString('yyyy-MM-dd HH:mm:ss.fff', [System.Globalization.CultureInfo]::InvariantCulture)
$manualReviewJson = $manualReviewDocument | ConvertTo-Json -Depth 6
$manualReviewJson = $manualReviewJson -replace "`r`n?", "`n"
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
$manualReviewOriginalBytes = [System.IO.File]::ReadAllBytes($resolvedManualReviewPath)
[System.IO.File]::WriteAllText($resolvedManualReviewPath, $manualReviewJson, $utf8WithoutBom)

try
{
    $process = Start-Process `
        -FilePath $resolvedExecutablePath `
        -WorkingDirectory (Split-Path -Parent $resolvedExecutablePath) `
        -Environment $childEnvironment `
        -PassThru
}
catch
{
    # 起動できなかったrunは未使用テンプレートへ戻し、同じ証跡を次回へ持ち越さない。
    [System.IO.File]::WriteAllBytes($resolvedManualReviewPath, $manualReviewOriginalBytes)
    throw
}

Write-Host "Phase 0診断アプリを起動しました。PID: $($process.Id)"
Write-Host "コピー済みDB: $resolvedDbPath"
Write-Host "目視確認JSON: $resolvedManualReviewPath"
Write-Host @'
主要8シナリオ:
1. cold startから first-page shown、input ready、heavy services started まで。
2. Grid系Resetを含む検索・通常sort後に、主選択・複数選択・先頭可視top・focusを確認する。250ms未満は操作表示を出さず、超過時も入力とscroll / PageUp / PageDownを継続し、Wrap系とList系のoffsetを別記録する。
3. 上側タブとLogタブを往復後、選択を保持し、SearchBox/別ペインのfocusを奪わないことを確認する。
4. 検索中にwatch 1件追加とrenameを発生させる。
5. Player準備中の操作表示を確認し、開始、停止、音量を変更する。
6. visible thumbnail、進捗、ERROR一覧、詳細画像を表示する。
7. コピーDB + no-persistでskin通常切り替えとHeader Reloadを行う。
8. 設定変更、bookmark / score / tag保存後に終了する。

起動前に scripts/New-Phase0ManualReview.ps1 で目視記録JSONを作成し、-ManualReviewPath <目視記録.json> を指定してください。
この起動でsessionを束縛した目視記録JSONだけを記入し、操作終了後は scripts/Invoke-Phase0LiveAudit.ps1 -ManualReviewPath <目視記録.json> でログと目視記録を監査してください。
'@

if ($Wait) {
    # 対話採取を妨げないまま、要求された時だけ終了を待つ。
    Wait-Process -Id $process.Id
}

Write-Output $process
