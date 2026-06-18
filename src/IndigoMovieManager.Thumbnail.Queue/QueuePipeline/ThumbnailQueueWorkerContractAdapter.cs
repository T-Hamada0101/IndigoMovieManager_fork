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

        private static string BuildJobId(QueueRequest request)
        {
            string key = string.IsNullOrWhiteSpace(request.MoviePathKey)
                ? QueueDbPathResolver.CreateMoviePathKey(request.MoviePath ?? "")
                : request.MoviePathKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                key = "empty";
            }

            return string.Create(
                CultureInfo.InvariantCulture,
                $"thumbnail-{key}-{request.RequestedAtUtc:yyyyMMddHHmmssfff}"
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
                ["moviePathKey"] = string.IsNullOrWhiteSpace(request.MoviePathKey)
                    ? QueueDbPathResolver.CreateMoviePathKey(request.MoviePath ?? "")
                    : request.MoviePathKey,
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
    }
}
