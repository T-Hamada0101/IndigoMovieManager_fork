[CmdletBinding()]
param(
    [string]$ProjectPath = "",
    [string]$MainExePath = "",
    [string]$VersionLabel = "",
    [switch]$AllowNonReleaseLabel
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Convert-ToAppVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,
        [Parameter(Mandatory = $true)]
        [string]$SourceName
    )

    $normalized = $Value.Trim()
    if ($normalized -notmatch '^\d+\.\d+\.\d+\.\d+$') {
        throw "$SourceName は 1.0.5.0 のような4要素の数値版数にしてください: '$Value'"
    }

    # FileVersion と MSI の双方で安全に扱える範囲へ入口を揃える。
    $parts = @($normalized.Split('.') | ForEach-Object { [uint32]::Parse($_) })
    if ($parts[0] -gt 255 -or $parts[1] -gt 255 -or $parts[2] -gt 65535 -or $parts[3] -gt 65535) {
        throw "$SourceName が配布版数の範囲を超えています: '$Value'"
    }

    return ($parts -join '.')
}

function Get-ProjectVersions {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResolvedProjectPath
    )

    # project評価は1回にまとめ、Release入口の待ち時間と評価条件の揺れを抑える。
    $output = & dotnet msbuild $ResolvedProjectPath -nologo "-getProperty:Version,FileVersion,AssemblyVersion"
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild 版数プロパティの取得に失敗しました。"
    }

    $outputText = $output -join [Environment]::NewLine
    $jsonStart = $outputText.IndexOf('{')
    $jsonEnd = $outputText.LastIndexOf('}')
    if ($jsonStart -lt 0 -or $jsonEnd -lt $jsonStart) {
        throw "MSBuild 版数プロパティのJSONを取得できませんでした。"
    }

    # 初回SDKメッセージが前後へ出ても、MSBuildのJSON部分だけを読む。
    $evaluated = $outputText.Substring($jsonStart, $jsonEnd - $jsonStart + 1) | ConvertFrom-Json
    return [ordered]@{
        Version = [string]$evaluated.Properties.Version
        FileVersion = [string]$evaluated.Properties.FileVersion
        AssemblyVersion = [string]$evaluated.Properties.AssemblyVersion
    }
}

function Get-VersionFromLabel {
    param(
        [string]$Label,
        [bool]$AllowNonRelease
    )

    if ([string]::IsNullOrWhiteSpace($Label)) {
        return ""
    }

    $trimmed = $Label.Trim()
    if ($trimmed -match '^v(.+)$') {
        return Convert-ToAppVersion -Value $Matches[1] -SourceName "正式リリースラベル"
    }

    if ($AllowNonRelease) {
        return ""
    }

    throw "正式リリースラベルは v1.0.5.0 の形式にしてください: '$Label'"
}

if ([string]::IsNullOrWhiteSpace($ProjectPath) -and [string]::IsNullOrWhiteSpace($MainExePath)) {
    throw "ProjectPath または MainExePath のどちらかが必要です。"
}

$projectVersion = ""
$exeVersion = ""
$labelVersion = Get-VersionFromLabel -Label $VersionLabel -AllowNonRelease $AllowNonReleaseLabel.IsPresent

if (-not [string]::IsNullOrWhiteSpace($ProjectPath)) {
    $resolvedProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
    $evaluatedProjectVersions = Get-ProjectVersions -ResolvedProjectPath $resolvedProjectPath
    $projectVersions = [ordered]@{}

    foreach ($entry in $evaluatedProjectVersions.GetEnumerator()) {
        $projectVersions[$entry.Key] = Convert-ToAppVersion -Value $entry.Value -SourceName "project $($entry.Key)"
    }

    $uniqueProjectVersions = @($projectVersions.Values | Sort-Object -Unique)
    if ($uniqueProjectVersions.Count -ne 1) {
        throw "project の Version / FileVersion / AssemblyVersion が一致していません: $($projectVersions | ConvertTo-Json -Compress)"
    }

    $projectVersion = $uniqueProjectVersions[0]
}

if (-not [string]::IsNullOrWhiteSpace($MainExePath)) {
    $resolvedMainExePath = (Resolve-Path -LiteralPath $MainExePath).Path
    $rawExeVersion = [string](Get-Item -LiteralPath $resolvedMainExePath).VersionInfo.FileVersion
    if ([string]::IsNullOrWhiteSpace($rawExeVersion)) {
        throw "配布EXEの FileVersion を取得できません: $resolvedMainExePath"
    }

    $exeVersion = Convert-ToAppVersion -Value $rawExeVersion -SourceName "配布EXE FileVersion"
}

if (-not [string]::IsNullOrWhiteSpace($projectVersion) -and
    -not [string]::IsNullOrWhiteSpace($exeVersion) -and
    $projectVersion -ne $exeVersion) {
    throw "project と配布EXEの版数が一致していません: project='$projectVersion' exe='$exeVersion'"
}

$actualVersion = if (-not [string]::IsNullOrWhiteSpace($exeVersion)) { $exeVersion } else { $projectVersion }
if (-not [string]::IsNullOrWhiteSpace($labelVersion) -and $labelVersion -ne $actualVersion) {
    throw "正式リリースラベルと実体の版数が一致していません: label='$labelVersion' actual='$actualVersion'"
}

Write-Host "ReleaseVersionConsistency: OK"
Write-Host "VersionLabel: $(if ([string]::IsNullOrWhiteSpace($VersionLabel)) { '-' } else { $VersionLabel })"
Write-Host "ProjectVersion: $(if ([string]::IsNullOrWhiteSpace($projectVersion)) { '-' } else { $projectVersion })"
Write-Host "ExeFileVersion: $(if ([string]::IsNullOrWhiteSpace($exeVersion)) { '-' } else { $exeVersion })"
