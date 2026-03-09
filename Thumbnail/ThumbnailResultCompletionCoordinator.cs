namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// CreateThumbAsync の返却締め処理を保持する。
    /// service から local function を消し、結果確定の責務をまとめる。
    /// </summary>
    internal sealed class ThumbnailResultCompletionCoordinator
    {
        private readonly bool isManual;
        private readonly TabInfo tabInfo;
        private readonly string movieFullPath;
        private readonly Action<double?> onCacheDuration;
        private readonly int attemptCount;
        private double? cachedDurationSec;

        public ThumbnailResultCompletionCoordinator(
            bool isManual,
            TabInfo tabInfo,
            string movieFullPath,
            Action<double?> onCacheDuration,
            double? cachedDurationSec,
            int attemptCount
        )
        {
            this.isManual = isManual;
            this.tabInfo = tabInfo ?? throw new ArgumentNullException(nameof(tabInfo));
            this.movieFullPath = movieFullPath ?? "";
            this.onCacheDuration =
                onCacheDuration ?? throw new ArgumentNullException(nameof(onCacheDuration));
            this.cachedDurationSec = cachedDurationSec;
            this.attemptCount = Math.Max(0, attemptCount);
        }

        public void UpdateCachedDuration(double? durationSec)
        {
            cachedDurationSec = durationSec;
        }

        public ThumbnailCreateResult Complete(
            ThumbnailCreateResult result,
            string processEngineId,
            string videoCodec,
            long fileSizeBytes
        )
        {
            return ThumbnailResultFinalizer.FinalizeAndLog(
                new ThumbnailResultFinalizeRequest
                {
                    IsManual = isManual,
                    Result = result,
                    TabInfo = tabInfo,
                    MovieFullPath = movieFullPath,
                    ProcessEngineId = processEngineId,
                    VideoCodec = videoCodec,
                    FileSizeBytes = fileSizeBytes,
                    CachedDurationSec = cachedDurationSec,
                    OnCacheDuration = HandleCacheDuration,
                    AttemptCount = attemptCount,
                }
            );
        }

        // キャッシュ更新後に手元の秒数も合わせ、後続の完了処理へ同じ値を流す。
        private void HandleCacheDuration(double? durationSec)
        {
            cachedDurationSec = durationSec;
            onCacheDuration(durationSec);
        }
    }
}
