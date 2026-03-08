using System.Text;
using System.Text.Json;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// Coordinator が外側へ公開する運転席制御スナップショット。
    /// 本体UIと外部運転席UIはこの契約だけを読んで状態を描画する。
    /// </summary>
    public sealed class ThumbnailCoordinatorControlSnapshot
    {
        public int SchemaVersion { get; init; } = 1;
        public string MainDbFullPath { get; init; } = "";
        public string DbName { get; init; } = "";
        public string OwnerInstanceId { get; init; } = "";
        public string CoordinatorState { get; init; } = "";
        public int RequestedParallelism { get; init; }
        public int TemporaryParallelismDelta { get; init; }
        public int EffectiveParallelism { get; init; }
        public int LargeMovieThresholdGb { get; init; }
        public bool GpuDecodeEnabled { get; init; }
        public string OperationMode { get; init; } = "";
        public int FastSlotCount { get; init; }
        public int SlowSlotCount { get; init; }
        public int ActiveWorkerCount { get; init; }
        public int ActiveFfmpegCount { get; init; }
        public int QueuedNormalCount { get; init; }
        public int QueuedSlowCount { get; init; }
        public int QueuedRecoveryCount { get; init; }
        public int RunningNormalCount { get; init; }
        public int RunningSlowCount { get; init; }
        public int RunningRecoveryCount { get; init; }
        public int DemandNormalCount { get; init; }
        public int DemandSlowCount { get; init; }
        public int DemandRecoveryCount { get; init; }
        public int WeightedNormalDemand { get; init; }
        public int WeightedSlowDemand { get; init; }
        public int SlowSlotMinimum { get; init; }
        public int SlowSlotMaximum { get; init; }
        public string DecisionCategory { get; init; } = "";
        public string DecisionSummary { get; init; } = "";
        public string Reason { get; init; } = "";
        public IReadOnlyList<ThumbnailCoordinatorDecisionHistoryEntry> DecisionHistory { get; init; } =
            [];
        public DateTime UpdatedAtUtc { get; init; }
    }

    /// <summary>
    /// Coordinator の配分判断を外側 UI へ持ち出す履歴項目。
    /// Viewer を再起動しても直近判断の変遷を追えるようにする。
    /// </summary>
    public sealed class ThumbnailCoordinatorDecisionHistoryEntry
    {
        public DateTime UpdatedAtUtc { get; init; }
        public string OperationMode { get; init; } = "";
        public string DecisionCategory { get; init; } = "";
        public string DecisionSummary { get; init; } = "";
        public int FastSlotCount { get; init; }
        public int SlowSlotCount { get; init; }
    }

    public static class ThumbnailCoordinatorState
    {
        public const string Starting = "starting";
        public const string Running = "running";
        public const string Degraded = "degraded";
        public const string Stopped = "stopped";
        public const string StartFailed = "start-failed";
    }

    public static class ThumbnailCoordinatorStateResolver
    {
        public static string ToDisplayText(string state)
        {
            return (state ?? "").ToLowerInvariant() switch
            {
                ThumbnailCoordinatorState.Starting => "起動中",
                ThumbnailCoordinatorState.Running => "稼働",
                ThumbnailCoordinatorState.Degraded => "縮退",
                ThumbnailCoordinatorState.Stopped => "停止",
                ThumbnailCoordinatorState.StartFailed => "起動失敗",
                _ => "不明",
            };
        }
    }

    public static class ThumbnailCoordinatorOperationMode
    {
        public const string NormalFirst = "normal-first";
        public const string PowerSave = "power-save";
        public const string RecoveryFirst = "recovery-first";
    }

    public static class ThumbnailCoordinatorOperationModeResolver
    {
        public static string ToDisplayText(string mode)
        {
            return (mode ?? "").ToLowerInvariant() switch
            {
                ThumbnailCoordinatorOperationMode.NormalFirst => "通常優先",
                ThumbnailCoordinatorOperationMode.PowerSave => "省電力",
                ThumbnailCoordinatorOperationMode.RecoveryFirst => "回復優先",
                _ => "未設定",
            };
        }
    }

    public static class ThumbnailCoordinatorDecisionCategory
    {
        public const string Steady = "steady";
        public const string Minimum = "minimum";
        public const string DelegationCapped = "delegation-capped";
        public const string RecoveryBiased = "recovery-biased";
        public const string DemandBiased = "demand-biased";
    }

    public static class ThumbnailCoordinatorDecisionCategoryResolver
    {
        public static string ToDisplayText(string category)
        {
            return (category ?? "").ToLowerInvariant() switch
            {
                ThumbnailCoordinatorDecisionCategory.Steady => "維持",
                ThumbnailCoordinatorDecisionCategory.Minimum => "最小維持",
                ThumbnailCoordinatorDecisionCategory.DelegationCapped => "代行余地確保",
                ThumbnailCoordinatorDecisionCategory.RecoveryBiased => "回復優先",
                ThumbnailCoordinatorDecisionCategory.DemandBiased => "需要追従",
                _ => "未分類",
            };
        }
    }

    /// <summary>
    /// Coordinator control snapshot の保存と読取を扱う。
    /// </summary>
    public static class ThumbnailCoordinatorControlStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

        public static void Save(ThumbnailCoordinatorControlSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.OwnerInstanceId))
            {
                return;
            }

            string directoryPath = ThumbnailProgressExternalSnapshotStore.ResolveSnapshotDirectoryPath();
            string safeOwner = ThumbnailProgressExternalSnapshotStore.ToSafeOwnerFileName(
                snapshot.OwnerInstanceId
            );
            string snapshotPath = Path.Combine(directoryPath, $"thumbnail-control-{safeOwner}.json");
            string tempPath = Path.Combine(directoryPath, $"thumbnail-control-{safeOwner}.tmp");

            try
            {
                Directory.CreateDirectory(directoryPath);
                string json = JsonSerializer.Serialize(snapshot, JsonOptions);
                File.WriteAllText(tempPath, json, new UTF8Encoding(false));
                File.Move(tempPath, snapshotPath, true);
            }
            catch
            {
                // control 更新失敗で本処理は止めない。
            }
        }

        public static ThumbnailCoordinatorControlSnapshot LoadLatest(
            string mainDbFullPath,
            string ownerInstanceId,
            TimeSpan maxAge
        )
        {
            if (string.IsNullOrWhiteSpace(ownerInstanceId))
            {
                return null;
            }

            string directoryPath = ThumbnailProgressExternalSnapshotStore.ResolveSnapshotDirectoryPath();
            if (!Directory.Exists(directoryPath))
            {
                return null;
            }

            string safeOwner = ThumbnailProgressExternalSnapshotStore.ToSafeOwnerFileName(ownerInstanceId);
            string snapshotPath = Path.Combine(directoryPath, $"thumbnail-control-{safeOwner}.json");
            if (!File.Exists(snapshotPath))
            {
                return null;
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

                ThumbnailCoordinatorControlSnapshot snapshot =
                    JsonSerializer.Deserialize<ThumbnailCoordinatorControlSnapshot>(json, JsonOptions);
                if (snapshot == null)
                {
                    return null;
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
                    return null;
                }

                if (
                    maxAge > TimeSpan.Zero
                    && snapshot.UpdatedAtUtc > DateTime.MinValue
                    && DateTime.UtcNow - snapshot.UpdatedAtUtc > maxAge
                )
                {
                    return null;
                }

                return snapshot;
            }
            catch
            {
                // 壊れた control は読み飛ばす。
                return null;
            }
        }
    }
}
