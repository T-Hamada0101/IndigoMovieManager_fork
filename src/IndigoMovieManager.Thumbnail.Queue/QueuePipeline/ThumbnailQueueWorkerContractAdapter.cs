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

        public static WorkerJobRequestDto ToWorkerJobRequestDto(
            QueueDbLeaseItem leasedItem,
            string outputArtifactPath = "",
            long timeoutMs = 0
        )
        {
            leasedItem ??= new QueueDbLeaseItem();
            ThumbnailQueuePriority priority = ThumbnailQueuePriorityHelper.Normalize(
                leasedItem.Priority
            );
            string moviePath = leasedItem.MoviePath ?? "";

            return new WorkerJobRequestDto
            {
                JobId = BuildJobId(leasedItem),
                Kind = WorkerKind,
                InputFiles = string.IsNullOrWhiteSpace(moviePath) ? [] : [moviePath],
                OutputArtifactPath = outputArtifactPath ?? "",
                TimeoutMs = Math.Max(0, timeoutMs),
                Capabilities = [QueueCapability, WorkerKind, priority.ToString()],
                DiagnosticContext = BuildDiagnosticContext(leasedItem, priority),
                RequestedAtUtc = DateTime.UtcNow,
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

        public static string BuildWorkerJobRequestLogFields(WorkerJobRequestDto request)
        {
            request ??= new WorkerJobRequestDto();

            return string.Create(
                CultureInfo.InvariantCulture,
                $"job_id={FormatLogValue(request.JobId)} worker_kind={FormatLogValue(request.Kind)} input_count={Math.Max(0, request.InputFiles?.Count ?? 0)} capability_count={Math.Max(0, request.Capabilities?.Count ?? 0)} diagnostic_context_count={Math.Max(0, request.DiagnosticContext?.Count ?? 0)} output_artifact_path={FormatLogValue(request.OutputArtifactPath)} timeout_ms={Math.Max(0, request.TimeoutMs)} queue_id={FormatLogValue(GetDiagnosticValue(request, "queueId"))} movie_path_key={FormatLogValue(GetDiagnosticValue(request, "moviePathKey"))} priority={FormatLogValue(GetDiagnosticValue(request, "priority"))}"
            );
        }

        public static string BuildWorkerJobProgressLogFields(WorkerJobProgressDto progress)
        {
            progress ??= new WorkerJobProgressDto();

            return string.Create(
                CultureInfo.InvariantCulture,
                $"job_id={FormatLogValue(progress.JobId)} worker_kind={FormatLogValue(WorkerKind)} worker_stage={FormatLogValue(progress.Stage)} progress_completed={Math.Max(0, progress.CompletedCount)} progress_total={Math.Max(0, progress.TotalCount)} queue_id={FormatLogValue(GetProgressMetricValue(progress, "queueId"))} movie_path_key={FormatLogValue(GetProgressMetricValue(progress, "moviePathKey"))} priority={FormatLogValue(GetProgressMetricValue(progress, "priority"))} current_parallelism={FormatLogValue(GetProgressMetricValue(progress, "currentParallelism"))} configured_parallelism={FormatLogValue(GetProgressMetricValue(progress, "configuredParallelism"))}"
            );
        }

        public static string BuildWorkerJobResultLogFields(WorkerJobResultDto result)
        {
            result ??= new WorkerJobResultDto();
            WorkerJobArtifactDto artifact = result.Artifact ?? new WorkerJobArtifactDto();

            // 実行結果ログへ載せる値だけをDTOから絞り、ログの語彙をWorker契約側に寄せる。
            return string.Create(
                CultureInfo.InvariantCulture,
                $"job_id={FormatLogValue(result.JobId)} worker_kind={FormatLogValue(WorkerKind)} status={FormatLogValue(result.Status)} artifact_kind={FormatLogValue(artifact.ArtifactKind)} retryability={FormatLogValue(result.Retryability)} elapsed_ms={Math.Max(0, result.ElapsedMs)} metric_count={Math.Max(0, result.Metrics?.Count ?? 0)} failure_kind={FormatLogValue(GetMetricValue(result, "failureKind"))} failure_reason={FormatLogValue(result.FailureReason)} output_artifact_path={FormatLogValue(artifact.Path)} queue_id={FormatLogValue(GetMetricValue(result, "queueId"))} movie_path_key={FormatLogValue(GetMetricValue(result, "moviePathKey"))} priority={FormatLogValue(GetMetricValue(result, "priority"))} attempt_count={FormatLogValue(GetMetricValue(result, "attemptCount"))}"
            );
        }

        public static string BuildWorkerQueueLogFields(
            WorkerJobRequestDto request,
            WorkerJobProgressDto progress,
            WorkerJobResultDto result
        )
        {
            request ??= new WorkerJobRequestDto();
            progress ??= new WorkerJobProgressDto();
            result ??= new WorkerJobResultDto();
            WorkerJobArtifactDto artifact = result.Artifact ?? new WorkerJobArtifactDto();

            // request / progress / result の代表値を1行へ畳み、実機ログで支配要因を追いやすくする。
            return string.Create(
                CultureInfo.InvariantCulture,
                $"job_id={FormatLogValue(FirstNonEmpty(result.JobId, progress.JobId, request.JobId))} worker_kind={FormatLogValue(FirstNonEmpty(request.Kind, WorkerKind))} worker_status={FormatLogValue(result.Status)} worker_stage={FormatLogValue(progress.Stage)} artifact_kind={FormatLogValue(artifact.ArtifactKind)} retryability={FormatLogValue(result.Retryability)} retryable={FormatLogValue(GetMetricValue(result, "retryable"))} elapsed_ms={Math.Max(0, result.ElapsedMs)} metric_count={Math.Max(0, result.Metrics?.Count ?? 0)} failure_kind={FormatLogValue(GetMetricValue(result, "failureKind"))} failure_reason={FormatLogValue(result.FailureReason)} progress_completed={Math.Max(0, progress.CompletedCount)} progress_total={Math.Max(0, progress.TotalCount)} queue_id={FormatLogValue(FirstNonEmpty(GetMetricValue(result, "queueId"), GetProgressMetricValue(progress, "queueId"), GetDiagnosticValue(request, "queueId")))} movie_path_key={FormatLogValue(FirstNonEmpty(GetMetricValue(result, "moviePathKey"), GetProgressMetricValue(progress, "moviePathKey"), GetDiagnosticValue(request, "moviePathKey")))} priority={FormatLogValue(FirstNonEmpty(GetMetricValue(result, "priority"), GetProgressMetricValue(progress, "priority"), GetDiagnosticValue(request, "priority")))} attempt_count={FormatLogValue(FirstNonEmpty(GetMetricValue(result, "attemptCount"), GetProgressMetricValue(progress, "attemptCount")))} current_parallelism={FormatLogValue(GetProgressMetricValue(progress, "currentParallelism"))} configured_parallelism={FormatLogValue(GetProgressMetricValue(progress, "configuredParallelism"))} input_count={Math.Max(0, request.InputFiles?.Count ?? 0)} capability_count={Math.Max(0, request.Capabilities?.Count ?? 0)} diagnostic_context_count={Math.Max(0, request.DiagnosticContext?.Count ?? 0)} output_artifact_path={FormatLogValue(FirstNonEmpty(artifact.Path, request.OutputArtifactPath))} timeout_ms={Math.Max(0, request.TimeoutMs)}"
            );
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

        private static Dictionary<string, string> BuildDiagnosticContext(
            QueueDbLeaseItem leasedItem,
            ThumbnailQueuePriority priority
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
                ["movieSizeBytes"] = Math.Max(0, leasedItem.MovieSizeBytes)
                    .ToString(CultureInfo.InvariantCulture),
                ["thumbPanelPos"] = leasedItem.ThumbPanelPos?.ToString(CultureInfo.InvariantCulture)
                    ?? "",
                ["thumbTimePos"] = leasedItem.ThumbTimePos?.ToString(CultureInfo.InvariantCulture)
                    ?? "",
                ["priority"] = priority.ToString(),
                ["attemptCount"] = Math.Max(0, leasedItem.AttemptCount)
                    .ToString(CultureInfo.InvariantCulture),
                ["ownerInstanceId"] = NormalizeField(leasedItem.OwnerInstanceId),
                ["leaseBucketRank"] = leasedItem.LeaseBucketRank.ToString(
                    CultureInfo.InvariantCulture
                ),
                ["leaseOrder"] = leasedItem.LeaseOrder.ToString(CultureInfo.InvariantCulture),
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

        private static string GetMetricValue(WorkerJobResultDto result, string key)
        {
            if (result?.Metrics == null || string.IsNullOrWhiteSpace(key))
            {
                return "";
            }

            return result.Metrics.TryGetValue(key, out string value) ? value : "";
        }

        private static string GetDiagnosticValue(WorkerJobRequestDto request, string key)
        {
            if (request?.DiagnosticContext == null || string.IsNullOrWhiteSpace(key))
            {
                return "";
            }

            return request.DiagnosticContext.TryGetValue(key, out string value) ? value : "";
        }

        private static string GetProgressMetricValue(WorkerJobProgressDto progress, string key)
        {
            if (progress?.Metrics == null || string.IsNullOrWhiteSpace(key))
            {
                return "";
            }

            return progress.Metrics.TryGetValue(key, out string value) ? value : "";
        }

        private static string FirstNonEmpty(params string[] values)
        {
            for (int i = 0; i < (values?.Length ?? 0); i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return values[i];
                }
            }

            return "";
        }

        private static string FormatLogValue(string value)
        {
            string normalized = NormalizeLogValue(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "''";
            }

            if (normalized.Any(char.IsWhiteSpace))
            {
                return $"'{normalized.Replace("'", "\\'", StringComparison.Ordinal)}'";
            }

            return normalized;
        }

        private static string NormalizeLogValue(string value)
        {
            return NormalizeField(value)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Replace("\t", " ", StringComparison.Ordinal);
        }
    }
}
