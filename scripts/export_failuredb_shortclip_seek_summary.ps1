param(
    [string]$MainDbPath = "",
    [string]$FailureDbPath = "",
    [string]$BuildDir = "",
    [string]$LogRoot = "",
    [string]$OutputPath = "",
    [int]$TopCount = 20,
    [int]$RuntimeMatchWindowSec = 180,
    [switch]$IncludeFailed
)

$ErrorActionPreference = "Stop"

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Content
    )

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}

function Escape-MarkdownCell {
    param([object]$Value)

    if ($null -eq $Value) {
        return ""
    }

    $text = [string]$Value
    $text = $text -replace "\r?\n", "<br>"
    $text = $text -replace "\|", "\|"
    return $text
}

function New-MarkdownTable {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$Rows,
        [Parameter(Mandatory = $true)]
        [string[]]$Columns
    )

    if ($Rows.Count -lt 1) {
        return "_データなし_"
    }

    $header = "| " + (($Columns | ForEach-Object { Escape-MarkdownCell $_ }) -join " | ") + " |"
    $separator = "| " + (($Columns | ForEach-Object { "---" }) -join " | ") + " |"
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add($header)
    $lines.Add($separator)

    foreach ($row in $Rows) {
        $cells = foreach ($column in $Columns) {
            Escape-MarkdownCell $row.$column
        }
        $lines.Add("| " + ($cells -join " | ") + " |")
    }

    return ($lines -join "`n")
}

function Resolve-BuildDir {
    param([string]$RequestedBuildDir)

    if (-not [string]::IsNullOrWhiteSpace($RequestedBuildDir)) {
        return (Resolve-Path -LiteralPath $RequestedBuildDir).Path
    }

    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    $candidates = @(
        (Join-Path $repoRoot "artifacts\autogen-playground-stage-2"),
        (Join-Path $repoRoot "artifacts\ffmpeg-shortclip-stage-2"),
        (Join-Path $repoRoot "artifacts\thumbnail-failuretab-stage"),
        (Join-Path $repoRoot "bin\x64\Debug\net8.0-windows")
    )

    foreach ($candidate in $candidates) {
        if (
            (Test-Path -LiteralPath $candidate -PathType Container) -and
            (Test-Path -LiteralPath (Join-Path $candidate "System.Data.SQLite.dll") -PathType Leaf)
        ) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "System.Data.SQLite.dll を含む BuildDir が見つかりません。-BuildDir を指定してください。"
}

function Resolve-FailureDbPath {
    param(
        [string]$RequestedMainDbPath,
        [string]$RequestedFailureDbPath
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedFailureDbPath)) {
        return (Resolve-Path -LiteralPath $RequestedFailureDbPath).Path
    }

    if (-not [string]::IsNullOrWhiteSpace($RequestedMainDbPath)) {
        $mainName = [System.IO.Path]::GetFileNameWithoutExtension($RequestedMainDbPath)
        $failureRoot = Join-Path $env:LOCALAPPDATA "IndigoMovieManager_fork\FailureDb"
        $pattern = "$mainName*.failure-debug.imm"
        $matched = Get-ChildItem -Path $failureRoot -Filter $pattern -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if ($null -ne $matched) {
            return $matched.FullName
        }
    }

    $defaultRoot = Join-Path $env:LOCALAPPDATA "IndigoMovieManager_fork\FailureDb"
    $latest = Get-ChildItem -Path $defaultRoot -Filter "*.failure-debug.imm" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -ne $latest) {
        return $latest.FullName
    }

    throw "FailureDb が見つかりません。-FailureDbPath または -MainDbPath を指定してください。"
}

function Resolve-LogRoot {
    param([string]$RequestedLogRoot)

    if (-not [string]::IsNullOrWhiteSpace($RequestedLogRoot)) {
        return (Resolve-Path -LiteralPath $RequestedLogRoot).Path
    }

    $defaultRoot = Join-Path $env:LOCALAPPDATA "IndigoMovieManager_fork\logs"
    if (Test-Path -LiteralPath $defaultRoot -PathType Container) {
        return (Resolve-Path -LiteralPath $defaultRoot).Path
    }

    throw "runtime log ルートが見つかりません。-LogRoot を指定してください。"
}

function Read-FailureDbRows {
    param([string]$DbPath)

    $connection = [System.Data.SQLite.SQLiteConnection]::new("Data Source=$DbPath")
    $connection.Open()
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = @"
SELECT
    RecordId,
    MoviePath,
    PanelType,
    Reason,
    FailureKind,
    QueueStatus,
    EngineId,
    LastError,
    OccurredAtUtc,
    ExtraJson
FROM ThumbnailFailureDebug
WHERE PanelType IN ('autogen-playground', 'ffmpeg-short-playground')
ORDER BY OccurredAtUtc DESC, RecordId DESC;
"@

        $reader = $command.ExecuteReader()
        $rows = New-Object System.Collections.Generic.List[object]
        try {
            while ($reader.Read()) {
                $rows.Add([pscustomobject]@{
                        RecordId = $reader.GetInt64(0)
                        MoviePath = if ($reader.IsDBNull(1)) { "" } else { $reader.GetString(1) }
                        PanelType = if ($reader.IsDBNull(2)) { "" } else { $reader.GetString(2) }
                        Reason = if ($reader.IsDBNull(3)) { "" } else { $reader.GetString(3) }
                        FailureKind = if ($reader.IsDBNull(4)) { "" } else { $reader.GetString(4) }
                        QueueStatus = if ($reader.IsDBNull(5)) { "" } else { $reader.GetString(5) }
                        EngineId = if ($reader.IsDBNull(6)) { "" } else { $reader.GetString(6) }
                        LastError = if ($reader.IsDBNull(7)) { "" } else { $reader.GetString(7) }
                        OccurredAtUtc = if ($reader.IsDBNull(8)) { "" } else { $reader.GetString(8) }
                        ExtraJson = if ($reader.IsDBNull(9)) { "" } else { $reader.GetString(9) }
                    })
            }
        }
        finally {
            $reader.Dispose()
            $command.Dispose()
        }

        return $rows
    }
    finally {
        $connection.Dispose()
    }
}

function Convert-ToAttemptRow {
    param([Parameter(Mandatory = $true)][pscustomobject]$Row)

    $extra = $null
    if (-not [string]::IsNullOrWhiteSpace($Row.ExtraJson)) {
        try {
            $extra = $Row.ExtraJson | ConvertFrom-Json
        }
        catch {
            $extra = $null
        }
    }

    $seekSec = $null
    if ($null -ne $extra -and $null -ne $extra.SeekSec -and $extra.SeekSec -ne "") {
        $seekSec = [double]$extra.SeekSec
    }

    $isSuccess = $false
    if ($null -ne $extra -and $null -ne $extra.IsSuccess) {
        $isSuccess = [bool]$extra.IsSuccess
    }
    elseif ($Row.QueueStatus -eq "Done") {
        $isSuccess = $true
    }

    [pscustomobject]@{
        RecordId = $Row.RecordId
        MoviePath = $Row.MoviePath
        MovieName = [System.IO.Path]::GetFileName($Row.MoviePath)
        PanelType = $Row.PanelType
        Reason = $Row.Reason
        FailureKind = $Row.FailureKind
        QueueStatus = $Row.QueueStatus
        EngineId = $Row.EngineId
        LastError = $Row.LastError
        OccurredAtUtc = $Row.OccurredAtUtc
        SeekSec = $seekSec
        SeekKey = if ($null -eq $seekSec) { "" } else { $seekSec.ToString("0.###", [System.Globalization.CultureInfo]::InvariantCulture) }
        IsSuccess = $isSuccess
    }
}

function Try-ParseDateTimeLocal {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    $cultures = @(
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.CultureInfo]::GetCultureInfo("ja-JP")
    )
    $styles = [System.Globalization.DateTimeStyles]::AssumeLocal
    foreach ($culture in $cultures) {
        $parsed = [datetime]::MinValue
        if ([datetime]::TryParse($Text, $culture, $styles, [ref]$parsed)) {
            return $parsed
        }
    }

    return $null
}

function Try-ParseOccurredAtLocal {
    param([string]$OccurredAtUtc)

    if ([string]::IsNullOrWhiteSpace($OccurredAtUtc)) {
        return $null
    }

    $stylesUtc = [System.Globalization.DateTimeStyles]::AssumeUniversal -bor [System.Globalization.DateTimeStyles]::AdjustToUniversal
    $utc = [datetime]::MinValue
    if ([datetime]::TryParse($OccurredAtUtc, [System.Globalization.CultureInfo]::InvariantCulture, $stylesUtc, [ref]$utc)) {
        return $utc.ToLocalTime()
    }

    return (Try-ParseDateTimeLocal -Text $OccurredAtUtc)
}

function Read-RuntimeFallbackEvents {
    param([string]$ResolvedLogRoot)

    $pattern = "autogen-header-frame-fallback|ffmpeg1pass-shortclip-seek"
    $logFiles = Get-ChildItem -LiteralPath $ResolvedLogRoot -Filter *.log -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending

    $events = New-Object System.Collections.Generic.List[object]
    foreach ($file in $logFiles) {
        $matchedLines = Select-String -Path $file.FullName -Pattern $pattern -SimpleMatch:$false -ErrorAction SilentlyContinue
        foreach ($matched in $matchedLines) {
            $line = $matched.Line
            if ([string]::IsNullOrWhiteSpace($line)) {
                continue
            }

            $parts = $line -split "`t", 3
            if ($parts.Length -lt 3) {
                continue
            }

            $timestamp = Try-ParseDateTimeLocal -Text $parts[0]
            $category = $parts[1]
            $message = $parts[2]
            $engineId = switch ($category) {
                "autogen-header-frame-fallback" { "autogen" }
                "ffmpeg1pass-shortclip-seek" { "ffmpeg1pass" }
                default { "" }
            }

            if ([string]::IsNullOrWhiteSpace($engineId)) {
                continue
            }

            $outcome = ""
            $moviePath = ""
            $seekSec = $null
            $candidateText = ""
            $errorText = ""

            if ($message -match "^fallback hit: movie='(?<movie>.+?)' sec=(?<sec>-?\d+(?:\.\d+)?)") {
                $outcome = "hit"
                $moviePath = $Matches["movie"]
                $seekSec = [double]::Parse($Matches["sec"], [System.Globalization.CultureInfo]::InvariantCulture)
            }
            elseif ($message -match "^fallback hit: movie='(?<movie>.+?)' start_sec=(?<sec>-?\d+(?:\.\d+)?)") {
                $outcome = "hit"
                $moviePath = $Matches["movie"]
                $seekSec = [double]::Parse($Matches["sec"], [System.Globalization.CultureInfo]::InvariantCulture)
            }
            elseif ($message -match "^fallback miss: movie='(?<movie>.+?)' candidates=\[(?<candidates>[^\]]*)\]") {
                $outcome = "miss"
                $moviePath = $Matches["movie"]
                $candidateText = $Matches["candidates"]
            }
            elseif ($message -match "^fallback miss: movie='(?<movie>.+?)' start_sec=(?<sec>-?\d+(?:\.\d+)?) err='(?<err>.*)'") {
                $outcome = "miss"
                $moviePath = $Matches["movie"]
                $seekSec = [double]::Parse($Matches["sec"], [System.Globalization.CultureInfo]::InvariantCulture)
                $errorText = $Matches["err"]
            }
            elseif ($message -match "^fallback exhausted: movie='(?<movie>.+?)' candidates=\[(?<candidates>[^\]]*)\]") {
                $outcome = "exhausted"
                $moviePath = $Matches["movie"]
                $candidateText = $Matches["candidates"]
            }
            elseif ($message -match "^fallback exhausted: movie='(?<movie>.+?)' err='(?<err>.*)'") {
                $outcome = "exhausted"
                $moviePath = $Matches["movie"]
                $errorText = $Matches["err"]
            }

            if ([string]::IsNullOrWhiteSpace($outcome) -or [string]::IsNullOrWhiteSpace($moviePath)) {
                continue
            }

            $events.Add([pscustomobject]@{
                    TimestampLocal = $timestamp
                    EngineId = $engineId
                    MoviePath = $moviePath
                    SeekSec = $seekSec
                    Outcome = $outcome
                    Candidates = $candidateText
                    ErrorText = $errorText
                    Category = $category
                    LogFile = $file.Name
                    LineNumber = $matched.LineNumber
                })
        }
    }

    return $events
}

function Add-RuntimeMatchInfo {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$AttemptRows,
        [Parameter(Mandatory = $true)]
        [object[]]$RuntimeEvents,
        [Parameter(Mandatory = $true)]
        [int]$MatchWindowSec
    )

    foreach ($row in $AttemptRows) {
        $row | Add-Member -NotePropertyName RuntimeFallbackOutcome -NotePropertyValue ""
        $row | Add-Member -NotePropertyName RuntimeFallbackSec -NotePropertyValue ""
        $row | Add-Member -NotePropertyName RuntimeFallbackHitSec -NotePropertyValue ""
        $row | Add-Member -NotePropertyName RuntimeFallbackCandidates -NotePropertyValue ""
        $row | Add-Member -NotePropertyName RuntimeFallbackError -NotePropertyValue ""
        $row | Add-Member -NotePropertyName RuntimeFallbackHitAtLocal -NotePropertyValue ""
        $row | Add-Member -NotePropertyName RuntimeFallbackLogFile -NotePropertyValue ""
        $row | Add-Member -NotePropertyName RuntimeFallbackDiffSec -NotePropertyValue ""

        $occurredLocal = Try-ParseOccurredAtLocal -OccurredAtUtc $row.OccurredAtUtc
        $candidates = $RuntimeEvents | Where-Object {
            $_.EngineId -eq $row.EngineId -and $_.MoviePath -eq $row.MoviePath
        }
        if ($null -eq $candidates -or $candidates.Count -lt 1) {
            continue
        }

        $best = $null
        $bestDiffSec = [double]::PositiveInfinity
        foreach ($candidate in $candidates) {
            if ($null -eq $occurredLocal -or $null -eq $candidate.TimestampLocal) {
                if ($null -eq $best) {
                    $best = $candidate
                }
                continue
            }

            $diffSec = [math]::Abs(($candidate.TimestampLocal - $occurredLocal).TotalSeconds)
            if ($diffSec -le $MatchWindowSec -and $diffSec -lt $bestDiffSec) {
                $best = $candidate
                $bestDiffSec = $diffSec
            }
        }

        if ($null -eq $best) {
            continue
        }

        $row.RuntimeFallbackOutcome = $best.Outcome
        $row.RuntimeFallbackSec = if ($null -eq $best.SeekSec) { "" } else { $best.SeekSec.ToString("0.###", [System.Globalization.CultureInfo]::InvariantCulture) }
        $row.RuntimeFallbackHitSec = if ($best.Outcome -eq "hit" -and $null -ne $best.SeekSec) { $best.SeekSec.ToString("0.###", [System.Globalization.CultureInfo]::InvariantCulture) } else { "" }
        $row.RuntimeFallbackCandidates = $best.Candidates
        $row.RuntimeFallbackError = $best.ErrorText
        $row.RuntimeFallbackHitAtLocal = if ($null -eq $best.TimestampLocal) { "" } else { $best.TimestampLocal.ToString("yyyy-MM-dd HH:mm:ss.fff") }
        $row.RuntimeFallbackLogFile = "$($best.LogFile):$($best.LineNumber)"
        $row.RuntimeFallbackDiffSec = if ([double]::IsPositiveInfinity($bestDiffSec)) { "" } else { $bestDiffSec.ToString("0.###", [System.Globalization.CultureInfo]::InvariantCulture) }
    }

    return $AttemptRows
}

function New-SummaryRows {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Rows,
        [Parameter(Mandatory = $true)]
        [int]$Limit
    )

    $groups = $Rows |
        Group-Object EngineId, SeekKey, QueueStatus |
        Sort-Object Count -Descending

    $summary = foreach ($group in $groups | Select-Object -First $Limit) {
        $first = $group.Group | Select-Object -First 1
        [pscustomobject]@{
            EngineId = $first.EngineId
            SeekSec = $first.SeekKey
            RuntimeOutcome = ($group.Group | Where-Object { -not [string]::IsNullOrWhiteSpace($_.RuntimeFallbackOutcome) } | Group-Object RuntimeFallbackOutcome | Sort-Object Count -Descending | Select-Object -First 1 -ExpandProperty Name)
            RuntimeSec = ($group.Group | Where-Object { -not [string]::IsNullOrWhiteSpace($_.RuntimeFallbackSec) } | Group-Object RuntimeFallbackSec | Sort-Object Count -Descending | Select-Object -First 1 -ExpandProperty Name)
            RuntimeHitSec = ($group.Group | Where-Object { -not [string]::IsNullOrWhiteSpace($_.RuntimeFallbackHitSec) } | Group-Object RuntimeFallbackHitSec | Sort-Object Count -Descending | Select-Object -First 1 -ExpandProperty Name)
            QueueStatus = $first.QueueStatus
            Count = $group.Count
            Movies = ($group.Group | Group-Object MovieName | Select-Object -ExpandProperty Name) -join ", "
            Reasons = ($group.Group | Group-Object Reason | Select-Object -ExpandProperty Name) -join ", "
        }
    }

    return ,$summary
}

$buildDir = Resolve-BuildDir -RequestedBuildDir $BuildDir
$sqliteDll = Join-Path $buildDir "System.Data.SQLite.dll"
[void][System.Reflection.Assembly]::LoadFrom($sqliteDll)

$resolvedFailureDbPath = Resolve-FailureDbPath -RequestedMainDbPath $MainDbPath -RequestedFailureDbPath $FailureDbPath
$resolvedLogRoot = Resolve-LogRoot -RequestedLogRoot $LogRoot
$runtimeEvents = Read-RuntimeFallbackEvents -ResolvedLogRoot $resolvedLogRoot
$resolvedRows = @(Read-FailureDbRows -DbPath $resolvedFailureDbPath | ForEach-Object { Convert-ToAttemptRow -Row $_ })
if ($resolvedRows.Count -gt 0) {
    $resolvedRows = @(Add-RuntimeMatchInfo -AttemptRows $resolvedRows -RuntimeEvents $runtimeEvents -MatchWindowSec $RuntimeMatchWindowSec)
}
$targetRows = if ($IncludeFailed) {
    @($resolvedRows)
}
else {
    @($resolvedRows | Where-Object { $_.QueueStatus -eq "Done" })
}

if ($targetRows.Count -gt 0) {
    $summaryRows = @(New-SummaryRows -Rows $targetRows -Limit $TopCount)
    $detailRows = @($targetRows |
        Sort-Object OccurredAtUtc -Descending |
        Select-Object -First $TopCount |
        ForEach-Object {
            [pscustomobject]@{
                OccurredAtUtc = $_.OccurredAtUtc
                EngineId = $_.EngineId
                QueueStatus = $_.QueueStatus
                SeekSec = $_.SeekKey
                RuntimeOutcome = $_.RuntimeFallbackOutcome
                RuntimeSec = $_.RuntimeFallbackSec
                RuntimeHitSec = $_.RuntimeFallbackHitSec
                RuntimeHitAtLocal = $_.RuntimeFallbackHitAtLocal
                MovieName = $_.MovieName
                Reason = $_.Reason
                FailureKind = $_.FailureKind
                RuntimeCandidates = $_.RuntimeFallbackCandidates
                RuntimeError = $_.RuntimeFallbackError
                RuntimeLog = $_.RuntimeFallbackLogFile
                LastError = $_.LastError
            }
        })
}
else {
    $summaryRows = @()
    $detailRows = @()
}

$summaryTable = New-MarkdownTable -Rows $summaryRows -Columns @("EngineId", "SeekSec", "RuntimeOutcome", "RuntimeSec", "RuntimeHitSec", "QueueStatus", "Count", "Movies", "Reasons")
$detailTable = New-MarkdownTable -Rows $detailRows -Columns @("OccurredAtUtc", "EngineId", "QueueStatus", "SeekSec", "RuntimeOutcome", "RuntimeSec", "RuntimeHitSec", "RuntimeHitAtLocal", "MovieName", "Reason", "FailureKind", "RuntimeCandidates", "RuntimeError", "RuntimeLog", "LastError")

$content = @"
# FailureDb 短尺 seek 集計 $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")

- FailureDb: $resolvedFailureDbPath
- BuildDir: $buildDir
- LogRoot: $resolvedLogRoot
- Runtime fallback 行数: $($runtimeEvents.Count)
- Runtime 突合せ許容秒: $RuntimeMatchWindowSec
- 対象行数: $($targetRows.Count)
- 対象種別: $(if ($IncludeFailed) { "Done + Failed" } else { "Done のみ" })

## 集計
$summaryTable

## 明細（先頭 $TopCount 件）
$detailTable
"@

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    $OutputPath = Join-Path $repoRoot ".local\failuredb_shortclip_seek_summary_$(Get-Date -Format 'yyyyMMdd_HHmmss').md"
}

$outputDir = Split-Path -Path $OutputPath -Parent
if (-not [string]::IsNullOrWhiteSpace($outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

Write-Utf8NoBom -Path $OutputPath -Content $content
Write-Host "FailureDb : $resolvedFailureDbPath"
Write-Host "Output    : $OutputPath"
Write-Host "Rows      : $($targetRows.Count)"
