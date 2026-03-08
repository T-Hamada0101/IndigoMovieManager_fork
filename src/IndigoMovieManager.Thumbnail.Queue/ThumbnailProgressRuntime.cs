using System.IO;
using IndigoMovieManager.Thumbnail.QueueDb;
using IndigoMovieManager.Thumbnail.QueuePipeline;

namespace IndigoMovieManager.Thumbnail
{
    // サムネイル進捗タブ向けに、実行中の状態を軽量に保持する。
    public sealed class ThumbnailProgressRuntime
    {
        public const int ProgressSnapshotSchemaVersion = 1;
        private const int MaxEnqueueLogCount = 10;
        private const int MovieNameHeadLength = 17;
        private const int MaxRetainedWorkerPanelCount = 48;
        private const long SlowLaneWorkerId = 1;
        private const long RecoveryLaneWorkerId = 2;
        private const long FirstGeneralWorkerId = 3;

        private readonly object stateLock = new();
        private readonly Queue<string> enqueueLogs = new();
        private readonly Dictionary<string, WorkerState> activeWorkers = new(
            StringComparer.OrdinalIgnoreCase
        );

        private string persistentMainDbFullPath = "";
        private long workerSequence;
        private int sessionCompletedCount;
        private int sessionTotalCount;
        private int sessionCreatedThumbnailCount;
        private int currentParallelism;
        private int configuredParallelism;
        private long stateVersion;
        private ThumbnailProgressRuntimeSnapshot cachedSnapshot;

        public void Reset()
        {
            lock (stateLock)
            {
                bool hasAnyState =
                    enqueueLogs.Count > 0
                    || activeWorkers.Count > 0
                    || workerSequence != 0
                    || sessionCompletedCount != 0
                    || sessionTotalCount != 0
                    || sessionCreatedThumbnailCount != 0
                    || currentParallelism != 0
                    || configuredParallelism != 0;

                enqueueLogs.Clear();
                activeWorkers.Clear();
                workerSequence = 0;
                sessionCompletedCount = 0;
                sessionTotalCount = 0;
                sessionCreatedThumbnailCount = string.IsNullOrWhiteSpace(persistentMainDbFullPath)
                    ? 0
                    : ThumbnailCreatedCountStore.Load(persistentMainDbFullPath);
                currentParallelism = 0;
                configuredParallelism = 0;
                if (hasAnyState)
                {
                    MarkStateDirty();
                }
            }
        }

        // DB切替や worker 起動時に永続カウンタの参照先を差し替える。
        public void SetPersistentMainDbFullPath(string mainDbFullPath)
        {
            string normalizedPath = mainDbFullPath ?? "";
            lock (stateLock)
            {
                if (
                    string.Equals(
                        persistentMainDbFullPath,
                        normalizedPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return;
                }

                persistentMainDbFullPath = normalizedPath;
                sessionCreatedThumbnailCount = string.IsNullOrWhiteSpace(normalizedPath)
                    ? 0
                    : ThumbnailCreatedCountStore.Load(normalizedPath);
                MarkStateDirty();
            }
        }

        // キュー投入ログは「動画名のみ」を最新N件で保持する。
        public void RecordEnqueue(QueueObj queueObj)
        {
            string movieName = Path.GetFileName(queueObj?.MovieFullPath ?? "");
            if (string.IsNullOrWhiteSpace(movieName))
            {
                return;
            }

            lock (stateLock)
            {
                enqueueLogs.Enqueue(movieName);
                while (enqueueLogs.Count > MaxEnqueueLogCount)
                {
                    _ = enqueueLogs.Dequeue();
                }

                MarkStateDirty();
            }
        }

        public void UpdateSessionProgress(
            int completedCount,
            int totalCount,
            int currentParallel,
            int configuredParallel
        )
        {
            lock (stateLock)
            {
                int nextCompletedCount = Math.Max(0, completedCount);
                int nextTotalCount = Math.Max(nextCompletedCount, Math.Max(0, totalCount));
                int nextCurrentParallelism = Math.Max(0, currentParallel);
                int nextConfiguredParallelism = Math.Max(0, configuredParallel);

                if (
                    sessionCompletedCount == nextCompletedCount
                    && sessionTotalCount == nextTotalCount
                    && currentParallelism == nextCurrentParallelism
                    && configuredParallelism == nextConfiguredParallelism
                )
                {
                    return;
                }

                sessionCompletedCount = nextCompletedCount;
                sessionTotalCount = nextTotalCount;
                currentParallelism = nextCurrentParallelism;
                configuredParallelism = nextConfiguredParallelism;
                MarkStateDirty();
            }
        }

        // ジョブ開始時に右サイド表示の作業パネルを追加/更新する。
        public void MarkJobStarted(QueueObj queueObj)
        {
            if (queueObj == null || string.IsNullOrWhiteSpace(queueObj.MovieFullPath))
            {
                return;
            }

            string key = CreateWorkerKey(queueObj);
            lock (stateLock)
            {
                WorkerState worker = AcquireWorkerForJob(key, queueObj);
                string nextMoviePath = queueObj.MovieFullPath ?? "";
                string nextDisplayMovieName = ToDisplayMovieName(queueObj.MovieFullPath);
                bool isChanged = false;

                if (!string.Equals(worker.MoviePath, nextMoviePath, StringComparison.OrdinalIgnoreCase))
                {
                    worker.MoviePath = nextMoviePath;
                    isChanged = true;
                }

                if (worker.MovieId != queueObj.MovieId)
                {
                    worker.MovieId = queueObj.MovieId;
                    isChanged = true;
                }

                if (worker.TabIndex != queueObj.Tabindex)
                {
                    worker.TabIndex = queueObj.Tabindex;
                    isChanged = true;
                }

                if (!string.Equals(worker.DisplayMovieName, nextDisplayMovieName, StringComparison.Ordinal))
                {
                    worker.DisplayMovieName = nextDisplayMovieName;
                    isChanged = true;
                }

                if (!worker.IsActive)
                {
                    worker.IsActive = true;
                    isChanged = true;
                }

                if (worker.CompletedAtUtc != DateTime.MinValue)
                {
                    worker.CompletedAtUtc = DateTime.MinValue;
                    isChanged = true;
                }

                if (!string.Equals(worker.State, ThumbnailProgressSnapshotState.Started, StringComparison.Ordinal))
                {
                    worker.State = ThumbnailProgressSnapshotState.Started;
                    isChanged = true;
                }

                worker.UpdatedAtUtc = DateTime.UtcNow;
                isChanged = true;

                if (isChanged)
                {
                    MarkStateDirty();
                }
            }
        }

        // サムネイル保存直後の画像パスを作業パネルへ反映する。
        // メモリプレビュー情報がある場合は、ファイル表示より優先できるよう同時に保持する。
        public void MarkThumbnailSaved(
            QueueObj queueObj,
            string previewImagePath,
            string previewCacheKey = "",
            long previewRevision = 0
        )
        {
            if (queueObj == null || string.IsNullOrWhiteSpace(previewImagePath))
            {
                return;
            }

            string key = CreateWorkerKey(queueObj);
            lock (stateLock)
            {
                WorkerState worker = AcquireWorkerForJob(key, queueObj);
                // 同一パネルに連続して同一動画キーが来た場合は、完了画像を再代入しない。
                if (string.Equals(worker.LastAppliedPreviewJobKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                worker.MoviePath = queueObj.MovieFullPath ?? "";
                worker.MovieId = queueObj.MovieId;
                worker.TabIndex = queueObj.Tabindex;
                worker.DisplayMovieName = ToDisplayMovieName(queueObj.MovieFullPath);
                worker.PreviewImagePath = previewImagePath;
                if (!string.IsNullOrWhiteSpace(previewCacheKey) && previewRevision > 0)
                {
                    worker.PreviewCacheKey = previewCacheKey;
                    worker.PreviewRevision = previewRevision;
                }
                else
                {
                    worker.PreviewCacheKey = "";
                    worker.PreviewRevision = previewRevision > 0
                        ? previewRevision
                        : DateTime.UtcNow.Ticks;
                }
                worker.IsActive = true;
                worker.CompletedAtUtc = DateTime.MinValue;
                worker.LastAppliedPreviewJobKey = key;
                worker.State = ThumbnailProgressSnapshotState.Saved;
                worker.UpdatedAtUtc = DateTime.UtcNow;
                sessionCreatedThumbnailCount = string.IsNullOrWhiteSpace(persistentMainDbFullPath)
                    ? sessionCreatedThumbnailCount + 1
                    : ThumbnailCreatedCountStore.Increment(persistentMainDbFullPath);
                MarkStateDirty();
            }
        }

        // ジョブ完了時は即削除せず、完了状態で残して履歴として見えるようにする。
        public void MarkJobCompleted(QueueObj queueObj)
        {
            if (queueObj == null || string.IsNullOrWhiteSpace(queueObj.MovieFullPath))
            {
                return;
            }

            string key = CreateWorkerKey(queueObj);
            lock (stateLock)
            {
                if (!activeWorkers.TryGetValue(key, out WorkerState worker))
                {
                    return;
                }

                if (!worker.IsActive)
                {
                    return;
                }

                worker.IsActive = false;
                worker.CompletedAtUtc = DateTime.UtcNow;
                worker.State = ThumbnailProgressSnapshotState.Completed;
                worker.UpdatedAtUtc = worker.CompletedAtUtc;
                TrimCompletedWorkersIfNeeded();
                MarkStateDirty();
            }
        }

        // 失敗も完了パネルとして残し、外部 viewer が状態を読めるようにする。
        public void MarkJobFailed(QueueObj queueObj)
        {
            if (queueObj == null || string.IsNullOrWhiteSpace(queueObj.MovieFullPath))
            {
                return;
            }

            string key = CreateWorkerKey(queueObj);
            lock (stateLock)
            {
                WorkerState worker = AcquireWorkerForJob(key, queueObj);
                worker.MoviePath = queueObj.MovieFullPath ?? "";
                worker.MovieId = queueObj.MovieId;
                worker.TabIndex = queueObj.Tabindex;
                worker.DisplayMovieName = ToDisplayMovieName(queueObj.MovieFullPath);
                worker.IsActive = false;
                worker.CompletedAtUtc = DateTime.UtcNow;
                worker.State = ThumbnailProgressSnapshotState.Failed;
                worker.UpdatedAtUtc = worker.CompletedAtUtc;
                TrimCompletedWorkersIfNeeded();
                MarkStateDirty();
            }
        }

        public ThumbnailProgressRuntimeSnapshot CreateSnapshot()
        {
            lock (stateLock)
            {
                if (cachedSnapshot is not null && cachedSnapshot.Version == stateVersion)
                {
                    return cachedSnapshot;
                }

                IReadOnlyList<string> logSnapshot = [.. enqueueLogs];
                IEnumerable<WorkerState> active =
                    activeWorkers.Values.Where(x => x.IsActive).OrderBy(x => x.WorkerId);
                IEnumerable<WorkerState> completed =
                    activeWorkers
                        .Values.Where(x => !x.IsActive)
                        .OrderByDescending(x => x.CompletedAtUtc)
                        .ThenByDescending(x => x.WorkerId);
                IReadOnlyList<ThumbnailProgressWorkerSnapshot> workerSnapshot =
                [
                    .. active.Concat(completed).Select(
                        x =>
                            new ThumbnailProgressWorkerSnapshot
                            {
                                WorkerId = x.WorkerId,
                                WorkerLabel = ResolveWorkerLabel(x.WorkerId),
                                WorkerRole = ResolveWorkerRole(x.WorkerId),
                                State = string.IsNullOrWhiteSpace(x.State)
                                    ? (
                                        x.IsActive
                                            ? ThumbnailProgressSnapshotState.Started
                                            : ThumbnailProgressSnapshotState.Completed
                                    )
                                    : x.State,
                                MovieId = x.MovieId,
                                TabIndex = x.TabIndex,
                                DisplayMovieName = x.DisplayMovieName,
                                MovieFullPath = x.MoviePath,
                                PreviewImagePath = x.PreviewImagePath,
                                PreviewCacheKey = x.PreviewCacheKey,
                                PreviewRevision = x.PreviewRevision,
                                IsActive = x.IsActive,
                                UpdatedAtUtc = x.UpdatedAtUtc,
                            }
                    ),
                ];
                IReadOnlyList<ThumbnailProgressWorkerSnapshot> waitingWorkers =
                    CreateWaitingWorkerSnapshots(workerSnapshot, configuredParallelism);

                ThumbnailProgressRuntimeSnapshot snapshot = new()
                {
                    SchemaVersion = ProgressSnapshotSchemaVersion,
                    Version = stateVersion,
                    SessionCompletedCount = sessionCompletedCount,
                    SessionTotalCount = sessionTotalCount,
                    SessionCreatedThumbnailCount = sessionCreatedThumbnailCount,
                    CurrentParallelism = currentParallelism,
                    ConfiguredParallelism = configuredParallelism,
                    EnqueueLogs = logSnapshot,
                    ActiveWorkers = workerSnapshot,
                    WaitingWorkers = waitingWorkers,
                };

                cachedSnapshot = snapshot;
                return snapshot;
            }
        }

        public static string CreateWorkerKey(QueueObj queueObj)
        {
            string moviePathKey = QueueDbPathResolver.CreateMoviePathKey(
                queueObj?.MovieFullPath ?? ""
            );
            return $"{moviePathKey}:{queueObj?.Tabindex ?? -1}";
        }

        // まず同一ジョブキーを探し、なければ完了済みパネルを1つ再利用する。
        // 再利用時はPreviewImagePathを維持し、次ジョブのサムネが来るまで画像を見せ続ける。
        private WorkerState AcquireWorkerForJob(string key, QueueObj queueObj)
        {
            if (activeWorkers.TryGetValue(key, out WorkerState existing))
            {
                return existing;
            }

            long preferredWorkerId = ResolvePreferredWorkerId(queueObj);
            if (preferredWorkerId > 0)
            {
                string preferredReusableKey =
                    activeWorkers
                        .Where(x => !x.Value.IsActive && x.Value.WorkerId == preferredWorkerId)
                        .OrderByDescending(x => x.Value.CompletedAtUtc)
                        .Select(x => x.Key)
                        .FirstOrDefault() ?? "";
                if (!string.IsNullOrWhiteSpace(preferredReusableKey))
                {
                    WorkerState preferredReused = activeWorkers[preferredReusableKey];
                    _ = activeWorkers.Remove(preferredReusableKey);
                    activeWorkers[key] = preferredReused;
                    return preferredReused;
                }

                bool preferredInUse = activeWorkers.Values.Any(x => x.WorkerId == preferredWorkerId);
                if (!preferredInUse)
                {
                    WorkerState createdPreferred = new() { WorkerId = preferredWorkerId };
                    workerSequence = Math.Max(workerSequence, preferredWorkerId);
                    activeWorkers[key] = createdPreferred;
                    return createdPreferred;
                }
            }

            string reusableKey =
                activeWorkers
                        .Where(x =>
                            !x.Value.IsActive
                        && x.Value.WorkerId != SlowLaneWorkerId
                        && x.Value.WorkerId != RecoveryLaneWorkerId
                    )
                    .OrderByDescending(x => x.Value.CompletedAtUtc)
                    .ThenByDescending(x => x.Value.WorkerId)
                    .Select(x => x.Key)
                    .FirstOrDefault() ?? "";
            if (!string.IsNullOrWhiteSpace(reusableKey))
            {
                WorkerState reused = activeWorkers[reusableKey];
                _ = activeWorkers.Remove(reusableKey);
                activeWorkers[key] = reused;
                return reused;
            }

            long nextWorkerId = Math.Max(workerSequence + 1, FirstGeneralWorkerId);
            WorkerState created = new() { WorkerId = nextWorkerId };
            workerSequence = nextWorkerId;
            activeWorkers[key] = created;
            return created;
        }

        // 動画サイズベースで、優先/低速レーンへ割り当てるWorkerIdを返す。
        // 0は「通常スロットへ割り当て」を意味する。
        private static long ResolvePreferredWorkerId(QueueObj queueObj)
        {
            if (queueObj == null)
            {
                return 0;
            }

            if (queueObj.AttemptCount > 0)
            {
                return RecoveryLaneWorkerId;
            }

            ThumbnailExecutionLane lane = ThumbnailLaneClassifier.ResolveLane(queueObj.MovieSizeBytes);
            return lane switch
            {
                ThumbnailExecutionLane.Slow => SlowLaneWorkerId,
                _ => 0,
            };
        }

        // パネル総数が上限を超えたら、古い完了済みだけ間引く。
        private void TrimCompletedWorkersIfNeeded()
        {
            int overflowCount = activeWorkers.Count - MaxRetainedWorkerPanelCount;
            if (overflowCount <= 0)
            {
                return;
            }

            string[] staleKeys =
            [
                .. activeWorkers
                    .Where(x => !x.Value.IsActive)
                    .OrderBy(x => x.Value.CompletedAtUtc)
                    .ThenBy(x => x.Value.WorkerId)
                    .Take(overflowCount)
                    .Select(x => x.Key),
            ];
            foreach (string staleKey in staleKeys)
            {
                _ = activeWorkers.Remove(staleKey);
            }
        }

        private void MarkStateDirty()
        {
            stateVersion++;
            cachedSnapshot = null;
        }

        // 長いファイル名は拡張子を残して中間省略する。
        private static string ToDisplayMovieName(string moviePath)
        {
            string fileName = Path.GetFileName(moviePath ?? "");
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return "(不明)";
            }

            string extension = Path.GetExtension(fileName);
            string body = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(extension))
            {
                return fileName.Length <= (MovieNameHeadLength + 3)
                    ? fileName
                    : $"{fileName[..MovieNameHeadLength]}...";
            }

            if (body.Length <= MovieNameHeadLength)
            {
                return fileName;
            }

            string extNoDot = extension.TrimStart('.');
            return $"{body[..MovieNameHeadLength]}...{extNoDot}";
        }

        // 特殊2レーンの表示名を固定し、それ以外は従来番号を使う。
        private static string ResolveWorkerLabel(long workerId)
        {
            return workerId switch
            {
                1 => "ゆっくり",
                2 => "再試行専",
                _ => $"通常 {Math.Max(1, workerId - 2)}",
            };
        }

        private static string ResolveWorkerRole(long workerId)
        {
            return workerId switch
            {
                1 => ThumbnailProgressWorkerRole.Idle,
                2 => ThumbnailProgressWorkerRole.Recovery,
                _ => ThumbnailProgressWorkerRole.Normal,
            };
        }

        private static IReadOnlyList<ThumbnailProgressWorkerSnapshot> CreateWaitingWorkerSnapshots(
            IReadOnlyList<ThumbnailProgressWorkerSnapshot> workers,
            int configuredParallelism
        )
        {
            int slotCount = Math.Max(0, configuredParallelism);
            if (slotCount < 1)
            {
                return [];
            }

            HashSet<long> usedWorkerIds = new((workers ?? []).Select(x => x.WorkerId));
            List<ThumbnailProgressWorkerSnapshot> waitingWorkers = [];
            for (long workerId = 1; workerId <= slotCount; workerId++)
            {
                if (usedWorkerIds.Contains(workerId))
                {
                    continue;
                }

                waitingWorkers.Add(
                    new ThumbnailProgressWorkerSnapshot
                    {
                        WorkerId = workerId,
                        WorkerLabel = ResolveWorkerLabel(workerId),
                        WorkerRole = ResolveWorkerRole(workerId),
                        State = ThumbnailProgressSnapshotState.Waiting,
                        IsActive = false,
                        UpdatedAtUtc = DateTime.UtcNow,
                    }
                );
            }

            return waitingWorkers;
        }

        private sealed class WorkerState
        {
            public long WorkerId { get; set; }
            public long MovieId { get; set; }
            public int TabIndex { get; set; } = -1;
            public string MoviePath { get; set; } = "";
            public string DisplayMovieName { get; set; } = "(不明)";
            public string PreviewImagePath { get; set; } = "";
            public string PreviewCacheKey { get; set; } = "";
            public long PreviewRevision { get; set; }
            public string LastAppliedPreviewJobKey { get; set; } = "";
            public bool IsActive { get; set; } = true;
            public DateTime CompletedAtUtc { get; set; } = DateTime.MinValue;
            public string State { get; set; } = ThumbnailProgressSnapshotState.Started;
            public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        }
    }

    public sealed class ThumbnailProgressRuntimeSnapshot
    {
        public int SchemaVersion { get; init; } = ThumbnailProgressRuntime.ProgressSnapshotSchemaVersion;
        public long Version { get; init; }
        public int SessionCompletedCount { get; init; }
        public int SessionTotalCount { get; init; }
        public int SessionCreatedThumbnailCount { get; init; }
        public int CurrentParallelism { get; init; }
        public int ConfiguredParallelism { get; init; }
        public IReadOnlyList<string> EnqueueLogs { get; init; } = [];
        public IReadOnlyList<ThumbnailProgressWorkerSnapshot> ActiveWorkers { get; init; } = [];
        public IReadOnlyList<ThumbnailProgressWorkerSnapshot> WaitingWorkers { get; init; } = [];
    }

    public sealed class ThumbnailProgressWorkerSnapshot
    {
        public long WorkerId { get; init; }
        public string WorkerLabel { get; init; } = "";
        public string WorkerRole { get; init; } = "";
        public string State { get; init; } = "";
        public long MovieId { get; init; }
        public int TabIndex { get; init; } = -1;
        public string MainDbFullPath { get; init; } = "";
        public string OwnerInstanceId { get; init; } = "";
        public string DisplayMovieName { get; init; } = "";
        public string MovieFullPath { get; init; } = "";
        public string PreviewImagePath { get; init; } = "";
        public string PreviewCacheKey { get; init; } = "";
        public long PreviewRevision { get; init; }
        public bool IsActive { get; init; } = true;
        public DateTime UpdatedAtUtc { get; init; }
    }

    public static class ThumbnailProgressSnapshotState
    {
        public const string Waiting = "waiting";
        public const string Started = "started";
        public const string Saved = "saved";
        public const string Completed = "completed";
        public const string Failed = "failed";
    }

    public static class ThumbnailProgressWorkerRole
    {
        public const string Normal = "normal";
        public const string Idle = "idle";
        public const string Recovery = "recovery";
    }
}
