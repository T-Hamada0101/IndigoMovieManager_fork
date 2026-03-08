param(
    [Parameter(Mandatory = $true)]
    [string]$InputMovie,
    [int]$Iteration = 3,
    [int]$Warmup = 1,
    [int]$TabIndex = 0,
    [string[]]$Priorities = @("Idle", "BelowNormal", "Normal", "AboveNormal", "High"),
    [switch]$Recovery,
    [string]$Configuration = "Debug",
    [string]$Platform = "x64",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $InputMovie -PathType Leaf)) {
    throw "入力動画が見つかりません: $InputMovie"
}
if ($Iteration -lt 1 -or $Iteration -gt 100) {
    throw "Iteration は 1 から 100 の範囲で指定してください。"
}
if ($Warmup -lt 0 -or $Warmup -gt 10) {
    throw "Warmup は 0 から 10 の範囲で指定してください。"
}
if ($TabIndex -notin @(0, 1, 2, 3, 4, 99)) {
    throw "TabIndex は 0,1,2,3,4,99 のいずれかを指定してください。"
}

# スクリプト配置場所からリポジトリルートへ移動する。
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot

$resolvedInputMovie = (Resolve-Path -LiteralPath $InputMovie).Path
$singleBenchScript = Join-Path $repoRoot "Thumbnail\Test\run_thumbnail_engine_bench.ps1"
$benchLogDir = Join-Path $env:LOCALAPPDATA "IndigoMovieManager_fork\logs"
$expectedInputFileName = [System.IO.Path]::GetFileName($resolvedInputMovie)
$expectedRowCount = $Iteration
$normalizedPriorities = $Priorities |
    ForEach-Object { $_.Trim() } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object {
        switch ($_.ToLowerInvariant()) {
            "idle" { "Idle" }
            "belownormal" { "BelowNormal" }
            "normal" { "Normal" }
            "abovenormal" { "AboveNormal" }
            "high" { "High" }
            default { throw "Priorities には Idle / BelowNormal / Normal / AboveNormal / High を指定してください: $_" }
        }
    } |
    Select-Object -Unique

if ($normalizedPriorities.Count -lt 1) {
    throw "Priorities が空です。"
}

function Resolve-BenchCsvForCurrentRun {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LogDir,
        [Parameter(Mandatory = $true)]
        [datetime]$Since,
        [Parameter(Mandatory = $true)]
        [string]$InputFileName,
        [Parameter(Mandatory = $true)]
        [int]$ExpectedRows,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedPriority,
        [Parameter(Mandatory = $true)]
        [bool]$ExpectedRecovery
    )

    $candidates = Get-ChildItem -LiteralPath $LogDir -Filter "thumbnail-engine-bench-*.csv" -File |
        Where-Object { $_.LastWriteTime -ge $Since.AddSeconds(-1) } |
        Sort-Object LastWriteTime -Descending

    foreach ($candidate in $candidates) {
        try {
            $rows = Import-Csv -LiteralPath $candidate.FullName
        }
        catch {
            continue
        }

        if (-not $rows -or $rows.Count -ne $ExpectedRows) {
            continue
        }

        $hasDifferentInput = ($rows | Where-Object { $_.input_file_name -ne $InputFileName } | Measure-Object).Count -gt 0
        if ($hasDifferentInput) {
            continue
        }

        $actualEngines = $rows |
            Select-Object -ExpandProperty engine -Unique |
            ForEach-Object { $_.Trim().ToLowerInvariant() } |
            Sort-Object -Unique
        if (($actualEngines -join ",") -ne "autogen") {
            continue
        }

        $hasDifferentPriority = ($rows | Where-Object { $_.priority -ne $ExpectedPriority } | Measure-Object).Count -gt 0
        if ($hasDifferentPriority) {
            continue
        }

        $expectedRecoveryText = if ($ExpectedRecovery) { "1" } else { "0" }
        $hasDifferentRecovery = ($rows | Where-Object { $_.is_recovery -ne $expectedRecoveryText } | Measure-Object).Count -gt 0
        if ($hasDifferentRecovery) {
            continue
        }

        return $candidate
    }

    return $null
}

$allRows = [System.Collections.Generic.List[object]]::new()
$startedAtAll = Get-Date
$isFirstRun = $true

foreach ($priority in $normalizedPriorities) {
    $startedAtSingle = Get-Date
    $singleArgs = @(
        "-File", $singleBenchScript,
        "-InputMovie", $resolvedInputMovie,
        "-Engines", "autogen",
        "-Iteration", $Iteration.ToString(),
        "-Warmup", $Warmup.ToString(),
        "-TabIndex", $TabIndex.ToString(),
        "-Priority", $priority,
        "-Configuration", $Configuration,
        "-Platform", $Platform
    )

    # 初回だけ必要ならビルドを許可し、以降は再ビルドしない。
    if ($SkipBuild -or -not $isFirstRun) {
        $singleArgs += "-SkipBuild"
    }
    if ($Recovery) {
        $singleArgs += "-Recovery"
    }

    Write-Host "autogen 優先度ベンチ: priority=$priority recovery=$($Recovery.IsPresent)"
    & pwsh @singleArgs
    if ($LASTEXITCODE -ne 0) {
        throw "run_thumbnail_engine_bench.ps1 が失敗しました。priority=$priority exit=$LASTEXITCODE"
    }

    $csv = Resolve-BenchCsvForCurrentRun `
        -LogDir $benchLogDir `
        -Since $startedAtSingle `
        -InputFileName $expectedInputFileName `
        -ExpectedRows $expectedRowCount `
        -ExpectedPriority $priority `
        -ExpectedRecovery $Recovery.IsPresent

    if (-not $csv) {
        throw "priority=$priority のベンチCSVを特定できませんでした。"
    }

    $rows = Import-Csv -LiteralPath $csv.FullName
    foreach ($row in $rows) {
        $allRows.Add([pscustomobject]@{
                datetime = $row.datetime
                input_file_name = $row.input_file_name
                input_size_bytes = $row.input_size_bytes
                bitrate_mbps = $row.bitrate_mbps
                play_time_sec = $row.play_time_sec
                requested_priority = $row.requested_priority
                priority = $row.priority
                priority_apply_error = $row.priority_apply_error
                is_recovery = $row.is_recovery
                drive_letter = $row.drive_letter
                engine = $row.engine
                iteration = [int]$row.iteration
                tab_index = [int]$row.tab_index
                panel_count = [int]$row.panel_count
                elapsed_ms = [double]$row.elapsed_ms
                success = $row.success
                output_bytes = $row.output_bytes
                output_path = $row.output_path
                error_message = $row.error_message
                source_csv = $csv.FullName
            })
    }

    $isFirstRun = $false
}

$logsDir = Join-Path $repoRoot "logs"
New-Item -ItemType Directory -Path $logsDir -Force | Out-Null
$ts = $startedAtAll.ToString("yyyyMMdd_HHmmss")
$outCombined = Join-Path $logsDir ("autogen-priority-bench-combined_{0}.csv" -f $ts)
$outSummary = Join-Path $logsDir ("autogen-priority-bench-summary_{0}.csv" -f $ts)

$allRows | Export-Csv -LiteralPath $outCombined -Encoding UTF8 -NoTypeInformation

$summary = $allRows |
    Group-Object priority, is_recovery |
    ForEach-Object {
        $successRows = $_.Group | Where-Object { $_.success -eq "success" }
        $successCount = $successRows.Count
        $failedCount = $_.Count - $successCount
        $avg = 0
        $min = 0
        $max = 0
        if ($successCount -gt 0) {
            $elapsed = $successRows | ForEach-Object { [double]$_.elapsed_ms }
            $avg = [math]::Round(($elapsed | Measure-Object -Average).Average, 2)
            $min = [math]::Round(($elapsed | Measure-Object -Minimum).Minimum, 2)
            $max = [math]::Round(($elapsed | Measure-Object -Maximum).Maximum, 2)
        }

        [pscustomobject]@{
            priority = $_.Group[0].priority
            is_recovery = $_.Group[0].is_recovery
            runs = $_.Count
            success = $successCount
            failed = $failedCount
            avg_ms_success = $avg
            min_ms_success = $min
            max_ms_success = $max
        }
    } |
    Sort-Object priority, is_recovery

$summary | Export-Csv -LiteralPath $outSummary -Encoding UTF8 -NoTypeInformation

Write-Host ""
Write-Host "autogen 優先度ベンチ完了"
Write-Host "combined: $outCombined"
Write-Host "summary : $outSummary"
$summary | Format-Table -AutoSize
