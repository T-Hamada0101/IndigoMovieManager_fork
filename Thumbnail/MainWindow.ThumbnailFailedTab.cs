using IndigoMovieManager.ModelViews;
using IndigoMovieManager.Thumbnail.QueueDb;
using System.Threading;
using System.Windows.Threading;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int ThumbnailFailedRefreshDebounceMs = 300;
        private int _thumbnailFailedTabSelected;
        private int _thumbnailFailedListDirty = 1;
        private int _thumbnailFailedRefreshRequested;
        private int _thumbnailFailedRefreshQueued;
        private int _thumbnailFailedRefreshRevision;
        private int _thumbnailFailedAppliedRevision = -1;
        private long _thumbnailFailedLastRefreshTickMs = -1;

        // 失敗一覧の更新を必要状態にする。必要時だけ再読込を予約する。
        private void MarkThumbnailFailedListDirty(bool incrementRevision = false, string reason = "")
        {
            if (incrementRevision)
            {
                _ = Interlocked.Increment(ref _thumbnailFailedRefreshRevision);
            }

            _ = Interlocked.Exchange(ref _thumbnailFailedListDirty, 1);
            if (!IsThumbnailFailedTabSelected())
            {
                return;
            }

            DebugRuntimeLog.Write(
                "thumbnail-failed",
                $"failed list dirty marked: reason={reason} revision={Volatile.Read(ref _thumbnailFailedRefreshRevision)}"
            );
            RequestThumbnailFailedListRefresh();
        }

        // タブ選択状態を更新し、表示中タブならdirtyを即時回収する。
        private void UpdateThumbnailFailedTabSelectionState(bool isSelected)
        {
            _ = Interlocked.Exchange(ref _thumbnailFailedTabSelected, isSelected ? 1 : 0);
            if (!isSelected)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _thumbnailFailedListDirty, 0, 0) == 1)
            {
                RequestThumbnailFailedListRefresh();
            }
        }

        private bool IsThumbnailFailedTabSelected()
        {
            return Interlocked.CompareExchange(ref _thumbnailFailedTabSelected, 0, 0) == 1;
        }

        // 連続イベントを1本化して、失敗一覧の再読込要求を過剰発火させない。
        private void RequestThumbnailFailedListRefresh()
        {
            if (!IsThumbnailFailedTabSelected())
            {
                return;
            }
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            _ = Interlocked.Exchange(ref _thumbnailFailedRefreshRequested, 1);
            if (Interlocked.Exchange(ref _thumbnailFailedRefreshQueued, 1) == 1)
            {
                return;
            }

            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(ProcessThumbnailFailedRefreshQueue)
            );
        }

        private async void ProcessThumbnailFailedRefreshQueue()
        {
            try
            {
                if (Interlocked.Exchange(ref _thumbnailFailedRefreshRequested, 0) == 1)
                {
                    await WaitThumbnailFailedRefreshDebounceAsync();
                    await RefreshThumbnailFailedListCoreAsync();
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "thumbnail-failed",
                    $"failed list refresh queue failed: {ex.Message}"
                );
            }
            finally
            {
                _ = Interlocked.Exchange(ref _thumbnailFailedRefreshQueued, 0);
                if (
                    IsThumbnailFailedTabSelected()
                    && Interlocked.CompareExchange(ref _thumbnailFailedRefreshRequested, 0, 0) == 1
                )
                {
                    RequestThumbnailFailedListRefresh();
                }
            }
        }

        // 直近反映から一定時間は待機し、完了イベント連打時の読み直し頻度を制限する。
        private async Task WaitThumbnailFailedRefreshDebounceAsync()
        {
            long lastTickMs = Interlocked.Read(ref _thumbnailFailedLastRefreshTickMs);
            if (lastTickMs < 0)
            {
                return;
            }

            long nowTickMs = Environment.TickCount64;
            long elapsedMs = nowTickMs - lastTickMs;
            if (elapsedMs < ThumbnailFailedRefreshDebounceMs)
            {
                int delayMs = (int)(ThumbnailFailedRefreshDebounceMs - elapsedMs);
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs);
                }
            }
        }

        // QueueDBから失敗一覧を取得し、最新要求と一致する場合だけUIへ反映する。
        private async Task RefreshThumbnailFailedListCoreAsync()
        {
            if (MainVM?.ThumbnailFailedRecs == null)
            {
                return;
            }

            string requestedMainDbPath = MainVM?.DbInfo?.DBFullPath ?? "";
            int requestedRevision = Volatile.Read(ref _thumbnailFailedRefreshRevision);

            List<QueueDbFailedItem> failedItems = [];
            QueueDbService queueDbService = ResolveCurrentQueueDbService();
            if (!string.IsNullOrWhiteSpace(requestedMainDbPath) && queueDbService != null)
            {
                try
                {
                    failedItems = await Task.Run(() => queueDbService.GetFailedItems());
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write(
                        "thumbnail-failed",
                        $"failed list load failed: {ex.Message}"
                    );
                }
            }

            if (!IsThumbnailFailedTabSelected())
            {
                return;
            }

            string currentMainDbPath = MainVM?.DbInfo?.DBFullPath ?? "";
            int currentRevision = Volatile.Read(ref _thumbnailFailedRefreshRevision);
            if (
                !string.Equals(
                    requestedMainDbPath,
                    currentMainDbPath,
                    StringComparison.OrdinalIgnoreCase
                )
                || currentRevision != requestedRevision
            )
            {
                return;
            }

            MainVM.ThumbnailFailedRecs.Clear();
            foreach (QueueDbFailedItem failedItem in failedItems)
            {
                MainVM.ThumbnailFailedRecs.Add(ToThumbnailFailedRecordViewModel(failedItem));
            }

            _ = Interlocked.Exchange(ref _thumbnailFailedListDirty, 0);
            _ = Interlocked.Exchange(ref _thumbnailFailedAppliedRevision, requestedRevision);
            Interlocked.Exchange(ref _thumbnailFailedLastRefreshTickMs, Environment.TickCount64);

            DebugRuntimeLog.Write(
                "thumbnail-failed",
                $"failed list applied: count={failedItems.Count} revision={requestedRevision} applied={Interlocked.CompareExchange(ref _thumbnailFailedAppliedRevision, 0, 0)}"
            );
        }

        // QueueDBの生情報を、表示専用ViewModelへ詰め替える。
        private static ThumbnailFailedRecordViewModel ToThumbnailFailedRecordViewModel(
            QueueDbFailedItem item
        )
        {
            if (item == null)
            {
                return new ThumbnailFailedRecordViewModel();
            }

            return new ThumbnailFailedRecordViewModel
            {
                QueueId = item.QueueId,
                MainDbPathHash = item.MainDbPathHash ?? "",
                MoviePath = item.MoviePath ?? "",
                MoviePathKey = item.MoviePathKey ?? "",
                TabIndex = item.TabIndex,
                MovieSizeBytes = item.MovieSizeBytes,
                ThumbPanelPos = item.ThumbPanelPos,
                ThumbTimePos = item.ThumbTimePos,
                Status = item.Status.ToString(),
                AttemptCount = item.AttemptCount,
                LastError = item.LastError ?? "",
                OwnerInstanceId = item.OwnerInstanceId ?? "",
                LeaseUntilUtc = item.LeaseUntilUtc ?? "",
                CreatedAtUtc = item.CreatedAtUtc ?? "",
                UpdatedAtUtc = item.UpdatedAtUtc ?? "",
            };
        }
    }
}
