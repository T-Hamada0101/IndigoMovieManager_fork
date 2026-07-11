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
            IsValid             = $false
            IsComplete          = $false
            SessionStartedLocal = ''
            Summary             = 'file-not-found'
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
            IsValid             = $false
            IsComplete          = $false
            SessionStartedLocal = ''
            Summary             = 'invalid-json'
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

    $sessionId = [Guid]::Empty
    $sessionStartedLocal = [DateTime]::MinValue
    if ($null -eq $document -or $document.PSObject.Properties.Match('session').Count -ne 1 -or $null -eq $document.session)
    {
        $issues.Add('missing-session')
    }
    else
    {
        if (
            $document.session.PSObject.Properties.Match('id').Count -ne 1 -or
            -not [Guid]::TryParse([string]$document.session.id, [ref]$sessionId)
        )
        {
            $issues.Add('invalid-session-id')
        }

        if (
            $document.session.PSObject.Properties.Match('started_local').Count -ne 1 -or
            -not [DateTime]::TryParseExact(
                [string]$document.session.started_local,
                'yyyy-MM-dd HH:mm:ss.fff',
                [System.Globalization.CultureInfo]::InvariantCulture,
                [System.Globalization.DateTimeStyles]::None,
                [ref]$sessionStartedLocal
            )
        )
        {
            $issues.Add('invalid-session-started-local')
        }
    }
    if ($null -eq $document -or $document.PSObject.Properties.Match('scenarios').Count -ne 1)
    {
        return [pscustomobject]@{
            IsValid             = $false
            IsComplete          = $false
            SessionStartedLocal = ''
            Summary             = (($issues + 'missing-scenarios') -join ', ')
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

    $completionIssues = [System.Collections.Generic.List[string]]::new()
    foreach ($status in $statusCounts.Keys | Sort-Object)
    {
        $completionIssues.Add("$status=$($statusCounts[$status])")
    }

    $validationIssues = @($issues | Select-Object -Unique)
    $isValid = $validationIssues.Count -eq 0
    $isComplete = $isValid -and $completionIssues.Count -eq 0
    $summary = if ($isComplete)
    {
        'complete'
    }
    elseif ($isValid)
    {
        $completionIssues -join ', '
    }
    else
    {
        @($validationIssues + $completionIssues | Select-Object -Unique) -join ', '
    }

    return [pscustomobject]@{
        IsValid             = $isValid
        IsComplete          = $isComplete
        SessionStartedLocal = if ($isValid) { $document.session.started_local } else { '' }
        Summary             = $summary
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

$manualReviewValidation = $null
$manualReviewSessionStartedLocal = ''
$manualReviewIsIncomplete = $false
if (-not [string]::IsNullOrWhiteSpace($ManualReviewPath))
{
    # dotnet監査より先に構造とsessionを検証し、未完でも同じrunのログevidenceは採取する。
    $manualReviewValidation = Get-Phase0ManualReviewValidation -Path $ManualReviewPath
    if (-not $manualReviewValidation.IsValid)
    {
        Write-Host "Phase0 manual review: incomplete ($($manualReviewValidation.Summary))"
        exit 1
    }

    $manualReviewSessionStartedLocal = $manualReviewValidation.SessionStartedLocal
    if ($manualReviewValidation.IsComplete)
    {
        Write-Host 'Phase0 manual review: complete'
    }
    else
    {
        Write-Host "Phase0 manual review: incomplete ($($manualReviewValidation.Summary))"
        $manualReviewIsIncomplete = $true
    }
}

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
$sessionStartedLocalEnvironmentName = 'IMM_PHASE0_LOG_AUDIT_SESSION_STARTED_LOCAL'
$previousEnabledValue = [Environment]::GetEnvironmentVariable($enabledEnvironmentName, 'Process')
$previousPathValue = [Environment]::GetEnvironmentVariable($pathEnvironmentName, 'Process')
$previousSessionStartedLocalValue = [Environment]::GetEnvironmentVariable($sessionStartedLocalEnvironmentName, 'Process')
$exitCode = 1

try
{
    # 監査用の環境変数は子プロセス実行中だけ設定し、親PowerShellへ残さない。
    Set-Item -Path "Env:$enabledEnvironmentName" -Value '1'
    Set-Item -Path "Env:$pathEnvironmentName" -Value $resolvedLogPath
    if (-not [string]::IsNullOrWhiteSpace($manualReviewSessionStartedLocal))
    {
        Set-Item -Path "Env:$sessionStartedLocalEnvironmentName" -Value $manualReviewSessionStartedLocal
    }
    else
    {
        # manual未指定の従来監査へ、親PowerShellの古いsession境界を継承させない。
        Remove-Item "Env:$sessionStartedLocalEnvironmentName" -ErrorAction SilentlyContinue
    }

    & dotnet @dotnetArguments
    $exitCode = $LASTEXITCODE
    if ($manualReviewIsIncomplete)
    {
        $exitCode = 1
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

    if ($null -eq $previousSessionStartedLocalValue)
    {
        Remove-Item "Env:$sessionStartedLocalEnvironmentName" -ErrorAction SilentlyContinue
    }
    else
    {
        Set-Item -Path "Env:$sessionStartedLocalEnvironmentName" -Value $previousSessionStartedLocalValue
    }
}

exit $exitCode
