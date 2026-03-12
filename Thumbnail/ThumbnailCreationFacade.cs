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
            ThumbnailRuntimeLog.Write(
                "thumbnail-facade",
                $"enter movie='{movieFullPath}' tab={queueObj?.Tabindex} rescue={queueObj?.IsRescueRequest} attempt={queueObj?.AttemptCount}"
            );

            CachedMovieMetaLookup cacheLookup = ThumbnailMovieMetaCache.GetOrCreate(
                movieFullPath,
                queueObj?.Hash
            );
            CachedMovieMeta cacheMeta = cacheLookup.Meta;
            ThumbnailRuntimeLog.Write(
                "thumbnail-facade",
                $"cache ready movie='{movieFullPath}' hash='{cacheMeta?.Hash}' duration_sec={cacheMeta?.DurationSec:0.###}"
            );
            string hash = cacheMeta.Hash;
            double? durationSec = cacheMeta.DurationSec;
            WriteCacheDurationDebugLog(movieFullPath, "cache-read", durationSec, queueObj);
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
            ThumbnailRuntimeLog.Write(
                "thumbnail-facade",
                $"path ready movie='{movieFullPath}' output='{saveThumbFileName}'"
            );
            OutputFileLockEntry outputLock = await ThumbnailOutputLockManager.AcquireAsync(
                saveThumbFileName,
                cts
            );
            ThumbnailRuntimeLog.Write(
                "thumbnail-facade",
                $"lock acquired movie='{movieFullPath}' output='{saveThumbFileName}'"
            );
            ThumbnailResultCompletionCoordinator completionCoordinator = new(
                isManual,
                tabInfo,
                movieFullPath,
                value => ThumbnailMovieMetaCache.UpdateDuration(cacheLookup.CacheKey, cacheMeta, value),
                durationSec,
                queueObj?.AttemptCount ?? 0
            );

            try
            {
                ThumbnailRuntimeLog.Write(
                    "thumbnail-facade",
                    $"orchestration begin movie='{movieFullPath}'"
                );
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
                ThumbnailRuntimeLog.Write(
                    "thumbnail-facade",
                    $"orchestration end movie='{movieFullPath}' success={orchestration?.Result?.IsSuccess} err='{orchestration?.Result?.ErrorMessage}'"
                );
                return orchestration.Result;
            }
            finally
            {
                // 自動修復で作った一時動画は必ず片付ける。
                TryDeleteFileQuietly(repairedMovieTempPath);
                ThumbnailOutputLockManager.Release(saveThumbFileName, outputLock);
                ThumbnailRuntimeLog.Write(
                    "thumbnail-facade",
                    $"lock released movie='{movieFullPath}' output='{saveThumbFileName}'"
                );
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

        private static void WriteCacheDurationDebugLog(
            string moviePath,
            string phase,
            double? durationSec,
            QueueObj queueObj
        )
        {
            string ext = Path.GetExtension(moviePath ?? "");
            if (
                !string.Equals(ext, ".avi", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ext, ".divx", StringComparison.OrdinalIgnoreCase)
            )
            {
                return;
            }

            string queueTimeText = queueObj?.ThumbTimePos?.ToString() ?? "";
            string queuePanelText = queueObj?.ThumbPanelPos?.ToString() ?? "";
            ThumbnailRuntimeLog.Write(
                "thumbinfo-cache",
                $"phase={phase} movie='{moviePath}' duration_sec={durationSec:0.###} "
                    + $"queue_panel={queuePanelText} queue_time={queueTimeText}"
            );
        }
    }
}
