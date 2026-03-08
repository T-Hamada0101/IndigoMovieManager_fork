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

            ThumbnailFailureFinalizer.WriteErrorMarkerIfNeeded(
                request.IsManual,
                request.Result,
                request.TabInfo,
                request.MovieFullPath
            );

            if (
                (!request.CachedDurationSec.HasValue || request.CachedDurationSec.Value <= 0)
                && request.Result.DurationSec.HasValue
                && request.Result.DurationSec.Value > 0
            )
            {
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
    }
}
