using System.Globalization;
using IndigoMovieManager.Thumbnail.Ipc;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager.Thumbnail.QueuePipeline
{
    // queue の永続化要求を、将来 worker へ渡せる UI 非依存の契約へ写す。
    public static class ThumbnailQueueWorkerContractAdapter
    {
        public const string WorkerKind = "thumbnail-create";
        public const string QueueCapability = "thumbnail-queue";
        public const string ResultStatusSucceeded = "succeeded";
        public const string ResultStatusFailed = "failed";
        public const string ResultRetryable = "retryable";
        public const string ResultNotRetryable = "not-retryable";
        public const string ResultArtifactKind = "thumbnail-image";
        public const string ProgressStageQueued = "queued";
        public const string ProgressStageRunning = "running";
        public const string ProgressStageCompleted = "completed";
        private const string ResultArtifactContentType = "image/jpeg";

        public static WorkerJobRequestDto ToWorkerJobRequestDto(
            QueueRequest request,
            string outputArtifactPath = "",
            long timeoutMs = 0
        )
        {
            request ??= new QueueRequest();
            ThumbnailQueuePriority priority = ThumbnailQueuePriorityHelper.Normalize(
                request.Priority
            );
            string moviePath = request.MoviePath ?? "";

            return new WorkerJobRequestDto
            {
                JobId = BuildJobId(request),
                Kind = WorkerKind,
                InputFiles = string.IsNullOrWhiteSpace(moviePath) ? [] : [moviePath],
                OutputArtifactPath = outputArtifactPath ?? "",
                TimeoutMs = Math.Max(0, timeoutMs),
                Capabilities = [QueueCapability, WorkerKind, priority.ToString()],
                DiagnosticContext = BuildDiagnosticContext(request, priority),
                RequestedAtUtc = request.RequestedAtUtc,
            };
        }

        public static WorkerJobProgressDto ToWorkerJobProgressDto(
            QueueDbLeaseItem leasedItem,
            int completedCount,
            int totalCount,
            int currentParallelism = 0,
            int configuredParallelism = 0,
            string stage = ProgressStageRunning,
            string message = "",
            DateTime? capturedAtUtc = null
        )
        {
            leasedItem ??= new QueueDbLeaseItem();
            string normalizedStage = NormalizeField(stage);
            if (string.IsNullOrWhiteSpace(normalizedStage))
            {
                normalizedStage = ProgressStageRunning;
            }

            return new WorkerJobProgressDto
            {
                JobId = BuildJobId(leasedItem),
                Stage = normalizedStage,
                CompletedCount = Math.Max(0, completedCount),
                TotalCount = NormalizeTotalCount(completedCount, totalCount),
                CurrentInputFile = leasedItem.MoviePath ?? "",
                Message = NormalizeField(message),
                Metrics = BuildProgressMetrics(
                    leasedItem,
                    currentParallelism,
                    configuredParallelism
                ),
                CapturedAtUtc = capturedAtUtc ?? DateTime.UtcNow,
            };
        }

        public static WorkerJobProgressDto ToWorkerJobProgressDto(
            ThumbnailProgressRuntimeSnapshot snapshot,
            string stage = ProgressStageRunning,
            string message = "",
            DateTime? capturedAtUtc = null
        )
        {
            snapshot ??= new ThumbnailProgressRuntimeSnapshot();
            string normalizedStage = NormalizeField(stage);
            if (string.IsNullOrWhiteSpace(normalizedStage))
            {
                normalizedStage = ProgressStageRunning;
            }

            ThumbnailProgressWorkerSnapshot currentWorker =
                snapshot.ActiveWorkers?.FirstOrDefault(x => x?.IsActive == true)
                ?? snapshot.RescueWorker;

            // runtime snapshot から取れる軽量値だけを Worker 進捗語彙へ畳む。
            return new WorkerJobProgressDto
            {
                JobId = BuildProgressSnapshotJobId(snapshot),
                Stage = normalizedStage,
                CompletedCount = Math.Max(0, snapshot.SessionCompletedCount),
                TotalCount = NormalizeTotalCount(
                    snapshot.SessionCompletedCount,
                    snapshot.SessionTotalCount
                ),
                CurrentInputFile = currentWorker?.MoviePath ?? "",
                Message = NormalizeField(message),
                Metrics = BuildProgressSnapshotMetrics(snapshot, currentWorker),
                CapturedAtUtc = capturedAtUtc ?? DateTime.UtcNow,
            };
        }

        public static WorkerJobResultDto ToWorkerJobResultDto(
            QueueDbLeaseItem leasedItem,
            bool succeeded,
            string artifactPath = "",
            string failureKind = "",
            string failureReason = "",
            bool retryable = false,
            long elapsedMs = 0,
            IReadOnlyDictionary<string, string> metrics = null
        )
        {
            leasedItem ??= new QueueDbLeaseItem();
            string status = succeeded ? ResultStatusSucceeded : ResultStatusFailed;
            string normalizedFailureKind = succeeded ? "" : NormalizeField(failureKind);
            string normalizedFailureReason = ResolveFailureReason(
                succeeded,
                normalizedFailureKind,
                failureReason
            );
            long normalizedElapsedMs = Math.Max(0, elapsedMs);

            return new WorkerJobResultDto
            {
                JobId = BuildJobId(leasedItem),
                Status = status,
                Artifact = BuildWorkerArtifact(leasedItem, artifactPath),
                FailureReason = normalizedFailureReason,
                ElapsedMs = normalizedElapsedMs,
                Retryability = !succeeded && retryable ? ResultRetryable : ResultNotRetryable,
                Logs = BuildResultLogs(
                    leasedItem,
                    status,
                    normalizedFailureKind,
                    normalizedFailureReason,
                    retryable
                ),
                Metrics = BuildResultMetrics(
                    leasedItem,
                    status,
                    normalizedFailureKind,
                    retryable,
                    normalizedElapsedMs,
                    metrics
                ),
            };
        }

        private static string BuildJobId(QueueRequest request)
        {
            string key = ResolveMoviePathKey(request.MoviePathKey, request.MoviePath);
            if (string.IsNullOrWhiteSpace(key))
            {
                key = "empty";
            }

            return string.Create(
                CultureInfo.InvariantCulture,
                $"thumbnail-{key}-{request.RequestedAtUtc:yyyyMMddHHmmssfff}"
            );
        }

        private static string BuildJobId(QueueDbLeaseItem leasedItem)
        {
            string key = ResolveMoviePathKey(leasedItem.MoviePathKey, leasedItem.MoviePath);
            if (string.IsNullOrWhiteSpace(key))
            {
                key = "empty";
            }

            return string.Create(
                CultureInfo.InvariantCulture,
                $"thumbnail-{key}-queue-{Math.Max(0, leasedItem.QueueId)}"
            );
        }

        private static string BuildProgressSnapshotJobId(ThumbnailProgressRuntimeSnapshot snapshot)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"thumbnail-progress-snapshot-{Math.Max(0, snapshot.Version)}"
            );
        }

        private static Dictionary<string, string> BuildDiagnosticContext(
            QueueRequest request,
            ThumbnailQueuePriority priority
        )
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mainDbFullPath"] = request.MainDbFullPath ?? "",
                ["mainDbSessionStamp"] = Math.Max(0, request.MainDbSessionStamp)
                    .ToString(CultureInfo.InvariantCulture),
                ["moviePathKey"] = ResolveMoviePathKey(request.MoviePathKey, request.MoviePath),
                ["tabIndex"] = request.TabIndex.ToString(CultureInfo.InvariantCulture),
                ["movieSizeBytes"] = Math.Max(0, request.MovieSizeBytes)
                    .ToString(CultureInfo.InvariantCulture),
                ["thumbPanelPos"] = request.ThumbPanelPos?.ToString(CultureInfo.InvariantCulture)
                    ?? "",
                ["thumbTimePos"] = request.ThumbTimePos?.ToString(CultureInfo.InvariantCulture)
                    ?? "",
                ["priority"] = priority.ToString(),
            };
        }

        private static Dictionary<string, string> BuildProgressMetrics(
            QueueDbLeaseItem leasedItem,
            int currentParallelism,
            int configuredParallelism
        )
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["queueId"] = Math.Max(0, leasedItem.QueueId)
                    .ToString(CultureInfo.InvariantCulture),
                ["moviePathKey"] = ResolveMoviePathKey(
                    leasedItem.MoviePathKey,
                    leasedItem.MoviePath
                ),
                ["tabIndex"] = leasedItem.TabIndex.ToString(CultureInfo.InvariantCulture),
                ["priority"] = ThumbnailQueuePriorityHelper.Normalize(leasedItem.Priority)
                    .ToString(),
                ["attemptCount"] = Math.Max(0, leasedItem.AttemptCount)
                    .ToString(CultureInfo.InvariantCulture),
                ["ownerInstanceId"] = NormalizeField(leasedItem.OwnerInstanceId),
                ["currentParallelism"] = Math.Max(0, currentParallelism)
                    .ToString(CultureInfo.InvariantCulture),
                ["configuredParallelism"] = Math.Max(0, configuredParallelism)
                    .ToString(CultureInfo.InvariantCulture),
            };
        }

        private static Dictionary<string, string> BuildProgressSnapshotMetrics(
            ThumbnailProgressRuntimeSnapshot snapshot,
            ThumbnailProgressWorkerSnapshot currentWorker
        )
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["version"] = Math.Max(0, snapshot.Version).ToString(CultureInfo.InvariantCulture),
                ["sessionCompletedCount"] = Math.Max(0, snapshot.SessionCompletedCount)
                    .ToString(CultureInfo.InvariantCulture),
                ["sessionTotalCount"] = Math.Max(0, snapshot.SessionTotalCount)
                    .ToString(CultureInfo.InvariantCulture),
                ["totalCreatedCount"] = Math.Max(0, snapshot.TotalCreatedCount)
                    .ToString(CultureInfo.InvariantCulture),
                ["currentParallelism"] = Math.Max(0, snapshot.CurrentParallelism)
                    .ToString(CultureInfo.InvariantCulture),
                ["configuredParallelism"] = Math.Max(0, snapshot.ConfiguredParallelism)
                    .ToString(CultureInfo.InvariantCulture),
                ["activeWorkerCount"] = Math.Max(0, snapshot.ActiveWorkers?.Count ?? 0)
                    .ToString(CultureInfo.InvariantCulture),
                ["workerId"] = Math.Max(0, currentWorker?.WorkerId ?? 0)
                    .ToString(CultureInfo.InvariantCulture),
                ["workerLabel"] = NormalizeField(currentWorker?.WorkerLabel),
            };
        }

        private static WorkerJobArtifactDto BuildWorkerArtifact(
            QueueDbLeaseItem leasedItem,
            string artifactPath
        )
        {
            string normalizedArtifactPath = NormalizeField(artifactPath);
            bool hasArtifact = !string.IsNullOrWhiteSpace(normalizedArtifactPath);

            return new WorkerJobArtifactDto
            {
                ArtifactKind = hasArtifact ? ResultArtifactKind : "",
                Path = normalizedArtifactPath,
                ContentType = hasArtifact ? ResultArtifactContentType : "",
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["queueId"] = Math.Max(0, leasedItem.QueueId)
                        .ToString(CultureInfo.InvariantCulture),
                    ["moviePathKey"] = ResolveMoviePathKey(
                        leasedItem.MoviePathKey,
                        leasedItem.MoviePath
                    ),
                    ["tabIndex"] = leasedItem.TabIndex.ToString(CultureInfo.InvariantCulture),
                    ["priority"] = ThumbnailQueuePriorityHelper.Normalize(leasedItem.Priority)
                        .ToString(),
                },
            };
        }

        private static Dictionary<string, string> BuildResultMetrics(
            QueueDbLeaseItem leasedItem,
            string status,
            string failureKind,
            bool retryable,
            long elapsedMs,
            IReadOnlyDictionary<string, string> metrics
        )
        {
            Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase)
            {
                ["queueId"] = Math.Max(0, leasedItem.QueueId)
                    .ToString(CultureInfo.InvariantCulture),
                ["moviePathKey"] = ResolveMoviePathKey(
                    leasedItem.MoviePathKey,
                    leasedItem.MoviePath
                ),
                ["tabIndex"] = leasedItem.TabIndex.ToString(CultureInfo.InvariantCulture),
                ["movieSizeBytes"] = Math.Max(0, leasedItem.MovieSizeBytes)
                    .ToString(CultureInfo.InvariantCulture),
                ["thumbPanelPos"] = leasedItem.ThumbPanelPos?.ToString(CultureInfo.InvariantCulture)
                    ?? "",
                ["thumbTimePos"] = leasedItem.ThumbTimePos?.ToString(CultureInfo.InvariantCulture)
                    ?? "",
                ["priority"] = ThumbnailQueuePriorityHelper.Normalize(leasedItem.Priority)
                    .ToString(),
                ["attemptCount"] = Math.Max(0, leasedItem.AttemptCount)
                    .ToString(CultureInfo.InvariantCulture),
                ["ownerInstanceId"] = NormalizeField(leasedItem.OwnerInstanceId),
                ["leaseBucketRank"] = leasedItem.LeaseBucketRank.ToString(
                    CultureInfo.InvariantCulture
                ),
                ["leaseOrder"] = leasedItem.LeaseOrder.ToString(CultureInfo.InvariantCulture),
                ["status"] = NormalizeField(status),
                ["failureKind"] = NormalizeField(failureKind),
                ["retryable"] = retryable ? "true" : "false",
                ["elapsedMs"] = Math.Max(0, elapsedMs).ToString(CultureInfo.InvariantCulture),
            };

            if (metrics == null)
            {
                return result;
            }

            foreach (KeyValuePair<string, string> pair in metrics)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                result[pair.Key.Trim()] = pair.Value ?? "";
            }

            return result;
        }

        private static List<string> BuildResultLogs(
            QueueDbLeaseItem leasedItem,
            string status,
            string failureKind,
            string failureReason,
            bool retryable
        )
        {
            List<string> logs =
            [
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"thumbnail queue result: queue_id={Math.Max(0, leasedItem.QueueId)} status={NormalizeField(status)} retryable={(retryable ? "true" : "false")}"
                ),
            ];

            if (!string.IsNullOrWhiteSpace(failureKind))
            {
                logs.Add($"failure_kind={failureKind}");
            }

            if (!string.IsNullOrWhiteSpace(failureReason))
            {
                logs.Add($"failure_reason={failureReason}");
            }

            return logs;
        }

        private static string ResolveFailureReason(
            bool succeeded,
            string failureKind,
            string failureReason
        )
        {
            if (succeeded)
            {
                return "";
            }

            string normalizedFailureReason = NormalizeField(failureReason);
            if (!string.IsNullOrWhiteSpace(normalizedFailureReason))
            {
                return normalizedFailureReason;
            }

            return string.IsNullOrWhiteSpace(failureKind) ? "unknown" : failureKind;
        }

        private static string ResolveMoviePathKey(string moviePathKey, string moviePath)
        {
            return string.IsNullOrWhiteSpace(moviePathKey)
                ? QueueDbPathResolver.CreateMoviePathKey(moviePath ?? "")
                : moviePathKey.Trim();
        }

        private static int NormalizeTotalCount(int completedCount, int totalCount)
        {
            return Math.Max(Math.Max(0, completedCount), Math.Max(0, totalCount));
        }

        private static string NormalizeField(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        }
    }
}
