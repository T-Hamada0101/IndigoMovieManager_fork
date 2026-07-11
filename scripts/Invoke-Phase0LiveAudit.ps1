[CmdletBinding()]
param(
    [string]$LogPath,
    [string]$Configuration = 'Release',
    [string]$Platform = 'x64',
    [switch]$NoBuild,
    [string]$ManualReviewPath
)

# 目視確認JSONは、Phase 0で固定した8シナリオと全確認項目だけを受け付ける。
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
$allowedManualReviewStatuses = @('pending', 'pass', 'fail', 'not_observed')

function Get-Phase0ManualReviewValidation
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $issues = [System.Collections.Generic.List[string]]::new()
    $statusCounts = @{}

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf))
    {
        return [pscustomobject]@{
            IsComplete = $false
            Summary    = 'file-not-found'
        }
    }

    try
    {
        $resolvedPath = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
        $document = Get-Content -LiteralPath $resolvedPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    }
    catch
    {
        return [pscustomobject]@{
            IsComplete = $false
            Summary    = 'invalid-json'
        }
    }

    if ($null -eq $document -or $document.schema -ne 'phase0-manual-review-v1')
    {
        $issues.Add('invalid-schema')
    }

    if ($null -eq $document -or $document.PSObject.Properties.Match('created_utc').Count -ne 1)
    {
        $issues.Add('missing-created-utc')
    }
    else
    {
        $createdUtc = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParse([string]$document.created_utc, [ref]$createdUtc))
        {
            $issues.Add('invalid-created-utc')
        }
    }

    if ($null -eq $document -or $document.PSObject.Properties.Match('scenarios').Count -ne 1)
    {
        return [pscustomobject]@{
            IsComplete = $false
            Summary    = (($issues + 'missing-scenarios') -join ', ')
        }
    }

    $actualScenariosByKey = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    $duplicateScenarioKeys = [System.Collections.Generic.List[string]]::new()
    foreach ($scenario in @($document.scenarios))
    {
        $scenarioKey = if ($null -eq $scenario) { '' } else { [string]$scenario.key }
        if ([string]::IsNullOrWhiteSpace($scenarioKey))
        {
            $issues.Add('invalid-scenario-key')
            continue
        }

        if ($actualScenariosByKey.ContainsKey($scenarioKey))
        {
            $duplicateScenarioKeys.Add($scenarioKey)
            continue
        }

        $actualScenariosByKey[$scenarioKey] = $scenario
    }

    if ($duplicateScenarioKeys.Count -gt 0)
    {
        $issues.Add("duplicate-scenarios=$(@($duplicateScenarioKeys | Select-Object -Unique) -join ',')")
    }

    $expectedScenarioKeys = @($expectedManualReviewScenarios | ForEach-Object { $_.key })
    $missingScenarioKeys = @($expectedScenarioKeys | Where-Object { -not $actualScenariosByKey.ContainsKey($_) })
    $unexpectedScenarioKeys = @($actualScenariosByKey.Keys | Where-Object { $_ -cnotin $expectedScenarioKeys })
    if ($missingScenarioKeys.Count -gt 0)
    {
        $issues.Add("missing-scenarios=$($missingScenarioKeys -join ',')")
    }
    if ($unexpectedScenarioKeys.Count -gt 0)
    {
        $issues.Add("unexpected-scenarios=$($unexpectedScenarioKeys -join ',')")
    }

    foreach ($expectedScenario in $expectedManualReviewScenarios)
    {
        $scenarioKey = $expectedScenario.key
        if (-not $actualScenariosByKey.ContainsKey($scenarioKey))
        {
            continue
        }

        $actualScenario = $actualScenariosByKey[$scenarioKey]
        if ($actualScenario.PSObject.Properties.Match('checks').Count -ne 1)
        {
            $issues.Add("missing-checks=$scenarioKey")
            continue
        }

        $actualChecksByKey = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
        $duplicateCheckKeys = [System.Collections.Generic.List[string]]::new()
        foreach ($check in @($actualScenario.checks))
        {
            $checkKey = if ($null -eq $check) { '' } else { [string]$check.key }
            if ([string]::IsNullOrWhiteSpace($checkKey))
            {
                $issues.Add("invalid-check-key=$scenarioKey")
                continue
            }

            if ($actualChecksByKey.ContainsKey($checkKey))
            {
                $duplicateCheckKeys.Add($checkKey)
                continue
            }

            $actualChecksByKey[$checkKey] = $check
        }

        if ($duplicateCheckKeys.Count -gt 0)
        {
            $issues.Add("duplicate-checks=${scenarioKey}:$(@($duplicateCheckKeys | Select-Object -Unique) -join ',')")
        }

        $expectedCheckKeys = @($expectedScenario.checks)
        $missingCheckKeys = @($expectedCheckKeys | Where-Object { -not $actualChecksByKey.ContainsKey($_) })
        $unexpectedCheckKeys = @($actualChecksByKey.Keys | Where-Object { $_ -cnotin $expectedCheckKeys })
        if ($missingCheckKeys.Count -gt 0)
        {
            $issues.Add("missing-checks=${scenarioKey}:$($missingCheckKeys -join ',')")
        }
        if ($unexpectedCheckKeys.Count -gt 0)
        {
            $issues.Add("unexpected-checks=${scenarioKey}:$($unexpectedCheckKeys -join ',')")
        }

        foreach ($expectedCheckKey in $expectedCheckKeys)
        {
            if (-not $actualChecksByKey.ContainsKey($expectedCheckKey))
            {
                continue
            }

            $actualCheck = $actualChecksByKey[$expectedCheckKey]
            if ($actualCheck.PSObject.Properties.Match('notes').Count -ne 1 -or $actualCheck.notes -isnot [string])
            {
                $issues.Add("invalid-notes=${scenarioKey}:$expectedCheckKey")
            }

            $status = if ($actualCheck.PSObject.Properties.Match('status').Count -eq 1) { [string]$actualCheck.status } else { '' }
            if ($status -cnotin $allowedManualReviewStatuses)
            {
                $issues.Add("invalid-status=${scenarioKey}:$expectedCheckKey")
                continue
            }

            if ($status -ne 'pass')
            {
                if (-not $statusCounts.ContainsKey($status))
                {
                    $statusCounts[$status] = 0
                }

                $statusCounts[$status]++
            }
        }
    }

    foreach ($status in $statusCounts.Keys | Sort-Object)
    {
        $issues.Add("$status=$($statusCounts[$status])")
    }

    $summaryIssues = @($issues | Select-Object -Unique)
    if ($summaryIssues.Count -eq 0)
    {
        return [pscustomobject]@{
            IsComplete = $true
            Summary    = 'complete'
        }
    }

    return [pscustomobject]@{
        IsComplete = $false
        Summary    = ($summaryIssues -join ', ')
    }
}

# スクリプトの場所を基準に、監査対象のテストプロジェクトを解決する。
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'Tests/IndigoMovieManager.Tests/IndigoMovieManager.Tests.csproj'

if ([string]::IsNullOrWhiteSpace($LogPath))
{
    $LogPath = Join-Path $env:LOCALAPPDATA 'IndigoMovieManager\logs\debug-runtime.log'
}

if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf))
{
    throw "監査対象ログが見つかりません: $LogPath"
}

$resolvedLogPath = (Resolve-Path -LiteralPath $LogPath).Path

Write-Host "Phase0 live auditを実行します: $resolvedLogPath"
Write-Host '非0終了は不足evidenceを示し、監査未完なら想定内です。'

$dotnetArguments = @(
    'test',
    $projectPath,
    '-c',
    $Configuration,
    "-p:Platform=$Platform",
    '--filter',
    'FullyQualifiedName~DebugRuntimeLogPhase0LiveAuditTests&Name~OptIn_live_audit'
)

if ($NoBuild)
{
    $dotnetArguments += '--no-build'
}

$enabledEnvironmentName = 'IMM_PHASE0_LOG_AUDIT_LIVE'
$pathEnvironmentName = 'IMM_PHASE0_LOG_AUDIT_PATH'
$previousEnabledValue = [Environment]::GetEnvironmentVariable($enabledEnvironmentName, 'Process')
$previousPathValue = [Environment]::GetEnvironmentVariable($pathEnvironmentName, 'Process')
$exitCode = 1
$manualReviewValidation = $null

try
{
    # 監査用の環境変数は子プロセス実行中だけ設定し、親PowerShellへ残さない。
    Set-Item -Path "Env:$enabledEnvironmentName" -Value '1'
    Set-Item -Path "Env:$pathEnvironmentName" -Value $resolvedLogPath

    & dotnet @dotnetArguments
    $exitCode = $LASTEXITCODE

    if (-not [string]::IsNullOrWhiteSpace($ManualReviewPath))
    {
        # 指定時だけ目視確認も同じ完了ゲートへ加え、ログ監査の終了コードはそのまま尊重する。
        $manualReviewValidation = Get-Phase0ManualReviewValidation -Path $ManualReviewPath
        if ($manualReviewValidation.IsComplete)
        {
            Write-Host 'Phase0 manual review: complete'
        }
        else
        {
            Write-Host "Phase0 manual review: incomplete ($($manualReviewValidation.Summary))"
            $exitCode = 1
        }
    }
}
finally
{
    if ($null -eq $previousEnabledValue)
    {
        Remove-Item "Env:$enabledEnvironmentName" -ErrorAction SilentlyContinue
    }
    else
    {
        Set-Item -Path "Env:$enabledEnvironmentName" -Value $previousEnabledValue
    }

    if ($null -eq $previousPathValue)
    {
        Remove-Item "Env:$pathEnvironmentName" -ErrorAction SilentlyContinue
    }
    else
    {
        Set-Item -Path "Env:$pathEnvironmentName" -Value $previousPathValue
    }
}

exit $exitCode
