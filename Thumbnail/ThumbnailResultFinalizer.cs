namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// CreateThumbAsync の最終返却前処理をまとめる。
    /// error marker、duration cache、process log をここで締める。
    /// </summary>
    internal static class ThumbnailResultFinalizer
    {
        public static ThumbnailCreateResult FinalizeAndLog(
            ThumbnailResultFinalizeRequest request
        )
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            string writeAction = ThumbnailFailureFinalizer.WriteErrorMarkerIfNeeded(
                request.IsManual,
                request.Result,
                request.TabInfo,
                request.MovieFullPath,
                request.AttemptCount
            );
            request.Result.FinalizerAction = writeAction;
            request.Result.FinalizerDetail = "";

            // 再試行中や成功時は固定化マーカーを残さず、再スキャンで拾える状態を保つ。
            if (request.Result?.IsSuccess == true || request.AttemptCount + 1 < 5)
            {
                string deleteAction = ThumbnailFailureFinalizer.DeleteErrorMarkerIfExists(
                    request.TabInfo,
                    request.MovieFullPath
                );
                request.Result.FinalizerAction = deleteAction;
            }

            if (
                (!request.CachedDurationSec.HasValue || request.CachedDurationSec.Value <= 0)
                && request.Result.DurationSec.HasValue
                && request.Result.DurationSec.Value > 0
            )
            {
                WriteCacheUpdateDebugLog(
                    request.MovieFullPath,
                    "result-finalizer",
                    request.Result.DurationSec
                );
                request.OnCacheDuration?.Invoke(request.Result.DurationSec);
            }

            double? loggedDurationSec = request.Result.DurationSec;
            if (
                (!loggedDurationSec.HasValue || loggedDurationSec.Value <= 0)
                && request.CachedDurationSec.HasValue
                && request.CachedDurationSec.Value > 0
            )
            {
                loggedDurationSec = request.CachedDurationSec;
            }

            ThumbnailProcessLogFinalizer.Write(
                request.ProcessEngineId,
                request.MovieFullPath,
                request.VideoCodec,
                loggedDurationSec,
                request.FileSizeBytes,
                request.Result
            );
            return request.Result;
        }

        private static void WriteCacheUpdateDebugLog(
            string movieFullPath,
            string phase,
            double? durationSec
        )
        {
            string ext = Path.GetExtension(movieFullPath ?? "");
            if (
                !string.Equals(ext, ".avi", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ext, ".divx", StringComparison.OrdinalIgnoreCase)
            )
            {
                return;
            }

            ThumbnailRuntimeLog.Write(
                "thumbinfo-cache",
                $"phase={phase} movie='{movieFullPath}' duration_sec={durationSec:0.###}"
            );
        }
    }

    internal sealed class ThumbnailResultFinalizeRequest
    {
        public bool IsManual { get; init; }

        public ThumbnailCreateResult Result { get; init; }

        public TabInfo TabInfo { get; init; }

        public string MovieFullPath { get; init; } = "";

        public string ProcessEngineId { get; init; } = "";

        public string VideoCodec { get; init; } = "";

        public long FileSizeBytes { get; init; }

        public double? CachedDurationSec { get; init; }

        public Action<double?> OnCacheDuration { get; init; }

        public int AttemptCount { get; init; }
    }
}
