param(
    [int]$DurationSeconds = 60,
    [int]$IntervalSeconds = 2,
    [string]$OutputPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($DurationSeconds -lt 1) {
    throw "DurationSeconds は 1 以上で指定してください。"
}

if ($IntervalSeconds -lt 1) {
    throw "IntervalSeconds は 1 以上で指定してください。"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $logDir = Join-Path $env:LOCALAPPDATA "IndigoMovieManager_fork\logs"
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $OutputPath = Join-Path $logDir "thumbnail-process-trace-$timestamp.log"
}

$targetNames = @(
    "IndigoMovieManager_fork",
    "IndigoMovieManager.Thumbnail.Coordinator",
    "IndigoMovieManager.Thumbnail.Worker",
    "ffmpeg"
)

$previousCpuByPid = @{}
$stagnantCountByPid = @{}
$sampleCount = [Math]::Max(1, [int][Math]::Ceiling($DurationSeconds / $IntervalSeconds))

function Write-TraceLine {
    param(
        [string]$Text
    )

    $Text | Out-File -FilePath $OutputPath -Encoding utf8 -Append
    Write-Host $Text
}

function Get-TargetCimProcesses {
    $all = Get-CimInstance Win32_Process
    return $all | Where-Object {
        $name = [System.IO.Path]::GetFileNameWithoutExtension($_.Name)
        $targetNames -contains $name
    }
}

function Get-TargetProcesses {
    return Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $targetNames -contains $_.ProcessName
    }
}

Write-TraceLine "# thumbnail process trace"
Write-TraceLine "# started_at=$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-TraceLine "# duration_sec=$DurationSeconds interval_sec=$IntervalSeconds"
Write-TraceLine "# output=$OutputPath"

for ($sampleIndex = 1; $sampleIndex -le $sampleCount; $sampleIndex++) {
    $sampleAt = Get-Date
    $cimList = @(Get-TargetCimProcesses)
    $procList = @(Get-TargetProcesses)
    $cimByPid = @{}
    $procByPid = @{}

    foreach ($cim in $cimList) {
        $cimByPid[[int]$cim.ProcessId] = $cim
    }

    foreach ($proc in $procList) {
        $procByPid[[int]$proc.Id] = $proc
    }

    Write-TraceLine ""
    Write-TraceLine "## sample=$sampleIndex at=$($sampleAt.ToString('yyyy-MM-dd HH:mm:ss'))"

    if ($procByPid.Count -lt 1) {
        Write-TraceLine "no target processes"
    }

    foreach ($processId in ($procByPid.Keys | Sort-Object)) {
        $proc = $procByPid[$processId]
        $cim = $null
        if ($cimByPid.ContainsKey($processId)) {
            $cim = $cimByPid[$processId]
        }

        $cpuTotal = [double]($proc.CPU ?? 0.0)
        $cpuDelta = 0.0
        if ($previousCpuByPid.ContainsKey($processId)) {
            $cpuDelta = $cpuTotal - [double]$previousCpuByPid[$processId]
        }
        $previousCpuByPid[$processId] = $cpuTotal

        if (-not $stagnantCountByPid.ContainsKey($processId)) {
            $stagnantCountByPid[$processId] = 0
        }

        if ([Math]::Abs($cpuDelta) -lt 0.01) {
            $stagnantCountByPid[$processId] = [int]$stagnantCountByPid[$processId] + 1
        }
        else {
            $stagnantCountByPid[$processId] = 0
        }

        $parentPid = if ($null -ne $cim) { [int]$cim.ParentProcessId } else { -1 }
        $childCount = @($cimList | Where-Object { [int]$_.ParentProcessId -eq $processId }).Count
        $commandLine = if ($null -ne $cim) { ($cim.CommandLine ?? "") } else { "" }
        if ($commandLine.Length -gt 180) {
            $commandLine = $commandLine.Substring(0, 180) + "..."
        }

        $flags = @()
        if ($proc.ProcessName -eq "IndigoMovieManager.Thumbnail.Worker" -and $stagnantCountByPid[$processId] -ge 3) {
            $flags += "worker-stagnant"
        }
        if ($proc.ProcessName -eq "ffmpeg" -and $stagnantCountByPid[$processId] -ge 3) {
            $flags += "ffmpeg-stagnant"
        }
        if ($proc.ProcessName -eq "IndigoMovieManager.Thumbnail.Coordinator" -and $stagnantCountByPid[$processId] -ge 3) {
            $flags += "coordinator-stagnant"
        }

        $flagText = if ($flags.Count -gt 0) { " flags=" + ($flags -join ",") } else { "" }

        Write-TraceLine (
            "pid={0} parent={1} name={2} cpu_total={3:N2}s cpu_delta={4:N2}s stagnant={5} threads={6} ws_mb={7:N1} pm_mb={8:N1} child={9} responding={10}{11}" -f
            $processId,
            $parentPid,
            $proc.ProcessName,
            $cpuTotal,
            $cpuDelta,
            $stagnantCountByPid[$processId],
            $proc.Threads.Count,
            ($proc.WorkingSet64 / 1MB),
            ($proc.PagedMemorySize64 / 1MB),
            $childCount,
            $proc.Responding,
            $flagText
        )

        if (-not [string]::IsNullOrWhiteSpace($commandLine)) {
            Write-TraceLine ("  cmd=" + $commandLine)
        }
    }

    if ($sampleIndex -lt $sampleCount) {
        Start-Sleep -Seconds $IntervalSeconds
    }
}

Write-TraceLine ""
Write-TraceLine "# finished_at=$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-TraceLine "# saved=$OutputPath"
