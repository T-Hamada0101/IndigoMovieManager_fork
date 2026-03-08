using System.Text;
using System.Text.Json;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// Worker の health 状態を外部へ出す契約。
    /// viewer と UI はこの状態だけを読んで稼働可否を判断する。
    /// </summary>
    public sealed class ThumbnailWorkerHealthSnapshot
    {
        public int SchemaVersion { get; init; } = 1;
        public string MainDbFullPath { get; init; } = "";
        public string OwnerInstanceId { get; init; } = "";
        public string WorkerRole { get; init; } = "";
        public string State { get; init; } = "";
        public string ReasonCode { get; init; } = "";
        public string SettingsVersionToken { get; init; } = "";
        public string CurrentPriority { get; init; } = "";
        public string Message { get; init; } = "";
        public int ProcessId { get; init; }
        public int? ExitCode { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
        public DateTime? LastHeartbeatUtc { get; init; }
    }

    public static class ThumbnailWorkerHealthState
    {
        public const string Starting = "starting";
        public const string Running = "running";
        public const string Stopped = "stopped";
        public const string Exited = "exited";
        public const string StartFailed = "start-failed";
        public const string Missing = "missing";
    }

    public static class ThumbnailWorkerHealthReasonCode
    {
        public const string None = "";
        public const string WorkerMissing = "worker-missing";
        public const string ProcessStartFailed = "process-start-failed";
        public const string DbMismatch = "db-mismatch";
        public const string DllMissing = "dll-missing";
        public const string Exception = "exception";
        public const string GracefulStop = "graceful-stop";
        public const string Canceled = "canceled";
    }

    public static class ThumbnailWorkerHealthReasonResolver
    {
        public static string Resolve(string state, Exception ex)
        {
            if (ex == null)
            {
                return Resolve(state, "");
            }

            if (ex is DllNotFoundException)
            {
                return ThumbnailWorkerHealthReasonCode.DllMissing;
            }

            if (
                ex is InvalidOperationException
                && (ex.Message ?? "").IndexOf("db mismatch", StringComparison.OrdinalIgnoreCase) >= 0
            )
            {
                return ThumbnailWorkerHealthReasonCode.DbMismatch;
            }

            return ThumbnailWorkerHealthReasonCode.Exception;
        }

        public static string Resolve(string state, string message)
        {
            string safeMessage = message ?? "";

            return (state ?? "").ToLowerInvariant() switch
            {
                ThumbnailWorkerHealthState.Missing => ThumbnailWorkerHealthReasonCode.WorkerMissing,
                ThumbnailWorkerHealthState.StartFailed => ThumbnailWorkerHealthReasonCode.ProcessStartFailed,
                ThumbnailWorkerHealthState.Stopped when safeMessage.IndexOf("gracefully", StringComparison.OrdinalIgnoreCase) >= 0
                    => ThumbnailWorkerHealthReasonCode.GracefulStop,
                ThumbnailWorkerHealthState.Stopped when safeMessage.IndexOf("canceled", StringComparison.OrdinalIgnoreCase) >= 0
                    => ThumbnailWorkerHealthReasonCode.Canceled,
                _ => ThumbnailWorkerHealthReasonCode.None,
            };
        }

        public static string ToDisplayText(string reasonCode)
        {
            return (reasonCode ?? "").ToLowerInvariant() switch
            {
                ThumbnailWorkerHealthReasonCode.WorkerMissing => "exe不足",
                ThumbnailWorkerHealthReasonCode.ProcessStartFailed => "起動失敗",
                ThumbnailWorkerHealthReasonCode.DbMismatch => "DB不一致",
                ThumbnailWorkerHealthReasonCode.DllMissing => "DLL不足",
                ThumbnailWorkerHealthReasonCode.Exception => "例外終了",
                ThumbnailWorkerHealthReasonCode.GracefulStop => "正常停止",
                ThumbnailWorkerHealthReasonCode.Canceled => "停止要求",
                _ => "",
            };
        }
    }

    /// <summary>
    /// Worker health snapshot の保存と読取を扱う。
    /// </summary>
    public static class ThumbnailWorkerHealthStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

        public static void Save(ThumbnailWorkerHealthSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.OwnerInstanceId))
            {
                return;
            }

            string directoryPath = ThumbnailProgressExternalSnapshotStore.ResolveSnapshotDirectoryPath();
            string safeOwner = ThumbnailProgressExternalSnapshotStore.ToSafeOwnerFileName(
                snapshot.OwnerInstanceId
            );
            string snapshotPath = Path.Combine(directoryPath, $"thumbnail-health-{safeOwner}.json");
            string tempPath = Path.Combine(directoryPath, $"thumbnail-health-{safeOwner}.tmp");

            try
            {
                Directory.CreateDirectory(directoryPath);
                string json = JsonSerializer.Serialize(snapshot, JsonOptions);
                File.WriteAllText(tempPath, json, new UTF8Encoding(false));
                File.Move(tempPath, snapshotPath, true);
            }
            catch
            {
                // health 更新失敗で本処理は止めない。
            }
        }

        public static IReadOnlyList<ThumbnailWorkerHealthSnapshot> LoadSnapshots(
            string mainDbFullPath,
            IReadOnlyList<string> allowedOwnerInstanceIds,
            TimeSpan maxAge
        )
        {
            string directoryPath = ThumbnailProgressExternalSnapshotStore.ResolveSnapshotDirectoryPath();
            if (!Directory.Exists(directoryPath))
            {
                return [];
            }

            HashSet<string> allowedOwners = new(
                (allowedOwnerInstanceIds ?? [])
                    .Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.Ordinal
            );
            if (allowedOwners.Count < 1)
            {
                return [];
            }

            DateTime nowUtc = DateTime.UtcNow;
            List<ThumbnailWorkerHealthSnapshot> snapshots = [];
            foreach (string ownerInstanceId in allowedOwners)
            {
                string safeOwner = ThumbnailProgressExternalSnapshotStore.ToSafeOwnerFileName(
                    ownerInstanceId
                );
                string snapshotPath = Path.Combine(directoryPath, $"thumbnail-health-{safeOwner}.json");
                if (!File.Exists(snapshotPath))
                {
                    continue;
                }

                try
                {
                    string json;
                    using (
                        FileStream stream = new(
                            snapshotPath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete
                        )
                    )
                    using (StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                    {
                        json = reader.ReadToEnd();
                    }

                    ThumbnailWorkerHealthSnapshot snapshot = JsonSerializer.Deserialize<ThumbnailWorkerHealthSnapshot>(
                        json,
                        JsonOptions
                    );
                    if (snapshot == null)
                    {
                        continue;
                    }

                    if (
                        !string.IsNullOrWhiteSpace(mainDbFullPath)
                        && !string.Equals(
                            snapshot.MainDbFullPath,
                            mainDbFullPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        continue;
                    }

                    if (snapshot.UpdatedAtUtc > DateTime.MinValue && nowUtc - snapshot.UpdatedAtUtc > maxAge)
                    {
                        continue;
                    }

                    snapshots.Add(snapshot);
                }
                catch
                {
                    // 壊れた health は読み飛ばして継続する。
                }
            }

            return snapshots
                .OrderBy(x => ResolveOwnerSortOrder(allowedOwnerInstanceIds, x.OwnerInstanceId))
                .ToArray();
        }

        private static int ResolveOwnerSortOrder(
            IReadOnlyList<string> allowedOwnerInstanceIds,
            string ownerInstanceId
        )
        {
            if (allowedOwnerInstanceIds == null || string.IsNullOrWhiteSpace(ownerInstanceId))
            {
                return int.MaxValue;
            }

            for (int i = 0; i < allowedOwnerInstanceIds.Count; i++)
            {
                if (string.Equals(allowedOwnerInstanceIds[i], ownerInstanceId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return int.MaxValue;
        }
    }
}
