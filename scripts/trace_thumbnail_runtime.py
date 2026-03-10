import argparse
import datetime as dt
import hashlib
import json
import os
import sqlite3
import subprocess
import time
from pathlib import Path


TARGET_NAMES = {
    "IndigoMovieManager_fork.exe",
    "IndigoMovieManager.Thumbnail.Coordinator.exe",
    "IndigoMovieManager.Thumbnail.Worker.exe",
    "ffmpeg.exe",
}


def now_text() -> str:
    return dt.datetime.now().strftime("%Y-%m-%d %H:%M:%S")


def resolve_queue_db_path(main_db_full_path: str) -> Path:
    normalized = main_db_full_path.strip().strip('"').replace("/", "\\").lower()
    hash8 = hashlib.sha256(normalized.encode("utf-8")).hexdigest()[:8].upper()
    db_name = Path(main_db_full_path).stem or "main"
    local_appdata = Path(os.environ["LOCALAPPDATA"])
    return local_appdata / "IndigoMovieManager_fork" / "QueueDb" / f"{db_name}.{hash8}.queue.imm"


def run_powershell_json(script: str) -> list[dict]:
    completed = subprocess.run(
        ["pwsh", "-NoLogo", "-Command", script],
        capture_output=True,
        text=True,
        encoding="utf-8",
        check=False,
    )
    if completed.returncode != 0:
        return []
    text = completed.stdout.strip()
    if not text:
        return []
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        return []


def get_process_rows() -> list[dict]:
    # PowerShell側で必要項目だけ JSON 化し、親子関係と CPU 時間を採る。
    script = r"""
$targetNames = @(
  'IndigoMovieManager_fork.exe',
  'IndigoMovieManager.Thumbnail.Coordinator.exe',
  'IndigoMovieManager.Thumbnail.Worker.exe',
  'ffmpeg.exe'
)
$cim = Get-CimInstance Win32_Process | Where-Object { $targetNames -contains $_.Name }
$procs = Get-Process -ErrorAction SilentlyContinue | Where-Object { $targetNames -contains ($_.ProcessName + '.exe') }
$procById = @{}
foreach ($p in $procs) { $procById[$p.Id] = $p }
$rows = foreach ($c in $cim) {
  $p = $null
  if ($procById.ContainsKey([int]$c.ProcessId)) { $p = $procById[[int]$c.ProcessId] }
  [pscustomobject]@{
    pid = [int]$c.ProcessId
    parent_pid = [int]$c.ParentProcessId
    name = $c.Name
    command_line = if ($null -eq $c.CommandLine) { '' } else { $c.CommandLine }
    cpu = if ($null -eq $p) { 0.0 } else { [double]$p.CPU }
    threads = if ($null -eq $p) { 0 } else { [int]$p.Threads.Count }
    ws_mb = if ($null -eq $p) { 0.0 } else { [math]::Round($p.WorkingSet64 / 1MB, 1) }
    pm_mb = if ($null -eq $p) { 0.0 } else { [math]::Round($p.PagedMemorySize64 / 1MB, 1) }
    start_time = if ($null -eq $p) { '' } else { $p.StartTime.ToString('yyyy-MM-dd HH:mm:ss') }
  }
}
$rows | ConvertTo-Json -Depth 4 -Compress
"""
    rows = run_powershell_json(script)
    if isinstance(rows, dict):
        return [rows]
    return rows


def find_latest_json(progress_dir: Path, prefix: str, main_db_full_path: str) -> dict | None:
    candidates = sorted(progress_dir.glob(f"{prefix}*.json"), key=lambda p: p.stat().st_mtime, reverse=True)
    for path in candidates:
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except Exception:
            continue
        if data.get("MainDbFullPath", "") == main_db_full_path:
            data["_path"] = str(path)
            return data
    return None


def query_queue_db(queue_db_path: Path) -> dict:
    result = {
        "exists": queue_db_path.exists(),
        "status_counts": [],
        "queued_total": 0,
        "leased_total": 0,
        "running_total": 0,
        "hang_suspected_total": 0,
        "leased_owners": [],
        "running_owners": [],
        "oldest_leased": [],
        "oldest_running": [],
    }
    if not queue_db_path.exists():
        return result

    con = sqlite3.connect(queue_db_path)
    try:
        cur = con.cursor()
        columns = {
            str(row[1]).lower()
            for row in cur.execute("pragma table_info('ThumbnailQueue')").fetchall()
        }
        result["status_counts"] = cur.execute(
            "select Status, count(*) from ThumbnailQueue group by Status order by Status"
        ).fetchall()

        has_started = "startedatutc" in columns
        has_last_error = "lasterror" in columns

        result["queued_total"] = int(
            cur.execute("select count(*) from ThumbnailQueue where Status=0").fetchone()[0]
        )
        if has_started:
            result["leased_total"] = int(
                cur.execute(
                    "select count(*) from ThumbnailQueue where Status=1 and ifnull(StartedAtUtc, '') = ''"
                ).fetchone()[0]
            )
            result["running_total"] = int(
                cur.execute(
                    "select count(*) from ThumbnailQueue where Status=1 and ifnull(StartedAtUtc, '') <> ''"
                ).fetchone()[0]
            )
            result["leased_owners"] = cur.execute(
                """
                select OwnerInstanceId, count(*)
                from ThumbnailQueue
                where Status=1 and ifnull(StartedAtUtc, '') = ''
                group by OwnerInstanceId
                order by count(*) desc
                """
            ).fetchall()
            result["running_owners"] = cur.execute(
                """
                select OwnerInstanceId, count(*)
                from ThumbnailQueue
                where Status=1 and ifnull(StartedAtUtc, '') <> ''
                group by OwnerInstanceId
                order by count(*) desc
                """
            ).fetchall()
            result["oldest_leased"] = cur.execute(
                """
                select MoviePath, TabIndex, AttemptCount, OwnerInstanceId, LeaseUntilUtc, StartedAtUtc, UpdatedAtUtc
                from ThumbnailQueue
                where Status=1 and ifnull(StartedAtUtc, '') = ''
                order by UpdatedAtUtc asc
                limit 5
                """
            ).fetchall()
            result["oldest_running"] = cur.execute(
                """
                select MoviePath, TabIndex, AttemptCount, OwnerInstanceId, LeaseUntilUtc, StartedAtUtc, UpdatedAtUtc
                from ThumbnailQueue
                where Status=1 and ifnull(StartedAtUtc, '') <> ''
                order by StartedAtUtc asc, UpdatedAtUtc asc
                limit 5
                """
            ).fetchall()
        else:
            processing_rows = cur.execute(
                """
                select MoviePath, TabIndex, AttemptCount, OwnerInstanceId, LeaseUntilUtc, UpdatedAtUtc
                from ThumbnailQueue
                where Status=1
                order by UpdatedAtUtc asc
                limit 5
                """
            ).fetchall()
            processing_owners = cur.execute(
                "select OwnerInstanceId, count(*) from ThumbnailQueue where Status=1 group by OwnerInstanceId order by count(*) desc"
            ).fetchall()
            result["leased_total"] = int(
                cur.execute("select count(*) from ThumbnailQueue where Status=1").fetchone()[0]
            )
            result["leased_owners"] = processing_owners
            result["oldest_leased"] = processing_rows

        if has_last_error:
            result["hang_suspected_total"] = int(
                cur.execute(
                    """
                    select count(*)
                    from ThumbnailQueue
                    where Status <> 2
                      and (
                        lower(ifnull(LastError, '')) like '%timeout%'
                        or lower(ifnull(LastError, '')) like '%timed out%'
                        or lower(ifnull(LastError, '')) like '%hang%'
                      )
                    """
                ).fetchone()[0]
            )
        return result
    finally:
        con.close()


def write_line(handle, text: str) -> None:
    handle.write(text + "\n")
    handle.flush()
    safe_text = text.encode("cp932", errors="backslashreplace").decode("cp932", errors="ignore")
    print(safe_text)


def main() -> int:
    parser = argparse.ArgumentParser(description="サムネイル runtime の実行状態を時系列で追跡する。")
    parser.add_argument("--main-db", required=True, help="対象の main DB フルパス")
    parser.add_argument("--duration", type=int, default=60, help="追跡秒数")
    parser.add_argument("--interval", type=int, default=2, help="採取間隔秒")
    parser.add_argument("--output", default="", help="出力ログパス")
    args = parser.parse_args()

    if args.duration < 1 or args.interval < 1:
        raise SystemExit("duration / interval は 1 以上で指定してください。")

    local_appdata = Path(os.environ["LOCALAPPDATA"])
    progress_dir = local_appdata / "IndigoMovieManager_fork" / "progress"
    log_dir = local_appdata / "IndigoMovieManager_fork" / "logs"
    log_dir.mkdir(parents=True, exist_ok=True)

    output_path = Path(args.output) if args.output else log_dir / f"thumbnail-runtime-trace-{dt.datetime.now():%Y%m%d_%H%M%S}.log"
    queue_db_path = resolve_queue_db_path(args.main_db)

    previous_cpu_by_pid: dict[int, float] = {}
    stagnant_by_pid: dict[int, int] = {}
    sample_count = max(1, (args.duration + args.interval - 1) // args.interval)

    with output_path.open("w", encoding="utf-8", newline="\n") as handle:
        write_line(handle, "# thumbnail runtime trace")
        write_line(handle, f"# started_at={now_text()}")
        write_line(handle, f"# main_db={args.main_db}")
        write_line(handle, f"# queue_db={queue_db_path}")
        write_line(handle, f"# duration_sec={args.duration} interval_sec={args.interval}")

        for sample_index in range(1, sample_count + 1):
            write_line(handle, "")
            write_line(handle, f"## sample={sample_index} at={now_text()}")

            process_rows = sorted(get_process_rows(), key=lambda row: row.get("pid", 0))
            if not process_rows:
                write_line(handle, "processes: none")
            for row in process_rows:
                pid = int(row.get("pid", 0))
                cpu_total = float(row.get("cpu", 0.0))
                cpu_delta = cpu_total - previous_cpu_by_pid.get(pid, cpu_total)
                previous_cpu_by_pid[pid] = cpu_total
                stagnant_by_pid[pid] = stagnant_by_pid.get(pid, 0) + 1 if abs(cpu_delta) < 0.01 else 0
                flags: list[str] = []
                if row.get("name") in ("IndigoMovieManager.Thumbnail.Worker.exe", "ffmpeg.exe") and stagnant_by_pid[pid] >= 3:
                    flags.append("stagnant")
                flag_text = f" flags={','.join(flags)}" if flags else ""
                write_line(
                    handle,
                    "proc pid={pid} parent={parent} name={name} cpu_total={cpu:.2f}s cpu_delta={delta:.2f}s stagnant={stagnant} threads={threads} ws_mb={ws} pm_mb={pm}{flag}".format(
                        pid=pid,
                        parent=row.get("parent_pid", 0),
                        name=row.get("name", ""),
                        cpu=cpu_total,
                        delta=cpu_delta,
                        stagnant=stagnant_by_pid[pid],
                        threads=row.get("threads", 0),
                        ws=row.get("ws_mb", 0.0),
                        pm=row.get("pm_mb", 0.0),
                        flag=flag_text,
                    ),
                )

            control = find_latest_json(progress_dir, "thumbnail-control-", args.main_db)
            if control:
                write_line(
                    handle,
                    "control state={state} updated={updated} q={qn}/{qs}/{qr} lease={ln}/{ls}/{lr} run={rn}/{rs}/{rr} hang={hang} fast={fast} slow={slow}".format(
                        state=control.get("CoordinatorState", ""),
                        updated=control.get("UpdatedAtUtc", ""),
                        qn=control.get("QueuedNormalCount", 0),
                        qs=control.get("QueuedSlowCount", 0),
                        qr=control.get("QueuedRecoveryCount", 0),
                        ln=control.get("LeasedNormalCount", 0),
                        ls=control.get("LeasedSlowCount", 0),
                        lr=control.get("LeasedRecoveryCount", 0),
                        rn=control.get("RunningNormalCount", 0),
                        rs=control.get("RunningSlowCount", 0),
                        rr=control.get("RunningRecoveryCount", 0),
                        hang=control.get("HangSuspectedCount", 0),
                        fast=control.get("FastSlotCount", 0),
                        slow=control.get("SlowSlotCount", 0),
                    ),
                )
            else:
                write_line(handle, "control state=missing")

            for prefix in ("thumbnail-health-thumb-normal", "thumbnail-health-thumb-idle"):
                health = find_latest_json(progress_dir, prefix, args.main_db)
                if health:
                    write_line(
                        handle,
                        "health role={role} state={state} updated={updated} heartbeat={heartbeat} pid={pid}".format(
                            role=health.get("WorkerRole", ""),
                            state=health.get("State", ""),
                            updated=health.get("UpdatedAtUtc", ""),
                            heartbeat=health.get("LastHeartbeatUtc", ""),
                            pid=health.get("ProcessId", 0),
                        ),
                    )

            queue_info = query_queue_db(queue_db_path)
            if not queue_info["exists"]:
                write_line(handle, "queue_db missing")
            else:
                write_line(handle, f"queue status_counts={queue_info['status_counts']}")
                write_line(
                    handle,
                    "queue totals queued={queued} leased={leased} running={running} hang={hang}".format(
                        queued=queue_info["queued_total"],
                        leased=queue_info["leased_total"],
                        running=queue_info["running_total"],
                        hang=queue_info["hang_suspected_total"],
                    ),
                )
                write_line(handle, f"queue leased_owners={queue_info['leased_owners']}")
                write_line(handle, f"queue running_owners={queue_info['running_owners']}")
                for row in queue_info["oldest_leased"]:
                    write_line(handle, f"queue oldest_leased={row}")
                for row in queue_info["oldest_running"]:
                    write_line(handle, f"queue oldest_running={row}")

            if sample_index < sample_count:
                time.sleep(args.interval)

        write_line(handle, "")
        write_line(handle, f"# finished_at={now_text()}")
        write_line(handle, f"# saved={output_path}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
