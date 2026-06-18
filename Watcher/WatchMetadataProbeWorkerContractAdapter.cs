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

        private static string NormalizeField(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        }
    }
}
