using System;
using System.Collections.Generic;
using System.Globalization;
using IndigoMovieManager.Thumbnail.Ipc;

namespace IndigoMovieManager.Watcher
{
    // watch の metadata probe 入力を、UI を知らない Worker 契約へ写すための薄い要求。
    internal sealed record WatchMetadataProbeRequest
    {
        private DateTime requestedAtUtc = DateTime.UtcNow;

        public string MoviePath { get; init; } = "";
        public long ExistingMovieLengthSeconds { get; init; }
        public bool HasFileDateDirty { get; init; }
        public bool HasMovieSizeDirty { get; init; }
        public string Source { get; init; } = "watch-scan";

        public bool HasCheapDirtyFields => HasFileDateDirty || HasMovieSizeDirty;

        public DateTime RequestedAtUtc
        {
            get => requestedAtUtc;
            init => requestedAtUtc = NormalizeUtc(value);
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Local)
            {
                return value.ToUniversalTime();
            }

            if (value.Kind == DateTimeKind.Unspecified)
            {
                return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            }

            return value;
        }
    }

    // probe 実行結果を request / result / artifact 語彙へ畳むための軽量結果。
    internal sealed record WatchMetadataProbeResult
    {
        private DateTime finishedAtUtc = DateTime.UtcNow;

        public string JobId { get; init; } = "";
        public string MoviePath { get; init; } = "";
        public long? MovieLengthSeconds { get; init; }
        public bool Succeeded { get; init; }
        public string FailureKind { get; init; } = "";
        public string FailureReason { get; init; } = "";
        public bool Retryable { get; init; }
        public long ElapsedMs { get; init; }

        public DateTime FinishedAtUtc
        {
            get => finishedAtUtc;
            init => finishedAtUtc = NormalizeUtc(value);
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Local)
            {
                return value.ToUniversalTime();
            }

            if (value.Kind == DateTimeKind.Unspecified)
            {
                return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            }

            return value;
        }
    }

    // probe の軽量進捗を Worker 進捗語彙へ写すための入口。
    internal sealed record WatchMetadataProbeProgress
    {
        private DateTime capturedAtUtc = DateTime.UtcNow;

        public string JobId { get; init; } = "";
        public string MoviePath { get; init; } = "";
        public string Stage { get; init; } = "running";
        public int CompletedCount { get; init; }
        public int TotalCount { get; init; }
        public string Message { get; init; } = "";

        public DateTime CapturedAtUtc
        {
            get => capturedAtUtc;
            init => capturedAtUtc = NormalizeUtc(value);
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Local)
            {
                return value.ToUniversalTime();
            }

            if (value.Kind == DateTimeKind.Unspecified)
            {
                return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            }

            return value;
        }
    }

    // 既存 probe の実行方式は変えず、将来 worker 化できる入出力形だけを先に固定する。
    internal static class WatchMetadataProbeWorkerContractAdapter
    {
        public const string WorkerKind = "metadata-probe";
        public const string ProbeCapability = "watch-metadata-probe";
        public const string ResultStatusSucceeded = "succeeded";
        public const string ResultStatusFailed = "failed";
        public const string ResultRetryable = "retryable";
        public const string ResultNotRetryable = "not-retryable";
        public const string ResultArtifactKind = "metadata-probe-state";
        public const string ProgressStageQueued = "queued";
        public const string ProgressStageRunning = "running";
        public const string ProgressStageCompleted = "completed";
        private const string ResultArtifactContentType = "application/x.indigo.metadata-probe";

        public static WorkerJobRequestDto ToWorkerJobRequestDto(
            WatchMetadataProbeRequest request,
            string outputArtifactPath = "",
            long timeoutMs = 0
        )
        {
            request ??= new WatchMetadataProbeRequest();
            string moviePath = NormalizeField(request.MoviePath);
            string moviePathKey = BuildMoviePathKey(moviePath);

            return new WorkerJobRequestDto
            {
                JobId = BuildJobId(moviePathKey, request.RequestedAtUtc),
                Kind = WorkerKind,
                InputFiles = string.IsNullOrWhiteSpace(moviePath) ? [] : [moviePath],
                OutputArtifactPath = outputArtifactPath ?? "",
                TimeoutMs = Math.Max(0, timeoutMs),
                Capabilities = BuildCapabilities(request),
                DiagnosticContext = BuildDiagnosticContext(request, moviePathKey),
                RequestedAtUtc = request.RequestedAtUtc,
            };
        }

        public static WorkerJobResultDto ToWorkerJobResultDto(
            WatchMetadataProbeResult result,
            IReadOnlyDictionary<string, string> metrics = null
        )
        {
            result ??= new WatchMetadataProbeResult();
            string status = result.Succeeded ? ResultStatusSucceeded : ResultStatusFailed;
            string failureKind = result.Succeeded ? "" : NormalizeField(result.FailureKind);
            string failureReason = ResolveFailureReason(
                result.Succeeded,
                failureKind,
                result.FailureReason
            );
            long elapsedMs = Math.Max(0, result.ElapsedMs);

            return new WorkerJobResultDto
            {
                JobId = NormalizeField(result.JobId),
                Status = status,
                Artifact = BuildWorkerArtifact(result),
                FailureReason = failureReason,
                ElapsedMs = elapsedMs,
                Retryability =
                    !result.Succeeded && result.Retryable ? ResultRetryable : ResultNotRetryable,
                Logs = BuildResultLogs(result, status, failureKind, failureReason),
                Metrics = BuildResultMetrics(
                    result,
                    status,
                    failureKind,
                    result.Retryable,
                    elapsedMs,
                    metrics
                ),
                FinishedAtUtc = result.FinishedAtUtc,
            };
        }

        public static WorkerJobProgressDto ToWorkerJobProgressDto(
            WatchMetadataProbeProgress progress,
            IReadOnlyDictionary<string, string> metrics = null
        )
        {
            progress ??= new WatchMetadataProbeProgress();
            string normalizedStage = NormalizeField(progress.Stage);
            if (string.IsNullOrWhiteSpace(normalizedStage))
            {
                normalizedStage = ProgressStageRunning;
            }

            int completedCount = Math.Max(0, progress.CompletedCount);
            int totalCount = NormalizeTotalCount(completedCount, progress.TotalCount);

            return new WorkerJobProgressDto
            {
                JobId = ResolveProgressJobId(progress),
                Stage = normalizedStage,
                CompletedCount = completedCount,
                TotalCount = totalCount,
                CurrentInputFile = NormalizeField(progress.MoviePath),
                Message = NormalizeField(progress.Message),
                Metrics = BuildProgressMetrics(
                    progress,
                    normalizedStage,
                    completedCount,
                    totalCount,
                    metrics
                ),
                CapturedAtUtc = progress.CapturedAtUtc,
            };
        }

        public static string BuildWorkerJobRequestLogFields(WorkerJobRequestDto request)
        {
            request ??= new WorkerJobRequestDto();

            return string.Create(
                CultureInfo.InvariantCulture,
                $"worker_job_id={FormatLogValue(request.JobId)} worker_kind={FormatLogValue(request.Kind)} input_count={Math.Max(0, request.InputFiles?.Count ?? 0)} capability_count={Math.Max(0, request.Capabilities?.Count ?? 0)} diagnostic_context_count={Math.Max(0, request.DiagnosticContext?.Count ?? 0)} output_artifact_path={FormatLogValue(request.OutputArtifactPath)} timeout_ms={Math.Max(0, request.TimeoutMs)} source={FormatLogValue(GetDiagnosticValue(request, "source"))} has_cheap_dirty_fields={FormatLogValue(GetDiagnosticValue(request, "hasCheapDirtyFields"))} existing_movie_length_seconds={FormatLogValue(GetDiagnosticValue(request, "existingMovieLengthSeconds"))}"
            );
        }

        public static string BuildWorkerJobProgressLogFields(WorkerJobProgressDto progress)
        {
            progress ??= new WorkerJobProgressDto();

            return string.Create(
                CultureInfo.InvariantCulture,
                $"worker_job_id={FormatLogValue(progress.JobId)} worker_kind={FormatLogValue(WorkerKind)} worker_stage={FormatLogValue(progress.Stage)} progress_completed={Math.Max(0, progress.CompletedCount)} progress_total={Math.Max(0, progress.TotalCount)} current_input_file={FormatLogValue(progress.CurrentInputFile)} movie_path_key={FormatLogValue(GetProgressMetricValue(progress, "moviePathKey"))}"
            );
        }

        public static string BuildWorkerJobResultLogFields(WorkerJobResultDto result)
        {
            result ??= new WorkerJobResultDto();
            WorkerJobArtifactDto artifact = result.Artifact ?? new WorkerJobArtifactDto();

            return string.Create(
                CultureInfo.InvariantCulture,
                $"worker_job_id={FormatLogValue(result.JobId)} worker_kind={FormatLogValue(WorkerKind)} worker_status={FormatLogValue(result.Status)} artifact_kind={FormatLogValue(artifact.ArtifactKind)} retryability={FormatLogValue(result.Retryability)} retryable={FormatLogValue(GetMetricValue(result, "retryable"))} elapsed_ms={Math.Max(0, result.ElapsedMs)} failure_kind={FormatLogValue(GetMetricValue(result, "failureKind"))} failure_reason={FormatLogValue(result.FailureReason)} movie_length_seconds={FormatLogValue(GetMetricValue(result, "movieLengthSeconds"))}"
            );
        }

        public static string BuildWorkerProbeLogFields(
            WorkerJobRequestDto request,
            WorkerJobProgressDto progress,
            WorkerJobResultDto result
        )
        {
            request ??= new WorkerJobRequestDto();
            progress ??= new WorkerJobProgressDto();
            result ??= new WorkerJobResultDto();
            WorkerJobArtifactDto artifact = result.Artifact ?? new WorkerJobArtifactDto();

            // 既存ログへ併記する値だけをDTOから拾い、probe本体の挙動へ影響させない。
            return string.Create(
                CultureInfo.InvariantCulture,
                $"worker_job_id={FormatLogValue(result.JobId)} worker_kind={FormatLogValue(request.Kind)} worker_status={FormatLogValue(result.Status)} worker_stage={FormatLogValue(progress.Stage)} artifact_kind={FormatLogValue(artifact.ArtifactKind)} retryability={FormatLogValue(result.Retryability)} retryable={FormatLogValue(GetMetricValue(result, "retryable"))} elapsed_ms={Math.Max(0, result.ElapsedMs)} failure_kind={FormatLogValue(GetMetricValue(result, "failureKind"))} failure_reason={FormatLogValue(result.FailureReason)} progress_completed={Math.Max(0, progress.CompletedCount)} progress_total={Math.Max(0, progress.TotalCount)} input_count={Math.Max(0, request.InputFiles?.Count ?? 0)} capability_count={Math.Max(0, request.Capabilities?.Count ?? 0)} diagnostic_context_count={Math.Max(0, request.DiagnosticContext?.Count ?? 0)} current_input_file={FormatLogValue(progress.CurrentInputFile)} movie_path_key={FormatLogValue(FirstNonEmpty(GetMetricValue(result, "moviePathKey"), GetProgressMetricValue(progress, "moviePathKey"), GetDiagnosticValue(request, "moviePathKey")))} source={FormatLogValue(GetDiagnosticValue(request, "source"))} has_cheap_dirty_fields={FormatLogValue(GetDiagnosticValue(request, "hasCheapDirtyFields"))} existing_movie_length_seconds={FormatLogValue(GetDiagnosticValue(request, "existingMovieLengthSeconds"))} movie_length_seconds={FormatLogValue(GetMetricValue(result, "movieLengthSeconds"))} output_artifact_path={FormatLogValue(request.OutputArtifactPath)} timeout_ms={Math.Max(0, request.TimeoutMs)}"
            );
        }

        private static string BuildJobId(string moviePathKey, DateTime requestedAtUtc)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"metadata-probe-{NormalizeField(moviePathKey)}-{requestedAtUtc:yyyyMMddHHmmssfff}"
            );
        }

        private static List<string> BuildCapabilities(WatchMetadataProbeRequest request)
        {
            List<string> capabilities = [ProbeCapability, WorkerKind];
            if (request.HasCheapDirtyFields)
            {
                capabilities.Add("cheap-dirty");
            }

            if (request.ExistingMovieLengthSeconds < 1)
            {
                capabilities.Add("length-missing");
            }

            return capabilities;
        }

        private static Dictionary<string, string> BuildDiagnosticContext(
            WatchMetadataProbeRequest request,
            string moviePathKey
        )
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["contractSource"] = "watch-scan",
                ["source"] = NormalizeField(request.Source),
                ["moviePathKey"] = NormalizeField(moviePathKey),
                ["existingMovieLengthSeconds"] = Math.Max(0, request.ExistingMovieLengthSeconds)
                    .ToString(CultureInfo.InvariantCulture),
                ["hasFileDateDirty"] = request.HasFileDateDirty ? "true" : "false",
                ["hasMovieSizeDirty"] = request.HasMovieSizeDirty ? "true" : "false",
                ["hasCheapDirtyFields"] = request.HasCheapDirtyFields ? "true" : "false",
            };
        }

        private static WorkerJobArtifactDto BuildWorkerArtifact(WatchMetadataProbeResult result)
        {
            bool hasProbeState = result.Succeeded && result.MovieLengthSeconds.HasValue;
            string moviePathKey = BuildMoviePathKey(result.MoviePath);

            return new WorkerJobArtifactDto
            {
                ArtifactKind = hasProbeState ? ResultArtifactKind : "",
                Path = "",
                ContentType = hasProbeState ? ResultArtifactContentType : "",
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["moviePathKey"] = moviePathKey,
                    ["movieLengthSeconds"] = result.MovieLengthSeconds.HasValue
                        ? Math.Max(0, result.MovieLengthSeconds.Value)
                            .ToString(CultureInfo.InvariantCulture)
                        : "",
                },
            };
        }

        private static Dictionary<string, string> BuildResultMetrics(
            WatchMetadataProbeResult result,
            string status,
            string failureKind,
            bool retryable,
            long elapsedMs,
            IReadOnlyDictionary<string, string> metrics
        )
        {
            Dictionary<string, string> resultMetrics =
                new(StringComparer.OrdinalIgnoreCase)
                {
                    ["moviePathKey"] = BuildMoviePathKey(result.MoviePath),
                    ["movieLengthSeconds"] = result.MovieLengthSeconds.HasValue
                        ? Math.Max(0, result.MovieLengthSeconds.Value)
                            .ToString(CultureInfo.InvariantCulture)
                        : "",
                    ["status"] = NormalizeField(status),
                    ["failureKind"] = NormalizeField(failureKind),
                    ["retryable"] = retryable ? "true" : "false",
                    ["elapsedMs"] = Math.Max(0, elapsedMs).ToString(CultureInfo.InvariantCulture),
                };

            if (metrics == null)
            {
                return resultMetrics;
            }

            foreach (KeyValuePair<string, string> pair in metrics)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                resultMetrics[pair.Key.Trim()] = pair.Value ?? "";
            }

            return resultMetrics;
        }

        private static Dictionary<string, string> BuildProgressMetrics(
            WatchMetadataProbeProgress progress,
            string stage,
            int completedCount,
            int totalCount,
            IReadOnlyDictionary<string, string> metrics
        )
        {
            Dictionary<string, string> progressMetrics =
                new(StringComparer.OrdinalIgnoreCase)
                {
                    ["workerKind"] = WorkerKind,
                    ["moviePathKey"] = BuildMoviePathKey(progress.MoviePath),
                    ["stage"] = NormalizeField(stage),
                    ["completedCount"] = completedCount.ToString(CultureInfo.InvariantCulture),
                    ["totalCount"] = totalCount.ToString(CultureInfo.InvariantCulture),
                };

            if (metrics == null)
            {
                return progressMetrics;
            }

            foreach (KeyValuePair<string, string> pair in metrics)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                progressMetrics[pair.Key.Trim()] = pair.Value ?? "";
            }

            return progressMetrics;
        }

        private static List<string> BuildResultLogs(
            WatchMetadataProbeResult result,
            string status,
            string failureKind,
            string failureReason
        )
        {
            List<string> logs =
            [
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"metadata probe result: job_id={NormalizeField(result.JobId)} status={NormalizeField(status)} elapsed_ms={Math.Max(0, result.ElapsedMs)}"
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

        private static string BuildMoviePathKey(string moviePath)
        {
            string normalizedPath = NormalizeField(moviePath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return "empty";
            }

            unchecked
            {
                uint hash = 2166136261;
                foreach (char value in normalizedPath.ToUpperInvariant())
                {
                    hash ^= value;
                    hash *= 16777619;
                }

                return hash.ToString("x8", CultureInfo.InvariantCulture);
            }
        }

        private static string ResolveProgressJobId(WatchMetadataProbeProgress progress)
        {
            string jobId = NormalizeField(progress.JobId);
            if (!string.IsNullOrWhiteSpace(jobId))
            {
                return jobId;
            }

            return BuildJobId(BuildMoviePathKey(progress.MoviePath), progress.CapturedAtUtc);
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

            if (ContainsWhiteSpace(normalized))
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

        private static bool ContainsWhiteSpace(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            foreach (char current in value)
            {
                if (char.IsWhiteSpace(current))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
