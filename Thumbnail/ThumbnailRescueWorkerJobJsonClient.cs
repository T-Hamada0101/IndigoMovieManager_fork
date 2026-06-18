using System.Reflection;
using System.Text;
using System.Text.Json;
using System.IO;
using IndigoMovieManager.Thumbnail.Ipc;

namespace IndigoMovieManager.Thumbnail
{
    // Public repo 側は、job/result JSON を薄い adapter として扱い、engine の実装詳細を持たない。
    internal static class ThumbnailRescueWorkerJobJsonClient
    {
        internal const string Command = "rescue";
        internal const string SupportedEntryMode = "rescue-job-json";
        internal const string JobJsonFileName = "rescue-worker.job.json";
        internal const string ResultJsonFileName = "rescue-worker.result.json";
        internal const string ContractVersion = "1";
        internal const string Mode = "rescue-main";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        internal static string BuildRequestId()
        {
            return $"rescue-{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}";
        }

        internal static string BuildJobJsonPath(string sessionDirectory)
        {
            return Path.Combine(NormalizeDirectoryPath(sessionDirectory), JobJsonFileName);
        }

        internal static string BuildResultJsonPath(string sessionDirectory)
        {
            return Path.Combine(NormalizeDirectoryPath(sessionDirectory), ResultJsonFileName);
        }

        internal static string BuildWorkerArguments(string jobJsonPath, string resultJsonPath)
        {
            List<string> args =
            [
                Command,
                "--job-json",
                QuoteArgument(jobJsonPath),
                "--result-json",
                QuoteArgument(resultJsonPath),
            ];

            return string.Join(" ", args);
        }

        internal static ThumbnailRescueWorkerMainJobRequest CreateMainJobRequest(
            string mainDbFullPath,
            string thumbFolderOverride,
            string logDirectoryPath,
            string failureDbDirectoryPath,
            long requestedFailureId,
            string requestId = ""
        )
        {
            return new ThumbnailRescueWorkerMainJobRequest
            {
                ContractVersion = ContractVersion,
                Mode = Mode,
                RequestId = string.IsNullOrWhiteSpace(requestId)
                    ? BuildRequestId()
                    : requestId.Trim(),
                MainDbFullPath = mainDbFullPath ?? "",
                ThumbFolderOverride = thumbFolderOverride ?? "",
                LogDirectoryPath = logDirectoryPath ?? "",
                FailureDbDirectoryPath = failureDbDirectoryPath ?? "",
                RequestedFailureId = requestedFailureId > 0 ? requestedFailureId : 0,
                Metadata =
                {
                    ["caller"] = "IndigoMovieManager",
                    ["callerVersion"] = ResolveCallerVersion(),
                },
            };
        }

        internal static WorkerJobRequestDto ToWorkerJobRequestDto(
            ThumbnailRescueWorkerMainJobRequest request,
            string outputArtifactPath = "",
            long timeoutMs = 0
        )
        {
            request ??= new ThumbnailRescueWorkerMainJobRequest();
            Dictionary<string, string> diagnosticContext = new(
                request.Metadata ?? [],
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["contractVersion"] = request.ContractVersion ?? "",
                ["mode"] = request.Mode ?? "",
                ["thumbFolderOverride"] = request.ThumbFolderOverride ?? "",
                ["logDirectoryPath"] = request.LogDirectoryPath ?? "",
                ["failureDbDirectoryPath"] = request.FailureDbDirectoryPath ?? "",
                ["requestedFailureId"] = request.RequestedFailureId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture
                ),
            };

            List<string> inputFiles = [];
            if (!string.IsNullOrWhiteSpace(request.MainDbFullPath))
            {
                inputFiles.Add(request.MainDbFullPath);
            }

            return new WorkerJobRequestDto
            {
                JobId = request.RequestId ?? "",
                Kind = "thumbnail-rescue",
                InputFiles = inputFiles,
                OutputArtifactPath = outputArtifactPath ?? "",
                TimeoutMs = Math.Max(0, timeoutMs),
                Capabilities = [SupportedEntryMode, Mode],
                DiagnosticContext = diagnosticContext,
            };
        }

        internal static WorkerJobResultDto ToWorkerJobResultDto(
            ThumbnailRescueWorkerMainJobResult result
        )
        {
            result ??= new ThumbnailRescueWorkerMainJobResult();
            ThumbnailRescueWorkerMainJobArtifact firstArtifact =
                result.Artifacts?.FirstOrDefault() ?? new ThumbnailRescueWorkerMainJobArtifact();
            string failureReason = ResolveWorkerFailureReason(result);
            long elapsedMs = ResolveWorkerElapsedMs(result.StartedAt, result.FinishedAt);

            return new WorkerJobResultDto
            {
                JobId = result.RequestId ?? "",
                Status = result.Status ?? "",
                Artifact = new WorkerJobArtifactDto
                {
                    ArtifactKind = firstArtifact.Type ?? "",
                    Path = firstArtifact.Path ?? "",
                },
                FailureReason = failureReason,
                ElapsedMs = elapsedMs,
                Retryability = ResolveWorkerRetryability(result.Status, failureReason),
                Logs = string.IsNullOrWhiteSpace(result.Message) ? [] : [result.Message.Trim()],
                Metrics = new Dictionary<string, string>
                {
                    ["resultCode"] = result.ResultCode ?? "",
                    ["engineVersion"] = result.EngineVersion ?? "",
                    ["compatibilityVersion"] = result.CompatibilityVersion ?? "",
                },
            };
        }

        internal static bool TryWriteMainJobRequest(
            string jobJsonPath,
            ThumbnailRescueWorkerMainJobRequest request,
            out string diagnosticMessage
        )
        {
            diagnosticMessage = "";
            string normalizedJobJsonPath = NormalizeFilePath(jobJsonPath);
            if (string.IsNullOrWhiteSpace(normalizedJobJsonPath))
            {
                diagnosticMessage = "job json path is empty.";
                return false;
            }

            try
            {
                Directory.CreateDirectory(
                    Path.GetDirectoryName(normalizedJobJsonPath) ?? Path.GetTempPath()
                );
                string json = JsonSerializer.Serialize(request ?? new ThumbnailRescueWorkerMainJobRequest(), JsonOptions);
                File.WriteAllText(normalizedJobJsonPath, json, new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                diagnosticMessage = $"job json write failed: {ex.Message}";
                return false;
            }
        }

        internal static bool TryReadMainJobResult(
            string resultJsonPath,
            string expectedRequestId,
            out ThumbnailRescueWorkerMainJobResult result,
            out string diagnosticMessage
        )
        {
            result = null;
            diagnosticMessage = "";
            string normalizedResultJsonPath = NormalizeFilePath(resultJsonPath);
            if (string.IsNullOrWhiteSpace(normalizedResultJsonPath) || !File.Exists(normalizedResultJsonPath))
            {
                diagnosticMessage = "result json is missing.";
                return false;
            }

            try
            {
                string json = File.ReadAllText(normalizedResultJsonPath, Encoding.UTF8);
                result = JsonSerializer.Deserialize<ThumbnailRescueWorkerMainJobResult>(
                    json,
                    JsonOptions
                );
                if (result == null)
                {
                    diagnosticMessage = "result json could not be deserialized.";
                    return false;
                }

                if (!string.Equals(result.ContractVersion, ContractVersion, StringComparison.Ordinal))
                {
                    diagnosticMessage =
                        $"result contractVersion mismatch: expected='{ContractVersion}' actual='{result.ContractVersion ?? ""}'.";
                    result = null;
                    return false;
                }

                if (!string.Equals(result.Mode, Mode, StringComparison.Ordinal))
                {
                    diagnosticMessage =
                        $"result mode mismatch: expected='{Mode}' actual='{result.Mode ?? ""}'.";
                    result = null;
                    return false;
                }

                if (string.IsNullOrWhiteSpace(result.RequestId))
                {
                    diagnosticMessage = "result requestId is empty.";
                    result = null;
                    return false;
                }

                if (
                    !string.IsNullOrWhiteSpace(expectedRequestId)
                    && !string.Equals(
                        result.RequestId,
                        expectedRequestId.Trim(),
                        StringComparison.Ordinal
                    )
                )
                {
                    diagnosticMessage =
                        $"result requestId mismatch: expected='{expectedRequestId.Trim()}' actual='{result.RequestId}'.";
                    result = null;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                diagnosticMessage = $"result json read failed: {ex.Message}";
                return false;
            }
        }

        internal static string BuildResultSummaryLine(
            ThumbnailRescueWorkerMainJobResult result,
            string defaultPrefix = "rescue worker result"
        )
        {
            if (result == null)
            {
                return $"{defaultPrefix}: unavailable";
            }

            return
                $"{defaultPrefix}: request_id={result.RequestId} status={result.Status} result_code={result.ResultCode} message='{(result.Message ?? "").Trim()}'";
        }

        private static string QuoteArgument(string value)
        {
            string normalized = NormalizeFilePath(value);
            return string.IsNullOrWhiteSpace(normalized) ? "\"\"" : $"\"{normalized}\"";
        }

        private static string NormalizeDirectoryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            try
            {
                return Path.GetFullPath(path.Trim());
            }
            catch
            {
                return path.Trim();
            }
        }

        private static string NormalizeFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            string trimmed = path.Trim();
            if (trimmed.Length >= 2 && trimmed.StartsWith('"') && trimmed.EndsWith('"'))
            {
                trimmed = trimmed[1..^1].Trim();
            }

            try
            {
                return Path.GetFullPath(trimmed);
            }
            catch
            {
                return trimmed;
            }
        }

        private static string ResolveCallerVersion()
        {
            Assembly assembly = typeof(ThumbnailRescueWorkerJobJsonClient).Assembly;
            string informationalVersion =
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion ?? "";
            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                return informationalVersion.Split('+', 2)[0];
            }

            Version version = assembly.GetName().Version;
            if (version == null)
            {
                return "0.0.0";
            }

            return version.Build >= 0
                ? $"{version.Major}.{version.Minor}.{version.Build}"
                : $"{version.Major}.{version.Minor}";
        }

        private static string ResolveWorkerFailureReason(ThumbnailRescueWorkerMainJobResult result)
        {
            if (result?.Errors != null && result.Errors.Count > 0)
            {
                ThumbnailRescueWorkerMainJobError firstError = result.Errors[0];
                string code = firstError.Code ?? "";
                string message = firstError.Message ?? "";
                string target = firstError.Target ?? "";
                return string.Join(
                    " ",
                    new[] { code, message, target }
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                );
            }

            bool succeeded = string.Equals(result?.Status, "success", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result?.Status, "succeeded", StringComparison.OrdinalIgnoreCase);
            return succeeded ? "" : result?.Message?.Trim() ?? "";
        }

        private static string ResolveWorkerRetryability(string status, string failureReason)
        {
            if (string.IsNullOrWhiteSpace(failureReason))
            {
                return "not-retryable";
            }

            return string.Equals(status, "canceled", StringComparison.OrdinalIgnoreCase)
                ? "retryable"
                : "unknown";
        }

        private static long ResolveWorkerElapsedMs(DateTimeOffset startedAt, DateTimeOffset finishedAt)
        {
            if (startedAt == default || finishedAt == default || finishedAt < startedAt)
            {
                return 0;
            }

            return Math.Max(0, (long)(finishedAt - startedAt).TotalMilliseconds);
        }
    }

    internal sealed class ThumbnailRescueWorkerMainJobRequest
    {
        public string ContractVersion { get; set; } = ThumbnailRescueWorkerJobJsonClient.ContractVersion;

        public string Mode { get; set; } = ThumbnailRescueWorkerJobJsonClient.Mode;

        public string RequestId { get; set; } = "";

        public string MainDbFullPath { get; set; } = "";

        public string ThumbFolderOverride { get; set; } = "";

        public string LogDirectoryPath { get; set; } = "";

        public string FailureDbDirectoryPath { get; set; } = "";

        public long RequestedFailureId { get; set; }

        public Dictionary<string, string> Metadata { get; set; } = [];
    }

    internal sealed class ThumbnailRescueWorkerMainJobResult
    {
        public string ContractVersion { get; set; } = "";

        public string Mode { get; set; } = "";

        public string RequestId { get; set; } = "";

        public string Status { get; set; } = "";

        public string ResultCode { get; set; } = "";

        public string Message { get; set; } = "";

        public string EngineVersion { get; set; } = "";

        public string CompatibilityVersion { get; set; } = "";

        public DateTimeOffset StartedAt { get; set; }

        public DateTimeOffset FinishedAt { get; set; }

        public List<ThumbnailRescueWorkerMainJobArtifact> Artifacts { get; set; } = [];

        public List<ThumbnailRescueWorkerMainJobError> Errors { get; set; } = [];
    }

    internal sealed class ThumbnailRescueWorkerMainJobArtifact
    {
        public string Type { get; set; } = "";

        public string Path { get; set; } = "";
    }

    internal sealed class ThumbnailRescueWorkerMainJobError
    {
        public string Code { get; set; } = "";

        public string Message { get; set; } = "";

        public string Target { get; set; } = "";
    }
}
