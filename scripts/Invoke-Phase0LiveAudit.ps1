[CmdletBinding()]
param(
    [string]$LogPath,
    [string]$Configuration = 'Release',
    [string]$Platform = 'x64',
    [switch]$NoBuild
)

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

try
{
    # 監査用の環境変数は子プロセス実行中だけ設定し、親PowerShellへ残さない。
    Set-Item -Path "Env:$enabledEnvironmentName" -Value '1'
    Set-Item -Path "Env:$pathEnvironmentName" -Value $resolvedLogPath

    & dotnet @dotnetArguments
    $exitCode = $LASTEXITCODE
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
