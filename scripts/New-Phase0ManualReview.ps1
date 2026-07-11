[CmdletBinding()]
param(
    [string]$OutputPath
)

# Phase 0で同じrunに記録する、固定の目視確認項目を定義する。
$scenarios = @(
    [ordered]@{ key = 'startup'; checks = @('first-useful-display', 'input-ready', 'blank') },
    [ordered]@{ key = 'search-sort-scroll'; checks = @('input-continuity', 'selection', 'focus', 'scroll', 'blank', 'multi-selection', 'scroll-anchor', 'operation-feedback', 'continued-input-during-feedback') },
    [ordered]@{ key = 'tab-selection-page'; checks = @('selection', 'focus', 'page-or-scroll-position', 'blank', 'focus-not-stolen') },
    [ordered]@{ key = 'watch-small-diff'; checks = @('selection', 'scroll', 'blank', 'stale-rollback') },
    [ordered]@{ key = 'player'; checks = @('playback-continuity', 'selection', 'focus', 'blank', 'operation-feedback') },
    [ordered]@{ key = 'image'; checks = @('visible-first', 'stale-image', 'blank') },
    [ordered]@{ key = 'skin'; checks = @('content-visible', 'focus', 'blank', 'flicker') },
    [ordered]@{ key = 'persistence-shutdown'; checks = @('setting-retained', 'shutdown-completes', 'blank') }
)

if ([string]::IsNullOrWhiteSpace($OutputPath))
{
    $timestamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
    $OutputPath = Join-Path $env:LOCALAPPDATA "IndigoMovieManager\logs\phase0-manual-review-$timestamp.json"
}

$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $resolvedOutputPath)
{
    throw "目視確認テンプレートは上書きしません: $resolvedOutputPath"
}

$parentDirectory = Split-Path -Parent $resolvedOutputPath
if (-not (Test-Path -LiteralPath $parentDirectory -PathType Container))
{
    New-Item -ItemType Directory -Path $parentDirectory -ErrorAction Stop | Out-Null
}

# 記録開始時は全項目を未確認にして、人間の判定だけを後から追記できるようにする。
$document = [ordered]@{
    schema      = 'phase0-manual-review-v1'
    created_utc = [DateTime]::UtcNow.ToString('o')
    session     = [ordered]@{
        id            = ''
        started_local = ''
    }
    scenarios   = @(
        foreach ($scenario in $scenarios)
        {
            [ordered]@{
                key    = $scenario.key
                checks = @(
                    foreach ($checkKey in $scenario.checks)
                    {
                        [ordered]@{
                            key    = $checkKey
                            status = 'pending'
                            notes  = ''
                        }
                    }
                )
            }
        }
    )
}

# PowerShellの既定改行に依存せず、BOMなしUTF-8とLFで証跡を固定する。
$json = $document | ConvertTo-Json -Depth 6
$json = $json -replace "`r`n?", "`n"
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($resolvedOutputPath, $json, $utf8WithoutBom)

Write-Output $resolvedOutputPath
