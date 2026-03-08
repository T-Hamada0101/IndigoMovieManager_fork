namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// CreateThumbAsync の入口配線をまとめる。
    /// service 本体は公開APIの窓口に寄せ、ここで request 構築と cleanup を扱う。
    /// </summary>
    internal sealed class ThumbnailCreationFacade
    {
        private readonly ThumbnailCreationOrchestrationCoordinator creationOrchestrationCoordinator;

        public ThumbnailCreationFacade(
            ThumbnailCreationOrchestrationCoordinator creationOrchestrationCoordinator
        )
        {
            this.creationOrchestrationCoordinator =
                creationOrchestrationCoordinator
                ?? throw new ArgumentNullException(nameof(creationOrchestrationCoordinator));
        }

        public async Task<ThumbnailCreateResult> ExecuteAsync(
            QueueObj queueObj,
            string dbName,
            string thumbFolder,
            bool isResizeThumb,
            bool isManual,
            CancellationToken cts
        )
        {
            TabInfo tabInfo = new(queueObj.Tabindex, dbName, thumbFolder);
            string movieFullPath = queueObj.MovieFullPath;
            string repairedMovieTempPath = "";

            CachedMovieMetaLookup cacheLookup = ThumbnailMovieMetaCache.GetOrCreate(
                movieFullPath,
                queueObj?.Hash
            );
            CachedMovieMeta cacheMeta = cacheLookup.Meta;
            string hash = cacheMeta.Hash;
            double? durationSec = cacheMeta.DurationSec;
            if (queueObj != null && string.IsNullOrWhiteSpace(queueObj.Hash))
            {
                // 以降の経路でも再利用できるよう、確定済みハッシュを QueueObj へ戻す。
                queueObj.Hash = hash;
            }

            string saveThumbFileName = ThumbnailPathResolver.BuildThumbnailPath(
                tabInfo,
                movieFullPath,
                hash
            );
            OutputFileLockEntry outputLock = await ThumbnailOutputLockManager.AcquireAsync(
                saveThumbFileName,
                cts
            );
            ThumbnailResultCompletionCoordinator completionCoordinator = new(
                isManual,
                tabInfo,
                movieFullPath,
                value => ThumbnailMovieMetaCache.UpdateDuration(cacheLookup.CacheKey, cacheMeta, value),
                durationSec
            );

            try
            {
                ThumbnailCreationOrchestrationResult orchestration =
                    await creationOrchestrationCoordinator
                        .ExecuteAsync(
                            new ThumbnailCreationOrchestrationRequest
                            {
                                QueueObj = queueObj,
                                TabInfo = tabInfo,
                                CacheMeta = cacheMeta,
                                MovieFullPath = movieFullPath,
                                SaveThumbFileName = saveThumbFileName,
                                IsResizeThumb = isResizeThumb,
                                IsManual = isManual,
                                DurationSec = durationSec,
                                CompletionCoordinator = completionCoordinator,
                                OnResolvedDuration = value =>
                                    ThumbnailMovieMetaCache.UpdateDuration(
                                        cacheLookup.CacheKey,
                                        cacheMeta,
                                        value
                                    ),
                            },
                            cts
                        )
                        .ConfigureAwait(false);
                repairedMovieTempPath = orchestration.RepairedMovieTempPath;
                return orchestration.Result;
            }
            finally
            {
                // 自動修復で作った一時動画は必ず片付ける。
                TryDeleteFileQuietly(repairedMovieTempPath);
                ThumbnailOutputLockManager.Release(saveThumbFileName, outputLock);
            }
        }

        private static void TryDeleteFileQuietly(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
                // 一時ファイル削除失敗は後続処理を優先する。
            }
        }
    }
}
