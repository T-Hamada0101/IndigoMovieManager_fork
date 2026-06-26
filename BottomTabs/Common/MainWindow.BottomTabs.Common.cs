using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using IndigoMovieManager.ViewModels;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // 上側タブで見えている ERROR だけを少数優先へ寄せ、通常運用の過負荷を避ける。
        private const int ThumbnailVisibleErrorAutoRescueLimit = 16;
        private const int ThumbnailVisibleErrorAutoRescueDelayMs = 250;
        private int _thumbnailVisibleErrorRescueRequestVersion;
        private IReadOnlyList<string> _activeUpperTabVisibleErrorMoviePathKeysSnapshot =
            Array.Empty<string>();
        private int _visibleUpperTabThumbnailErrorRescueRunning;

        private readonly record struct VisibleUpperTabThumbnailErrorRescueRequest(
            ThumbnailErrorRescueRecordSnapshot[] Records,
            string DbFullPath,
            string DbName,
            string ThumbFolder,
            int TabIndex,
            int RequestVersion,
            DateTime PreferredUntilUtc
        );

        /// <summary>
        /// 今開いてるタブの先頭アイテムにカーソルを合わせる！これが俺のスマートなエスコートだ！😎
        /// </summary>
        public void SelectFirstItem()
        {
            SelectUpperTabDefaultView(GetCurrentUpperTabFixedIndex());
        }

        // タブ切替時に不足サムネイルを検出し、必要な再作成キューを積む。
        private async void Tabs_SelectionChangedAsync(object sender, SelectionChangedEventArgs e)
        {
            if (sender as TabControl == null || e.OriginalSource is not TabControl)
            {
                return;
            }

            UiOperationSnapshot snapshot = CaptureUserPriorityOperationSnapshot(
                IsUserPriorityWorkActive(),
                isManualMode: false
            );
            DebugRuntimeLog.Write(
                "ui-priority",
                BuildUiShellInputLogMessage("upper-tab-switch", "selection-changed", snapshot)
            );

            HandleUpperTabSelectionChangedCore();
        }

        // タブ切替で見つかった error 画像動画は、通常キューへ戻さず救済レーンへ静かに逃がす。
        private async Task EnqueueVisibleUpperTabThumbnailErrorsToRescueAsync(
            VisibleUpperTabThumbnailErrorRescueRequest request
        )
        {
            if (request.Records.Length == 0)
            {
                return;
            }

            await Task.Delay(ThumbnailVisibleErrorAutoRescueDelayMs).ConfigureAwait(false);

            bool canRun = await Dispatcher
                .InvokeAsync(() => IsVisibleUpperTabThumbnailErrorRescueRequestCurrent(request))
                .Task.ConfigureAwait(false);
            if (!canRun)
            {
                return;
            }

            if (Interlocked.Exchange(ref _visibleUpperTabThumbnailErrorRescueRunning, 1) == 1)
            {
                DebugRuntimeLog.Write(
                    "thumbnail-rescue",
                    $"tab error rescue enqueue skipped: running tab={request.TabIndex} queued_error={request.Records.Length}"
                );
                return;
            }

            try
            {
                int queuedCount = await Task
                    .Run(() => EnqueueVisibleUpperTabThumbnailErrorsToRescueCore(request))
                    .ConfigureAwait(false);

                await Dispatcher
                    .InvokeAsync(
                        () => ApplyVisibleUpperTabThumbnailErrorRescueResult(request, queuedCount),
                        DispatcherPriority.Background
                    )
                    .Task.ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _visibleUpperTabThumbnailErrorRescueRunning, 0);
            }
        }

        private bool IsVisibleUpperTabThumbnailErrorRescueRequestCurrent(
            VisibleUpperTabThumbnailErrorRescueRequest request
        )
        {
            bool current =
                !Dispatcher.HasShutdownStarted
                && !Dispatcher.HasShutdownFinished
                && GetCurrentUpperTabFixedIndex() == request.TabIndex
                && request.RequestVersion == _thumbnailVisibleErrorRescueRequestVersion
                && AreSameMainDbPath(request.DbFullPath, MainVM?.DbInfo?.DBFullPath ?? "");
            if (!current)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"tab enqueue skip: tab={request.TabIndex} reason=tab_changed queued_error={request.Records.Length}"
                );
            }

            return current;
        }

        private int EnqueueVisibleUpperTabThumbnailErrorsToRescueCore(
            VisibleUpperTabThumbnailErrorRescueRequest request
        )
        {
            if (IsThumbnailErrorVisiblePromotionShutdownStarted())
            {
                return 0;
            }

            ThumbnailFailureDbService failureDbService = CreateThumbnailErrorFailureDbService(
                request.DbFullPath
            );
            int queuedCount = 0;
            foreach (ThumbnailErrorRescueRecordSnapshot record in request.Records)
            {
                if (IsThumbnailErrorVisiblePromotionShutdownStarted())
                {
                    break;
                }

                QueueObj queueObj = new()
                {
                    MovieId = record.MovieId,
                    MovieFullPath = record.MoviePath,
                    Hash = record.Hash,
                    Tabindex = request.TabIndex,
                    Priority = ThumbnailQueuePriority.Preferred,
                };
                if (
                    TryEnqueueThumbnailErrorRescueSnapshotJob(
                        queueObj,
                        request.DbFullPath,
                        failureDbService,
                        request.DbName,
                        request.ThumbFolder,
                        reason: "tab-error-placeholder",
                        requiresIdle: true,
                        priorityUntilUtc: request.PreferredUntilUtc
                    )
                )
                {
                    queuedCount++;
                }
            }

            return queuedCount;
        }

        private void ApplyVisibleUpperTabThumbnailErrorRescueResult(
            VisibleUpperTabThumbnailErrorRescueRequest request,
            int queuedCount
        )
        {
            if (!IsVisibleUpperTabThumbnailErrorRescueRequestCurrent(request))
            {
                return;
            }

            DebugRuntimeLog.Write(
                "thumbnail-rescue",
                $"tab error rescue enqueue end: tab={request.TabIndex} queued_error={queuedCount}"
            );
            if (queuedCount > 0)
            {
                RequestThumbnailErrorSnapshotRefresh();
                RequestThumbnailProgressSnapshotRefresh();
            }
        }

        // visible range の placeholder だけを差分で拾い、今見えている ERROR へ優先を付ける。
        private void QueueVisibleUpperTabThumbnailErrorsToRescue(
            int tabIndex,
            IndigoMovieManager.UpperTabs.Common.UpperTabVisibleRange visibleRange
        )
        {
            if (
                !IsStandardUpperTabFixedIndex(tabIndex)
                || !_activeUpperTabVisibleRange.HasVisibleItems
                || MainVM?.FilteredMovieRecs == null
            )
            {
                _activeUpperTabVisibleErrorMoviePathKeysSnapshot = Array.Empty<string>();
                return;
            }

            MovieRecords[] visibleErrorMovies = ResolveVisibleUpperTabErrorMovies(tabIndex, visibleRange);
            List<string> nextKeys = visibleErrorMovies
                .Select(x => QueueDbPathResolver.CreateMoviePathKey(x?.Movie_Path ?? ""))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (
                AreMoviePathKeyListsEqual(
                    _activeUpperTabVisibleErrorMoviePathKeysSnapshot,
                    nextKeys
                )
            )
            {
                return;
            }

            _activeUpperTabVisibleErrorMoviePathKeysSnapshot = nextKeys;
            if (visibleErrorMovies.Length < 1)
            {
                return;
            }

            int requestVersion = ++_thumbnailVisibleErrorRescueRequestVersion;
            VisibleUpperTabThumbnailErrorRescueRequest request =
                CaptureVisibleUpperTabThumbnailErrorRescueRequest(
                    tabIndex,
                    visibleErrorMovies,
                    requestVersion
                );
            _ = EnqueueVisibleUpperTabThumbnailErrorsToRescueAsync(request);
        }

        private VisibleUpperTabThumbnailErrorRescueRequest CaptureVisibleUpperTabThumbnailErrorRescueRequest(
            int tabIndex,
            MovieRecords[] visibleErrorMovies,
            int requestVersion
        )
        {
            ThumbnailErrorRescueRecordSnapshot[] records = (visibleErrorMovies ?? [])
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Movie_Path))
                .Select(x =>
                    new ThumbnailErrorRescueRecordSnapshot(
                        x.Movie_Id,
                        x.Movie_Path,
                        x.Hash ?? "",
                        [tabIndex]
                    )
                )
                .ToArray();

            return new VisibleUpperTabThumbnailErrorRescueRequest(
                records,
                MainVM?.DbInfo?.DBFullPath ?? "",
                MainVM?.DbInfo?.DBName ?? "",
                MainVM?.DbInfo?.ThumbFolder ?? "",
                tabIndex,
                requestVersion,
                DateTime.UtcNow.Add(ThumbnailVisibleErrorPreferredDuration)
            );
        }

        private MovieRecords[] ResolveVisibleUpperTabErrorMovies(
            int tabIndex,
            IndigoMovieManager.UpperTabs.Common.UpperTabVisibleRange visibleRange
        )
        {
            if (!visibleRange.HasVisibleItems || MainVM?.FilteredMovieRecs == null)
            {
                return [];
            }

            int resolvedTabIndex = ResolvePlayerTabGridProxyTabIndex(tabIndex);
            string[] thumbProps =
            [
                nameof(MovieRecords.ThumbPathSmall),
                nameof(MovieRecords.ThumbPathBig),
                nameof(MovieRecords.ThumbPathGrid),
                nameof(MovieRecords.ThumbPathList),
                nameof(MovieRecords.ThumbPathBig10),
            ];
            if (resolvedTabIndex < 0 || resolvedTabIndex >= thumbProps.Length)
            {
                return [];
            }

            var thumbProp = typeof(MovieRecords).GetProperty(thumbProps[resolvedTabIndex]);
            if (thumbProp == null)
            {
                return [];
            }

            List<MovieRecords> result = [];
            int totalCount = MainVM.FilteredMovieRecs.Count;
            int lastVisibleIndex = Math.Min(visibleRange.LastVisibleIndex, totalCount - 1);
            for (int index = Math.Max(0, visibleRange.FirstVisibleIndex); index <= lastVisibleIndex; index++)
            {
                MovieRecords movie = MainVM.FilteredMovieRecs[index];
                if (!IsThumbnailErrorPlaceholderPath(thumbProp.GetValue(movie)?.ToString()))
                {
                    continue;
                }

                result.Add(movie);
                if (result.Count >= ThumbnailVisibleErrorAutoRescueLimit)
                {
                    break;
                }
            }

            if (result.Count >= ThumbnailVisibleErrorAutoRescueLimit)
            {
                DebugRuntimeLog.Write(
                    "thumbnail-rescue",
                    $"visible error rescue capped: tab={tabIndex} queued={result.Count}"
                );
            }

            return result.ToArray();
        }

        // 現在タブから選択中の1件を取得する。
        public MovieRecords GetSelectedItemByTabIndex()
        {
            return ResolveSelectedUpperTabMovieRecord(GetCurrentUpperTabFixedIndex());
        }

        // 現在タブから複数選択中のレコード一覧を取得する。
        private List<MovieRecords> GetSelectedItemsByTabIndex()
        {
            return ResolveSelectedUpperTabMovieRecords(GetCurrentUpperTabFixedIndex());
        }

        // ラベルクリック時は、現在前面にいる通常タブへだけ選択を返す。
        private void SelectCurrentUpperTabMovieRecord(MovieRecords record)
        {
            SelectUpperTabMovieRecord(GetCurrentUpperTabFixedIndex(), record);
        }
    }
}
