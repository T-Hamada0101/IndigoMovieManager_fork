using System.Text;
using System.Text.Json;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// Worker別の進捗スナップショットをファイルへ逃がし、UIが後から読むための保存口。
    /// </summary>
    public sealed class ThumbnailProgressExternalSnapshotPublisher : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
        private static readonly TimeSpan MinimumWriteInterval = TimeSpan.FromMilliseconds(150);

        private readonly object writeLock = new();
        private readonly string mainDbFullPath;
        private readonly string ownerInstanceId;
        private readonly string snapshotFilePath;
        private readonly string tempFilePath;
        private long lastPublishedVersion = -1;
        private DateTime lastWrittenAtUtc = DateTime.MinValue;
        private bool disposed;

        public ThumbnailProgressExternalSnapshotPublisher(
            string mainDbFullPath,
            string ownerInstanceId
        )
        {
            this.mainDbFullPath = mainDbFullPath ?? "";
            this.ownerInstanceId = ownerInstanceId ?? "";

            string safeOwner = ThumbnailProgressExternalSnapshotStore.ToSafeOwnerFileName(
                ownerInstanceId
            );
            string directoryPath =
                ThumbnailProgressExternalSnapshotStore.ResolveSnapshotDirectoryPath();
            snapshotFilePath = Path.Combine(directoryPath, $"thumbnail-progress-{safeOwner}.json");
            tempFilePath = Path.Combine(
                directoryPath,
                $"thumbnail-progress-{safeOwner}.tmp"
            );
        }

        // 高頻度イベントでも書き込みを少し束ねつつ、最新状態は必ずファイルへ流す。
        public void Publish(ThumbnailProgressRuntimeSnapshot snapshot, bool force = false)
        {
            if (snapshot == null || disposed)
            {
                return;
            }

            lock (writeLock)
            {
                DateTime nowUtc = DateTime.UtcNow;
                if (
                    !force
                    && snapshot.Version == lastPublishedVersion
                    && nowUtc - lastWrittenAtUtc < MinimumWriteInterval
                )
                {
                    return;
                }

                ThumbnailProgressExternalSnapshotEnvelope envelope = new()
                {
                    SchemaVersion = ThumbnailProgressRuntime.ProgressSnapshotSchemaVersion,
                    MainDbFullPath = mainDbFullPath,
                    OwnerInstanceId = ownerInstanceId,
                    UpdatedAtUtc = nowUtc,
                    Snapshot = NormalizeSnapshot(snapshot, mainDbFullPath, ownerInstanceId, nowUtc),
                };
                string json = JsonSerializer.Serialize(envelope, JsonOptions);

                try
                {
                    Directory.CreateDirectory(
                        ThumbnailProgressExternalSnapshotStore.ResolveSnapshotDirectoryPath()
                    );
                    File.WriteAllText(tempFilePath, json, new UTF8Encoding(false));
                    File.Move(tempFilePath, snapshotFilePath, true);

                    lastPublishedVersion = snapshot.Version;
                    lastWrittenAtUtc = nowUtc;
                }
                catch
                {
                    // 運悪くUIが読み込み中でMoveが弾かれた場合は、次回スナップショットの送信に任せてワーカーを止めない。
                }
            }
        }

        public void Dispose()
        {
            lock (writeLock)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                TryDelete(tempFilePath);
                TryDelete(snapshotFilePath);
            }
        }

        private static ThumbnailProgressRuntimeSnapshot NormalizeSnapshot(
            ThumbnailProgressRuntimeSnapshot snapshot,
            string mainDbFullPath,
            string ownerInstanceId,
            DateTime updatedAtUtc
        )
        {
            ThumbnailProgressRuntimeSnapshot safeSnapshot = snapshot ?? new();
            IReadOnlyList<ThumbnailProgressWorkerSnapshot> normalizedWorkers =
            [
                .. (safeSnapshot.ActiveWorkers ?? []).Select(
                    worker =>
                        new ThumbnailProgressWorkerSnapshot
                        {
                            WorkerId = worker.WorkerId,
                            WorkerLabel = worker.WorkerLabel,
                            WorkerRole = worker.WorkerRole,
                            State = string.IsNullOrWhiteSpace(worker.State)
                                ? (
                                    worker.IsActive
                                        ? ThumbnailProgressSnapshotState.Started
                                        : ThumbnailProgressSnapshotState.Completed
                                )
                                : worker.State,
                            MovieId = worker.MovieId,
                            TabIndex = worker.TabIndex,
                            MainDbFullPath = string.IsNullOrWhiteSpace(worker.MainDbFullPath)
                                ? mainDbFullPath ?? ""
                                : worker.MainDbFullPath,
                            OwnerInstanceId = string.IsNullOrWhiteSpace(worker.OwnerInstanceId)
                                ? ownerInstanceId ?? ""
                                : worker.OwnerInstanceId,
                            DisplayMovieName = worker.DisplayMovieName,
                            MovieFullPath = worker.MovieFullPath,
                            PreviewImagePath = worker.PreviewImagePath,
                            PreviewCacheKey = worker.PreviewCacheKey,
                            PreviewRevision = worker.PreviewRevision,
                            IsActive = worker.IsActive,
                            UpdatedAtUtc = worker.UpdatedAtUtc > DateTime.MinValue
                                ? worker.UpdatedAtUtc
                                : updatedAtUtc,
                        }
                ),
            ];
            IReadOnlyList<ThumbnailProgressWorkerSnapshot> normalizedWaitingWorkers =
            [
                .. (safeSnapshot.WaitingWorkers ?? []).Select(
                    worker =>
                        new ThumbnailProgressWorkerSnapshot
                        {
                            WorkerId = worker.WorkerId,
                            WorkerLabel = worker.WorkerLabel,
                            WorkerRole = worker.WorkerRole,
                            State = string.IsNullOrWhiteSpace(worker.State)
                                ? ThumbnailProgressSnapshotState.Waiting
                                : worker.State,
                            MovieId = worker.MovieId,
                            TabIndex = worker.TabIndex,
                            MainDbFullPath = string.IsNullOrWhiteSpace(worker.MainDbFullPath)
                                ? mainDbFullPath ?? ""
                                : worker.MainDbFullPath,
                            OwnerInstanceId = string.IsNullOrWhiteSpace(worker.OwnerInstanceId)
                                ? ownerInstanceId ?? ""
                                : worker.OwnerInstanceId,
                            DisplayMovieName = worker.DisplayMovieName,
                            MovieFullPath = worker.MovieFullPath,
                            PreviewImagePath = worker.PreviewImagePath,
                            PreviewCacheKey = worker.PreviewCacheKey,
                            PreviewRevision = worker.PreviewRevision,
                            IsActive = false,
                            UpdatedAtUtc = worker.UpdatedAtUtc > DateTime.MinValue
                                ? worker.UpdatedAtUtc
                                : updatedAtUtc,
                        }
                ),
            ];

            return new ThumbnailProgressRuntimeSnapshot
            {
                SchemaVersion = safeSnapshot.SchemaVersion > 0
                    ? safeSnapshot.SchemaVersion
                    : ThumbnailProgressRuntime.ProgressSnapshotSchemaVersion,
                Version = safeSnapshot.Version,
                SessionCompletedCount = safeSnapshot.SessionCompletedCount,
                SessionTotalCount = safeSnapshot.SessionTotalCount,
                SessionCreatedThumbnailCount = safeSnapshot.SessionCreatedThumbnailCount,
                LeasedCount = safeSnapshot.LeasedCount,
                RunningCount = safeSnapshot.RunningCount,
                HangSuspectedCount = safeSnapshot.HangSuspectedCount,
                CurrentParallelism = safeSnapshot.CurrentParallelism,
                ConfiguredParallelism = safeSnapshot.ConfiguredParallelism,
                EnqueueLogs = safeSnapshot.EnqueueLogs ?? [],
                ActiveWorkers = normalizedWorkers,
                WaitingWorkers = normalizedWaitingWorkers,
            };
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 終了時の掃除失敗で本体処理は止めない。
            }
        }
    }

    /// <summary>
    /// UIから Worker 別の進捗ファイルを読み、表示用スナップショットへ統合する。
    /// </summary>
    public static class ThumbnailProgressExternalSnapshotStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new();

        public static ThumbnailProgressRuntimeSnapshot CreateMergedSnapshot(
            string mainDbFullPath,
            ThumbnailProgressRuntimeSnapshot localSnapshot,
            IReadOnlyList<string> allowedOwnerInstanceIds,
            TimeSpan maxAge
        )
        {
            ThumbnailProgressRuntimeSnapshot safeLocalSnapshot = localSnapshot ?? new();
            if (string.IsNullOrWhiteSpace(mainDbFullPath))
            {
                return safeLocalSnapshot;
            }

            List<ThumbnailProgressExternalSnapshotEnvelope> envelopes = LoadEnvelopes(
                mainDbFullPath,
                allowedOwnerInstanceIds,
                maxAge
            );
            if (envelopes.Count < 1)
            {
                return safeLocalSnapshot;
            }

            int sessionCompletedCount = 0;
            int sessionTotalCount = 0;
            int sessionCreatedThumbnailCount = Math.Max(
                0,
                safeLocalSnapshot.SessionCreatedThumbnailCount
            );
            int leasedCount = Math.Max(0, safeLocalSnapshot.LeasedCount);
            int runningCount = Math.Max(0, safeLocalSnapshot.RunningCount);
            int hangSuspectedCount = Math.Max(0, safeLocalSnapshot.HangSuspectedCount);
            int currentParallelism = 0;
            int configuredParallelism = 0;
            List<ThumbnailProgressWorkerSnapshot> mergedWorkers = [];
            List<ThumbnailProgressWorkerSnapshot> mergedWaitingWorkers = [];

            foreach (
                ThumbnailProgressExternalSnapshotEnvelope envelope in envelopes.OrderBy(x =>
                    ResolveOwnerSortOrder(allowedOwnerInstanceIds, x.OwnerInstanceId)
                )
            )
            {
                ThumbnailProgressRuntimeSnapshot snapshot = envelope.Snapshot ?? new();
                sessionCompletedCount += Math.Max(0, snapshot.SessionCompletedCount);
                sessionTotalCount += Math.Max(0, snapshot.SessionTotalCount);
                sessionCreatedThumbnailCount = Math.Max(
                    sessionCreatedThumbnailCount,
                    Math.Max(0, snapshot.SessionCreatedThumbnailCount)
                );
                leasedCount += Math.Max(0, snapshot.LeasedCount);
                runningCount += Math.Max(0, snapshot.RunningCount);
                hangSuspectedCount += Math.Max(0, snapshot.HangSuspectedCount);
                currentParallelism += Math.Max(0, snapshot.CurrentParallelism);
                configuredParallelism += Math.Max(0, snapshot.ConfiguredParallelism);
                mergedWorkers.AddRange(snapshot.ActiveWorkers ?? []);
                mergedWaitingWorkers.AddRange(snapshot.WaitingWorkers ?? []);
            }

            int localConfiguredParallelism = Math.Max(0, safeLocalSnapshot.ConfiguredParallelism);
            configuredParallelism = localConfiguredParallelism > 0
                ? localConfiguredParallelism
                : configuredParallelism;

            return new ThumbnailProgressRuntimeSnapshot
            {
                SchemaVersion = envelopes.Max(x => x.SchemaVersion),
                Version = ComputeMergedVersion(safeLocalSnapshot, envelopes),
                SessionCompletedCount = sessionCompletedCount,
                SessionTotalCount = Math.Max(sessionCompletedCount, sessionTotalCount),
                SessionCreatedThumbnailCount = sessionCreatedThumbnailCount,
                LeasedCount = leasedCount,
                RunningCount = runningCount,
                HangSuspectedCount = hangSuspectedCount,
                CurrentParallelism = currentParallelism,
                ConfiguredParallelism = configuredParallelism,
                EnqueueLogs = safeLocalSnapshot.EnqueueLogs ?? [],
                ActiveWorkers = mergedWorkers
                    .OrderBy(x => x.WorkerId)
                    .ThenBy(x => x.WorkerLabel, StringComparer.Ordinal)
                    .ToArray(),
                WaitingWorkers = MergeWaitingWorkers(mergedWorkers, mergedWaitingWorkers, configuredParallelism),
            };
        }

        private static IReadOnlyList<ThumbnailProgressWorkerSnapshot> MergeWaitingWorkers(
            IReadOnlyList<ThumbnailProgressWorkerSnapshot> mergedWorkers,
            IReadOnlyList<ThumbnailProgressWorkerSnapshot> mergedWaitingWorkers,
            int configuredParallelism
        )
        {
            HashSet<long> activeIds = new((mergedWorkers ?? []).Select(x => x.WorkerId));
            Dictionary<long, ThumbnailProgressWorkerSnapshot> waitingById = new();
            foreach (ThumbnailProgressWorkerSnapshot waiting in mergedWaitingWorkers ?? [])
            {
                if (waiting == null || waiting.WorkerId < 1 || activeIds.Contains(waiting.WorkerId))
                {
                    continue;
                }

                waitingById[waiting.WorkerId] = waiting;
            }

            for (long workerId = 1; workerId <= Math.Max(0, configuredParallelism); workerId++)
            {
                if (activeIds.Contains(workerId) || waitingById.ContainsKey(workerId))
                {
                    continue;
                }

                waitingById[workerId] = new ThumbnailProgressWorkerSnapshot
                {
                    WorkerId = workerId,
                    WorkerLabel = workerId switch
                    {
                        1 => "ゆっくり",
                        2 => "再試行専",
                        _ => $"通常 {Math.Max(1, workerId - 2)}",
                    },
                    WorkerRole = workerId switch
                    {
                        1 => ThumbnailProgressWorkerRole.Idle,
                        2 => ThumbnailProgressWorkerRole.Recovery,
                        _ => ThumbnailProgressWorkerRole.Normal,
                    },
                    State = ThumbnailProgressSnapshotState.Waiting,
                    IsActive = false,
                    UpdatedAtUtc = DateTime.UtcNow,
                };
            }

            return waitingById.Values.OrderBy(x => x.WorkerId).ToArray();
        }

        internal static string ResolveSnapshotDirectoryPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IndigoMovieManager_fork",
                "progress"
            );
        }

        internal static string ToSafeOwnerFileName(string ownerInstanceId)
        {
            if (string.IsNullOrWhiteSpace(ownerInstanceId))
            {
                return "worker";
            }

            StringBuilder builder = new(ownerInstanceId.Length);
            foreach (char ch in ownerInstanceId)
            {
                builder.Append(
                    char.IsLetterOrDigit(ch) || ch == '_' || ch == '-'
                        ? ch
                        : '_'
                );
            }

            return builder.Length > 0 ? builder.ToString() : "worker";
        }

        private static List<ThumbnailProgressExternalSnapshotEnvelope> LoadEnvelopes(
            string mainDbFullPath,
            IReadOnlyList<string> allowedOwnerInstanceIds,
            TimeSpan maxAge
        )
        {
            string directoryPath = ResolveSnapshotDirectoryPath();
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
            List<ThumbnailProgressExternalSnapshotEnvelope> envelopes = [];
            foreach (string ownerInstanceId in allowedOwners)
            {
                string safeOwner = ToSafeOwnerFileName(ownerInstanceId);
                string snapshotFilePath = Path.Combine(
                    directoryPath,
                    $"thumbnail-progress-{safeOwner}.json"
                );
                if (!File.Exists(snapshotFilePath))
                {
                    continue;
                }

                try
                {
                    string json;
                    // Move(上書き)によるアトミックな差し替えを阻害しないよう、ReadWriteとDeleteの共有を許可して開く！
                    using (
                        FileStream fs = new(
                            snapshotFilePath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete
                        )
                    )
                    using (StreamReader reader = new(fs, Encoding.UTF8))
                    {
                        json = reader.ReadToEnd();
                    }

                    ThumbnailProgressExternalSnapshotEnvelope envelope =
                        JsonSerializer.Deserialize<ThumbnailProgressExternalSnapshotEnvelope>(
                            json,
                            JsonOptions
                        );
                    if (envelope == null)
                    {
                        continue;
                    }

                    if (
                        !string.Equals(
                            envelope.MainDbFullPath ?? "",
                            mainDbFullPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        continue;
                    }

                    if (!allowedOwners.Contains(envelope.OwnerInstanceId ?? ""))
                    {
                        continue;
                    }

                    if (maxAge > TimeSpan.Zero && nowUtc - envelope.UpdatedAtUtc > maxAge)
                    {
                        continue;
                    }

                    envelopes.Add(envelope);
                }
                catch
                {
                    // 読み途中のファイルは次周期で拾い直す。
                }
            }

            return envelopes;
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
                if (
                    string.Equals(
                        allowedOwnerInstanceIds[i],
                        ownerInstanceId,
                        StringComparison.Ordinal
                    )
                )
                {
                    return i;
                }
            }

            return int.MaxValue;
        }

        private static long ComputeMergedVersion(
            ThumbnailProgressRuntimeSnapshot localSnapshot,
            IReadOnlyList<ThumbnailProgressExternalSnapshotEnvelope> envelopes
        )
        {
            ulong version = unchecked((ulong)(localSnapshot?.Version ?? 0));
            foreach (
                ThumbnailProgressExternalSnapshotEnvelope envelope in envelopes.OrderBy(x =>
                    x.OwnerInstanceId,
                    StringComparer.Ordinal
                )
            )
            {
                version = Fnv1a(version, (ulong)(envelope.UpdatedAtUtc.Ticks));
                version = Fnv1a(version, (ulong)(envelope.Snapshot?.Version ?? 0));
                version = Fnv1a(version, ComputeStableStringHash(envelope.OwnerInstanceId ?? ""));
            }

            return unchecked((long)version);
        }

        private static ulong Fnv1a(ulong current, ulong value)
        {
            const ulong fnvPrime = 1099511628211;
            ulong hash = current == 0 ? 14695981039346656037 : current;
            hash ^= value;
            hash *= fnvPrime;
            return hash;
        }

        private static ulong ComputeStableStringHash(string text)
        {
            const ulong fnvPrime = 1099511628211;
            ulong hash = 14695981039346656037;
            foreach (char ch in text ?? "")
            {
                hash ^= ch;
                hash *= fnvPrime;
            }

            return hash;
        }
    }

    public sealed class ThumbnailProgressExternalSnapshotEnvelope
    {
        public int SchemaVersion { get; init; } = ThumbnailProgressRuntime.ProgressSnapshotSchemaVersion;
        public string MainDbFullPath { get; init; } = "";
        public string OwnerInstanceId { get; init; } = "";
        public DateTime UpdatedAtUtc { get; init; }
        public ThumbnailProgressRuntimeSnapshot Snapshot { get; init; } = new();
    }
}
