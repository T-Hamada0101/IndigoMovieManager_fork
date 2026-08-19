using System.Collections.Generic;
using System.Threading.Tasks;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // QueueDBのアクティブ件数を安全に取得する。取得不能時はfalseを返して救済判定を継続する。
        private bool TryGetCurrentQueueActiveCount(out int activeCount)
        {
            activeCount = 0;
            try
            {
                var queueDbService = ResolveCurrentQueueDbService();
                if (queueDbService == null)
                {
                    return false;
                }

                activeCount = queueDbService.GetActiveQueueCount(thumbnailQueueOwnerInstanceId);
                return true;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"missing-thumb rescue queue count failed: {ex.Message}"
                );
                return false;
            }
        }

        // 一発移行中は新しいleaseをuser-priorityで止め、実行中writerが抜けてからsnapshotする。
        internal async Task<SharedThumbnailMigrationResult> RunSharedThumbnailMigrationFromSettingsAsync(
            string sourceRoot,
            string targetRoot,
            string sharedRoot
        )
        {
            const string priorityReason = "shared-thumbnail-migration";
            BeginUserPriorityWork(priorityReason);
            try
            {
                if (!await WaitForThumbnailQueueIdleForSettingsMigrationAsync())
                {
                    return SharedThumbnailMigrationResult.Rejected(
                        sourceRoot,
                        targetRoot,
                        "thumbnail-writers-did-not-settle"
                    );
                }

                return await Task.Run(() =>
                    SharedThumbnailStoragePolicy.MigrateThumbnailTreeToSharedStore(
                        sourceRoot,
                        targetRoot,
                        sharedRoot
                    )
                );
            }
            finally
            {
                EndUserPriorityWork(priorityReason);
            }
        }

        // 旧ファイル削除も同じwriter停止境界を使い、生成直後のファイルを誤って消さない。
        internal async Task<SharedThumbnailSourceCleanupResult> RunSharedThumbnailSourceCleanupFromSettingsAsync(
            string sourceRoot,
            string targetRoot,
            string sharedRoot
        )
        {
            const string priorityReason = "shared-thumbnail-source-cleanup";
            BeginUserPriorityWork(priorityReason);
            try
            {
                if (!await WaitForThumbnailQueueIdleForSettingsMigrationAsync())
                {
                    return SharedThumbnailSourceCleanupResult.Rejected(
                        sourceRoot,
                        targetRoot,
                        "thumbnail-writers-did-not-settle"
                    );
                }

                return await Task.Run(() =>
                    SharedThumbnailStoragePolicy.DeleteMigratedSourceFiles(
                        sourceRoot,
                        targetRoot,
                        sharedRoot
                    )
                );
            }
            finally
            {
                EndUserPriorityWork(priorityReason);
            }
        }

        private async Task<bool> WaitForThumbnailQueueIdleForSettingsMigrationAsync()
        {
            DateTime deadlineUtc = DateTime.UtcNow.AddSeconds(65);
            while (DateTime.UtcNow < deadlineUtc)
            {
                if (!TryGetCurrentQueueActiveCount(out int activeCount))
                {
                    return false;
                }

                if (activeCount == 0)
                {
                    // 完了通知と次leaseの境界をまたいで再確認し、writer終了直前の誤判定を避ける。
                    await Task.Delay(200);
                    return TryGetCurrentQueueActiveCount(out int confirmedCount)
                        && confirmedCount == 0;
                }

                await Task.Delay(250);
            }

            return false;
        }

        // 100件たまるごとにサムネイルキューへ流すための共通処理。
        private void FlushPendingQueueItems(List<QueueObj> pendingItems, string folderPath)
        {
            if (pendingItems.Count < 1)
            {
                return;
            }

            bool bypassDebounce = string.Equals(
                folderPath,
                "RescueMissingThumbnails",
                StringComparison.Ordinal
            );
            int flushedCount = 0;
            foreach (QueueObj pending in pendingItems)
            {
                if (TryEnqueueThumbnailJob(pending, bypassDebounce))
                {
                    flushedCount++;
                }
            }
            DebugRuntimeLog.Write(
                "watch-check",
                $"enqueue batch: folder='{folderPath}' requested={pendingItems.Count} flushed={flushedCount}"
            );
            pendingItems.Clear();
        }
    }
}
