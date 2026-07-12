using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using IndigoMovieManager.Data;
using IndigoMovieManager.Startup;
using IndigoMovieManager.ViewModels;
using IndigoMovieManager.Watcher;
namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int StartupFirstPageSize = 200;
        private const int StartupAppendPageSize = 300;
        private const int StartupLoadMoreNearEndThreshold = 72;
        private const int StartupHeavyServicesDelayMs = 1500;
        private const string StartupWarmPathTrigger = "StartupDbOpen";
        private const string StartupSourceDbFallback = "DbFallback";
        private const string StartupSourceLegacyFallback = "LegacyFallback";
        private static readonly int[] StartupThumbnailPrewarmTabIndexes = [0, 1, 2, 3, 4, 99];

        private readonly Stopwatch _startupUiStopwatch = Stopwatch.StartNew();
        private readonly StartupLoadCoordinator _startupLoadCoordinator = new();
        private bool _startupWindowShownLogged;
        private bool _startupInputReadyLogged;
        private bool _startupFeedIsPartialActive;
        private bool _startupFeedLoadedAllPages;
        private bool _startupLightServicesStarted;
        private bool _startupHeavyServicesStarted;
        private StartupFeedRequest? _startupContinuationRequest;
        private MovieRecordBulkBuildContext? _startupContinuationBulkContext;
        private MovieRecordBulkBuildCache _startupContinuationBulkCache = null!;
        private CancellationToken _startupContinuationCancellationToken;
        private int _startupSessionRevision;
        private string _startupSessionTrigger = StartupWarmPathTrigger;
        private int _startupContinuationRevision;
        private int _startupNextPageIndex = 1;
        private bool _startupHasMorePages;
        private bool _startupAppendInFlight;

        private bool IsStartupFeedPartialActive =>
            Volatile.Read(ref _startupFeedIsPartialActive)
            && !Volatile.Read(ref _startupFeedLoadedAllPages);

        private void LogStartupWindowShownOnce()
        {
            if (_startupWindowShownLogged)
            {
                return;
            }

            _startupWindowShownLogged = true;
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"startup window shown: elapsed_ms={_startupUiStopwatch.ElapsedMilliseconds}"
            );
        }

        private void LogStartupWarmPathMilestone(
            string milestone,
            int revision,
            string trigger,
            string sourceKind,
            string feedState,
            string details = ""
        )
        {
            string normalizedDetails = string.IsNullOrWhiteSpace(details)
                ? ""
                : $" {details.Trim()}";
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"startup warm path {milestone}: revision={revision} trigger={trigger} source={sourceKind} feed_state={feedState}{normalizedDetails} elapsed_ms={_startupUiStopwatch.ElapsedMilliseconds}"
            );
        }

        private string ResolveStartupWarmPathTrigger()
        {
            return string.IsNullOrWhiteSpace(_startupSessionTrigger)
                ? StartupWarmPathTrigger
                : _startupSessionTrigger;
        }

        private int ResolveStartupWarmPathRevision(int revision)
        {
            return revision > 0 ? revision : _startupSessionRevision;
        }

        private static string ResolveStartupFeedState(bool hasMore)
        {
            return hasMore ? "partial-feed" : "complete";
        }

        private static string ResolveStartupHeavyServicesFeedState(string startReason)
        {
            if (string.Equals(startReason, "StartupFallback", StringComparison.Ordinal))
            {
                return "fallback";
            }

            if (string.Equals(startReason, "StartupFeedComplete", StringComparison.Ordinal))
            {
                return "complete";
            }

            if (startReason.StartsWith("StartupCanceled:", StringComparison.Ordinal))
            {
                return "canceled";
            }

            return "partial-feed";
        }

        private void BeginStartupDbOpen()
        {
            string dbPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                return;
            }

            _startupUiStopwatch.Restart();
            _startupSessionTrigger = StartupWarmPathTrigger;
            string sortId = string.IsNullOrWhiteSpace(MainVM?.DbInfo?.Sort)
                ? "1"
                : MainVM.DbInfo.Sort;
            string searchKeyword = MainVM?.DbInfo?.SearchKeyword ?? "";
            StartupFeedRequest request = new(
                UiHangActivityKind.Startup,
                dbPath,
                sortId,
                searchKeyword,
                StartupFirstPageSize,
                StartupAppendPageSize
            );
            _ = RunStartupDbOpenAsync(request);
        }

        private async Task RunStartupDbOpenAsync(StartupFeedRequest request)
        {
            using IDisposable uiHangScope = TrackUiHangActivity(request.ActivityKind);
            StartupLoadSession session = _startupLoadCoordinator.StartNewSession();
            _startupSessionRevision = session.Revision;
            _startupSessionTrigger = StartupWarmPathTrigger;
            _startupFeedIsPartialActive = true;
            _startupFeedLoadedAllPages = false;
            _startupInputReadyLogged = false;
            _startupLightServicesStarted = false;
            _startupHeavyServicesStarted = false;
            SetThumbnailQueueInputEnabled(false);

            DebugRuntimeLog.Write(
                "ui-tempo",
                $"startup open begin: revision={session.Revision} trigger={ResolveStartupWarmPathTrigger()} db='{request.DbPath}' sort={request.SortId} search='{request.SearchKeyword}' first_page={request.FirstPageSize}"
            );

            try
            {
                if (!string.IsNullOrWhiteSpace(request.SearchKeyword))
                {
                    throw new NotSupportedException(
                        "起動時検索キーワード付きの first-page ロードは未対応です。"
                    );
                }

                MovieRecordBulkBuildContext bulkContext = CaptureMovieRecordBulkBuildContext();
                // first page前は全サムネイルフォルダを列挙せず、各ページの候補だけを増分確認する。
                MovieRecordBulkBuildCache bulkCache = CreateEmptyMovieRecordBulkBuildCache(
                    bulkContext
                );
                StartupFeedPage firstPage = await LoadStartupFeedPageAsync(
                    request,
                    pageIndex: 0,
                    bulkContext,
                    bulkCache,
                    session.CancellationToken
                );

                if (!_startupLoadCoordinator.IsCurrent(session.Revision))
                {
                    return;
                }

                await Dispatcher.InvokeAsync(
                    () => ApplyStartupFirstPage(request, firstPage, session.Revision),
                    DispatcherPriority.Background
                );

                if (!_startupLoadCoordinator.IsCurrent(session.Revision))
                {
                    return;
                }

                _ = RunStartupDeferredServicesAsync(session.Revision);

                if (firstPage.HasMore)
                {
                    await Dispatcher.InvokeAsync(
                        () =>
                            RememberStartupContinuationState(
                                request,
                                bulkContext,
                                bulkCache,
                                session.Revision,
                                session.CancellationToken,
                                nextPageIndex: 1,
                                hasMorePages: true
                            ),
                        DispatcherPriority.Background
                    );
                    _ = RunStartupHeavyServicesAfterDelayAsync(
                        session.Revision,
                        session.CancellationToken,
                        "StartupFirstPageDelay"
                    );
                }
                else
                {
                    await Dispatcher.InvokeAsync(
                        () => FinishStartupFeedIfCurrent(session.Revision),
                        DispatcherPriority.Background
                    );
                }
            }
            catch (OperationCanceledException)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"startup open canceled: revision={session.Revision} elapsed_ms={_startupUiStopwatch.ElapsedMilliseconds}"
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"startup open failed: revision={session.Revision} err='{ex.GetType().Name}: {ex.Message}' elapsed_ms={_startupUiStopwatch.ElapsedMilliseconds}"
                );
                await Dispatcher.InvokeAsync(
                    () => FallbackToLegacyStartupLoad(request.SortId, session.Revision),
                    DispatcherPriority.Background
                );
            }
        }

        private async Task RunStartupDeferredServicesAsync(int revision)
        {
            try
            {
                await Task.Yield();
                if (!_startupLoadCoordinator.IsCurrent(revision))
                {
                    return;
                }

                await Dispatcher.InvokeAsync(
                    () =>
                    {
                        if (!_startupLoadCoordinator.IsCurrent(revision))
                        {
                            return;
                        }

                        if (_startupLightServicesStarted)
                        {
                            return;
                        }

                        _startupLightServicesStarted = true;
                        ReloadBookmarkTabData();
                        QueueThumbnailSuccessIndexPrewarm();
                        QueueStartupThumbnailProgressSnapshotRefresh();
                        QueueEverythingLiteWatchRootPrewarm();
                        QueuePlayerWebViewEnvironmentWarm();
                        DebugRuntimeLog.Write(
                            "ui-tempo",
                            $"startup light services started: revision={revision} trigger={ResolveStartupWarmPathTrigger()} elapsed_ms={_startupUiStopwatch.ElapsedMilliseconds}"
                        );
                    },
                    DispatcherPriority.Background
                );
            }
            catch (OperationCanceledException)
            {
                // 新しい起動要求へ切り替わっただけなので黙って終える。
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"startup deferred services failed: revision={revision} err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

        private void QueueStartupThumbnailProgressSnapshotRefresh()
        {
            // first-page 表示後の軽サービスで、進捗 snapshot を既存のcoalesce経路へ合流させる。
            RequestThumbnailProgressSnapshotRefresh();
        }

        // 起動直後の first-page 表示を優先し、watcher 作成は UI が一息ついてから始める。
        private void QueueStartupWatcherCreation(int revision)
        {
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() =>
                {
                    if (revision > 0 && !_startupLoadCoordinator.IsCurrent(revision))
                    {
                        return;
                    }

                    if (!_startupLightServicesStarted && !_startupHeavyServicesStarted)
                    {
                        return;
                    }

                    CreateWatcher();
                    DebugRuntimeLog.Write(
                        "ui-tempo",
                        $"startup watcher creation queued: revision={revision} elapsed_ms={_startupUiStopwatch.ElapsedMilliseconds}"
                    );
                })
            );
        }

        // first-page 表示後に各タブの成功jpgインデックスを裏で温め、最初の同期走査を起きにくくする。
        private void QueueThumbnailSuccessIndexPrewarm()
        {
            string dbName = MainVM?.DbInfo?.DBName ?? "";
            string thumbFolder = MainVM?.DbInfo?.ThumbFolder ?? "";
            string dbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(dbName) && string.IsNullOrWhiteSpace(thumbFolder))
            {
                return;
            }

            _ = Task.Run(() => PrewarmThumbnailSuccessIndexCore(dbName, thumbFolder, dbFullPath));
        }

        // 起動直後のUI tickを空けるため、成功jpg索引の構築は背景でまとめて進める。
        private void PrewarmThumbnailSuccessIndexCore(
            string dbName,
            string thumbFolder,
            string dbFullPath
        )
        {
            HashSet<string> queuedOutPaths = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < StartupThumbnailPrewarmTabIndexes.Length; i++)
            {
                string outPath = ResolveThumbnailOutPath(
                    StartupThumbnailPrewarmTabIndexes[i],
                    dbName,
                    thumbFolder
                );
                if (string.IsNullOrWhiteSpace(outPath) || !queuedOutPaths.Add(outPath))
                {
                    continue;
                }

                Thumbnail.ThumbnailPathResolver.PrewarmSuccessThumbnailPathIndex(outPath);
            }

            DebugRuntimeLog.Write(
                "ui-tempo",
                $"thumbnail index prewarm queued: count={queuedOutPaths.Count} db='{dbFullPath}'"
            );
        }

        // EverythingLite 選択時だけ watch root を背景 rebuild し、初回の provider 同期構築を減らす。
        private void QueueEverythingLiteWatchRootPrewarm()
        {
            string dbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return;
            }

            IntegrationMode integrationMode = GetEverythingIntegrationMode();
            string providerKey = FileIndexProviderFactory.NormalizeProviderKey(
                Properties.Settings.Default.FileIndexProvider
            );
            int revision = _startupSessionRevision;
            _ = RunEverythingLiteWatchRootPrewarmAsync(
                dbFullPath,
                integrationMode,
                providerKey,
                revision
            );
        }

        private async Task RunEverythingLiteWatchRootPrewarmAsync(
            string dbFullPath,
            IntegrationMode integrationMode,
            string providerKey,
            int revision
        )
        {
            EverythingLiteWatchRootPrewarmPlan plan;
            try
            {
                plan = await Task.Run(
                        () => PrewarmEverythingLiteWatchRoots(
                            dbFullPath,
                            integrationMode,
                            providerKey
                        )
                    )
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"everythinglite root prewarm failed: db='{dbFullPath}' err='{ex.GetType().Name}: {ex.Message}'"
                );
                return;
            }

            if (plan.WatchRoots.Count < 1)
            {
                return;
            }

            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            bool isCurrent;
            try
            {
                isCurrent = await Dispatcher
                    .InvokeAsync(
                        () =>
                            IsEverythingLiteWatchRootPrewarmCurrent(
                                plan.DbFullPath,
                                revision,
                                plan.WatchRoots
                            ),
                        DispatcherPriority.Background
                    )
                    .Task.ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }
            catch (InvalidOperationException)
            {
                return;
            }

            if (!isCurrent)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"everythinglite root prewarm skipped by stale state: revision={revision} db='{plan.DbFullPath}'"
                );
                return;
            }

            foreach (string watchRoot in plan.WatchRoots)
            {
                EverythingLiteProvider.PrewarmRootIndex(watchRoot);
            }

            DebugRuntimeLog.Write(
                "ui-tempo",
                $"everythinglite root prewarm queued: count={plan.WatchRoots.Count} db='{plan.DbFullPath}'"
            );
        }

        private EverythingLiteWatchRootPrewarmPlan PrewarmEverythingLiteWatchRoots(
            string dbFullPath,
            IntegrationMode integrationMode,
            string providerKey
        )
        {
            if (!_indexProviderFacade.IsIntegrationConfigured(integrationMode))
            {
                return EverythingLiteWatchRootPrewarmPlan.Empty(dbFullPath);
            }

            AvailabilityResult availability = _indexProviderFacade.CheckAvailability(integrationMode);
            if (!availability.CanUse)
            {
                return EverythingLiteWatchRootPrewarmPlan.Empty(dbFullPath);
            }

            if (
                !string.Equals(
                    providerKey,
                    FileIndexProviderFactory.ProviderEverythingLite,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return EverythingLiteWatchRootPrewarmPlan.Empty(dbFullPath);
            }

            DataTable watchTable = GetWatchTableSnapshot(
                dbFullPath,
                "SELECT * FROM watch where watch = 1"
            );
            if (watchTable == null || watchTable.Rows.Count < 1)
            {
                return EverythingLiteWatchRootPrewarmPlan.Empty(dbFullPath);
            }

            HashSet<string> queuedRoots = new(StringComparer.OrdinalIgnoreCase);
            foreach (DataRow row in watchTable.Rows)
            {
                string watchRoot = row["dir"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(watchRoot) || !Path.Exists(watchRoot))
                {
                    continue;
                }

                if (!queuedRoots.Add(watchRoot))
                {
                    continue;
                }
            }

            return new EverythingLiteWatchRootPrewarmPlan(dbFullPath, queuedRoots.ToList());
        }

        private bool IsEverythingLiteWatchRootPrewarmCurrent(
            string dbFullPath,
            int revision,
            IReadOnlyCollection<string> watchRoots
        )
        {
            if (watchRoots.Count < 1)
            {
                return false;
            }

            if (revision > 0 && !_startupLoadCoordinator.IsCurrent(revision))
            {
                return false;
            }

            if (!AreSameMainDbPath(dbFullPath, MainVM?.DbInfo?.DBFullPath ?? ""))
            {
                return false;
            }

            // 背景で確定した root だけを後段へ流し、空文字や壊れた値は UI 戻り時に捨てる。
            return watchRoots.All(root => !string.IsNullOrWhiteSpace(root));
        }

        private sealed record EverythingLiteWatchRootPrewarmPlan(
            string DbFullPath,
            IReadOnlyList<string> WatchRoots
        )
        {
            internal static EverythingLiteWatchRootPrewarmPlan Empty(string dbFullPath)
            {
                return new EverythingLiteWatchRootPrewarmPlan(dbFullPath, []);
            }
        }

        private async Task RunStartupHeavyServicesAfterDelayAsync(
            int revision,
            CancellationToken cancellationToken,
            string trigger
        )
        {
            try
            {
                await Task.Delay(StartupHeavyServicesDelayMs, cancellationToken);
                if (!_startupLoadCoordinator.IsCurrent(revision))
                {
                    return;
                }

                await Dispatcher.InvokeAsync(
                    () =>
                    {
                        if (!_startupLoadCoordinator.IsCurrent(revision))
                        {
                            return;
                        }

                        StartStartupHeavyServicesIfNeeded(trigger);
                    },
                    DispatcherPriority.Background
                );
            }
            catch (OperationCanceledException)
            {
                // 新しい要求へ切り替わっただけなので黙って終える。
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"startup heavy services delay failed: revision={revision} err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

        private async Task<StartupFeedPage> LoadStartupFeedPageAsync(
            StartupFeedRequest request,
            int pageIndex,
            MovieRecordBulkBuildContext bulkContext,
            MovieRecordBulkBuildCache bulkCache,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            int pageSize = pageIndex == 0 ? request.FirstPageSize : request.AppendPageSize;
            Stopwatch pageStopwatch = Stopwatch.StartNew();

            DebugRuntimeLog.Write(
                "db",
                $"startup page load: page={pageIndex} take={pageSize} sort={request.SortId}"
            );

            // 起動時の page 読みは facade へ寄せ、UI 側から SQL と reader 実装を剥がす。
            MainDbMovieReadRequest dataRequest = new(
                request.DbPath,
                request.SortId,
                request.FirstPageSize,
                request.AppendPageSize
            );
            Stopwatch dbReadStopwatch = Stopwatch.StartNew();
            MainDbMovieReadPageResult sourcePage = await Task.Run(
                    () => _mainDbMovieReadFacade.ReadStartupPage(dataRequest, pageIndex),
                    cancellationToken
                )
                .ConfigureAwait(false);
            dbReadStopwatch.Stop();
            cancellationToken.ThrowIfCancellationRequested();
            StartupBulkCacheWarmResult cacheWarmResult = await Task.Run(
                    () =>
                        WarmMovieRecordBulkBuildCacheForStartupPage(
                            bulkCache,
                            bulkContext,
                            sourcePage.Items
                        ),
                    cancellationToken
                )
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Stopwatch convertStopwatch = Stopwatch.StartNew();
            MovieRecords[] items = await Task.Run(
                    () => BuildStartupMovieRecords(sourcePage.Items, bulkContext, bulkCache),
                    cancellationToken
                )
                .ConfigureAwait(false);
            convertStopwatch.Stop();
            pageStopwatch.Stop();

            DebugRuntimeLog.Write(
                "db",
                $"startup page load end: page={pageIndex} take={pageSize} rows={sourcePage.Items.Length} db_read_ms={dbReadStopwatch.ElapsedMilliseconds} bulk_cache_ms={cacheWarmResult.ElapsedMs} bulk_cache_candidates={cacheWarmResult.CandidateCount} bulk_cache_hits={cacheWarmResult.HitCount} bulk_cache_small_ms={cacheWarmResult.ComponentElapsedMs[0]} bulk_cache_big_ms={cacheWarmResult.ComponentElapsedMs[1]} bulk_cache_grid_ms={cacheWarmResult.ComponentElapsedMs[2]} bulk_cache_list_ms={cacheWarmResult.ComponentElapsedMs[3]} bulk_cache_big10_ms={cacheWarmResult.ComponentElapsedMs[4]} bulk_cache_detail_ms={cacheWarmResult.ComponentElapsedMs[5]} row_convert_ms={convertStopwatch.ElapsedMilliseconds} total_ms={pageStopwatch.ElapsedMilliseconds}"
            );

            return new StartupFeedPage(
                items,
                sourcePage.ApproximateTotalCount,
                sourcePage.HasMore,
                SourceKind: StartupSourceDbFallback,
                PageIndex: pageIndex
            );
        }

        private MovieRecords[] BuildStartupMovieRecords(
            MainDbMovieReadItemResult[] items,
            MovieRecordBulkBuildContext bulkContext,
            MovieRecordBulkBuildCache bulkCache
        )
        {
            if (items == null || items.Length < 1)
            {
                return [];
            }

            MovieRecords[] shells = new MovieRecords[items.Length];
            for (int index = 0; index < items.Length; index++)
            {
                shells[index] = CreateStartupMovieRecordFromSource(items[index], bulkContext, bulkCache);
            }

            return shells.Where(item => item != null).ToArray();
        }

        private void ApplyStartupFirstPage(
            StartupFeedRequest request,
            StartupFeedPage page,
            int revision
        )
        {
            if (!_startupLoadCoordinator.IsCurrent(revision))
            {
                return;
            }

            MainVM.ReplaceMovieRecs(page.Items);
            FilteredMovieRecsUpdateResult applyResult = MainVM.ReplaceFilteredMovieRecs(
                page.Items,
                FilteredMovieRecsUpdateMode.Reset
            );
            filterList = page.Items;
            MainVM.DbInfo.SearchCount = page.Items.Length;
            UpdateExtensionDetailVisibilityBySearchCount();

            if (page.Items.Length > 0)
            {
                SelectFirstItem();
            }

            if (applyResult.HasChanges)
            {
                NotifyUpperTabViewportSourceChanged();
                Refresh();
                RequestUpperTabVisibleRangeRefresh(immediate: true, reason: "startup-first-page");
            }

            string feedState = ResolveStartupFeedState(page.HasMore);
            LogStartupWarmPathMilestone(
                "first-page shown",
                revision,
                ResolveStartupWarmPathTrigger(),
                page.SourceKind,
                feedState,
                $"count={page.Items.Length} has_more={page.HasMore}"
            );

            if (!_startupInputReadyLogged)
            {
                _startupInputReadyLogged = true;
                LogStartupWarmPathMilestone(
                    "input ready",
                    revision,
                    ResolveStartupWarmPathTrigger(),
                    page.SourceKind,
                    feedState,
                    $"count={page.Items.Length} has_more={page.HasMore}"
                );
            }

            QueueStartupAsciiSearchPrewarm(revision, request.DbPath);
        }

        private void ApplyStartupAppendPage(StartupFeedPage page, int revision)
        {
            if (!_startupLoadCoordinator.IsCurrent(revision) || page.Items.Length < 1)
            {
                return;
            }

            foreach (MovieRecords item in page.Items)
            {
                MainVM.MovieRecs.Add(item);
                MainVM.FilteredMovieRecs.Add(item);
            }

            filterList = MainVM.FilteredMovieRecs;
            MainVM.DbInfo.SearchCount = MainVM.FilteredMovieRecs.Count;
            UpdateExtensionDetailVisibilityBySearchCount();
            NotifyUpperTabViewportSourceChanged();

            LogStartupWarmPathMilestone(
                "page append",
                revision,
                ResolveStartupWarmPathTrigger(),
                page.SourceKind,
                ResolveStartupFeedState(page.HasMore),
                $"page={page.PageIndex} count={page.Items.Length} visible_total={MainVM.FilteredMovieRecs.Count} has_more={page.HasMore}"
            );

            _startupAppendInFlight = false;
            _startupHasMorePages = page.HasMore;
            _startupNextPageIndex = page.PageIndex + 1;

            if (!page.HasMore)
            {
                FinishStartupFeedIfCurrent(revision);
                return;
            }

            // append 直後は timer 経由へ寄せて、Apply -> Append -> Apply の詰まりを和らげる。
            RequestUpperTabVisibleRangeRefresh(reason: "startup-append");
        }

        private void FinishStartupFeedIfCurrent(int revision)
        {
            if (!_startupLoadCoordinator.IsCurrent(revision))
            {
                return;
            }

            _startupFeedIsPartialActive = false;
            _startupFeedLoadedAllPages = true;
            ClearStartupContinuationState();
            MainVM.DbInfo.SearchCount = MainVM.FilteredMovieRecs.Count;
            StartStartupHeavyServicesIfNeeded(
                "StartupFeedComplete",
                revision,
                StartupSourceDbFallback,
                "complete"
            );

            LogStartupWarmPathMilestone(
                "feed complete",
                revision,
                ResolveStartupWarmPathTrigger(),
                StartupSourceDbFallback,
                "complete",
                $"total_count={MainVM.FilteredMovieRecs.Count}"
            );
        }

        private void FallbackToLegacyStartupLoad(string sortId, int revision)
        {
            if (!_startupLoadCoordinator.IsCurrent(revision))
            {
                return;
            }

            string warmPathTrigger = ResolveStartupWarmPathTrigger();
            LogStartupWarmPathMilestone(
                "fallback begin",
                revision,
                warmPathTrigger,
                StartupSourceLegacyFallback,
                "fallback",
                $"sort={sortId}"
            );
            CancelStartupFeed("startup-fallback");
            StartStartupHeavyServicesIfNeeded(
                "StartupFallback",
                revision,
                StartupSourceLegacyFallback,
                "fallback"
            );
            ReloadBookmarkTabData();
            // fallback 起動でも通常起動と同じ遅延予約へ合流し、初期表示の置き去りを防ぐ。
            QueueStartupThumbnailProgressSnapshotRefresh();
            RefreshStartupFallbackMovieView(sortId, revision, warmPathTrigger);
            CreateWatcher();
            if (!_startupInputReadyLogged)
            {
                _startupInputReadyLogged = true;
                LogStartupWarmPathMilestone(
                    "input ready",
                    revision,
                    warmPathTrigger,
                    StartupSourceLegacyFallback,
                    "fallback",
                    $"count={MainVM.FilteredMovieRecs.Count}"
                );
            }

            LogStartupWarmPathMilestone(
                "fallback complete",
                revision,
                warmPathTrigger,
                StartupSourceLegacyFallback,
                "fallback",
                $"count={MainVM.FilteredMovieRecs.Count}"
            );
        }

        private void RefreshStartupFallbackMovieView(
            string sortId,
            int revision,
            string warmPathTrigger
        )
        {
            int sourceCount = MainVM?.MovieRecs?.Count ?? -1;
            if (ShouldUseStartupFallbackMemoryRefresh(_startupFeedLoadedAllPages, sourceCount))
            {
                LogStartupWarmPathMilestone(
                    "fallback memory-refresh",
                    revision,
                    warmPathTrigger,
                    StartupSourceLegacyFallback,
                    "fallback",
                    $"sort={sortId} source_count={sourceCount}"
                );
                _ = RefreshStartupFallbackMovieViewFromCurrentSourceAsync(sortId, revision);
                return;
            }

            LogStartupWarmPathMilestone(
                "fallback full-reload",
                revision,
                warmPathTrigger,
                StartupSourceLegacyFallback,
                "fallback",
                $"sort={sortId} source_count={sourceCount} source_complete={_startupFeedLoadedAllPages}"
            );
            FilterAndSort(sortId, true);
        }

        internal static bool ShouldUseStartupFallbackMemoryRefresh(
            bool startupFeedLoadedAllPages,
            int sourceCount
        )
        {
            return startupFeedLoadedAllPages && sourceCount >= 0;
        }

        private async Task RefreshStartupFallbackMovieViewFromCurrentSourceAsync(
            string sortId,
            int revision
        )
        {
            try
            {
                await RefreshMovieViewFromCurrentSourceAsync(
                    sortId,
                    "startup-fallback",
                    UiHangActivityKind.Startup
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"startup fallback memory refresh failed: revision={revision} err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

        private void EnsureStartupBackgroundTasksRunning(string trigger)
        {
            if (_thumbCheckTask == null || _thumbCheckTask.IsCompleted)
            {
                DebugRuntimeLog.TaskStart(nameof(CheckThumbAsync), $"trigger={trigger}");
                _thumbCheckTask = CheckThumbAsync(_thumbCheckCts.Token);
            }

            if (
                _thumbnailQueuePersisterTask == null
                || _thumbnailQueuePersisterTask.IsCompleted
            )
            {
                DebugRuntimeLog.TaskStart(
                    nameof(RunThumbnailQueuePersisterSupervisorAsync),
                    $"trigger={trigger}"
                );
                _thumbnailQueuePersisterTask = RunThumbnailQueuePersisterSupervisorAsync(
                    _thumbnailQueuePersisterCts.Token
                );
            }

            if (_everythingWatchPollTask == null || _everythingWatchPollTask.IsCompleted)
            {
                DebugRuntimeLog.TaskStart(
                    nameof(RunEverythingWatchPollLoopAsync),
                    $"trigger={trigger}"
                );
                _everythingWatchPollTask = RunEverythingWatchPollLoopAsync(
                    _everythingWatchPollCts.Token
                );
            }
        }

        private void StartStartupHeavyServicesIfNeeded(
            string startReason,
            int revisionForLog = 0,
            string sourceKind = StartupSourceDbFallback,
            string feedState = ""
        )
        {
            if (_startupHeavyServicesStarted)
            {
                return;
            }

            int revision = ResolveStartupWarmPathRevision(revisionForLog);
            string trigger = ResolveStartupWarmPathTrigger();
            string resolvedFeedState = string.IsNullOrWhiteSpace(feedState)
                ? ResolveStartupHeavyServicesFeedState(startReason)
                : feedState;
            _startupHeavyServicesStarted = true;
            SetThumbnailQueueInputEnabled(true);
            EnsureStartupBackgroundTasksRunning(startReason);
            QueueStartupWatcherCreation(_startupSessionRevision);
            DebugRuntimeLog.TaskStart(nameof(CheckFolderAsync), $"mode=Auto trigger={startReason}");
            _ = QueueCheckFolderAsync(CheckMode.Auto, startReason);
            StartKanaBackfillIfNeeded(startReason);
            LogStartupWarmPathMilestone(
                "heavy services started",
                revision,
                trigger,
                sourceKind,
                resolvedFeedState,
                $"start_reason={startReason}"
            );
        }

        private void ResetStartupFeedState(string reason)
        {
            _startupLoadCoordinator.CancelCurrent();
            _startupFeedIsPartialActive = false;
            _startupFeedLoadedAllPages = false;
            _startupLightServicesStarted = false;
            _startupHeavyServicesStarted = false;
            _startupSessionRevision = 0;
            _startupSessionTrigger = StartupWarmPathTrigger;
            ClearStartupContinuationState();

            DebugRuntimeLog.Write(
                "ui-tempo",
                $"startup feed reset: reason={reason} trigger={ResolveStartupWarmPathTrigger()} elapsed_ms={_startupUiStopwatch.ElapsedMilliseconds}"
            );
        }

        private void CancelStartupFeed(string reason)
        {
            if (!_startupFeedIsPartialActive)
            {
                return;
            }

            _startupLoadCoordinator.CancelCurrent();
            _startupFeedIsPartialActive = false;
            int revision = _startupSessionRevision;
            _startupSessionRevision = 0;
            ClearStartupContinuationState();

            LogStartupWarmPathMilestone(
                "feed canceled",
                revision,
                ResolveStartupWarmPathTrigger(),
                StartupSourceDbFallback,
                "canceled",
                $"reason={reason}"
            );
            if (!string.Equals(reason, "startup-fallback", StringComparison.Ordinal))
            {
                StartStartupHeavyServicesIfNeeded(
                    $"StartupCanceled:{reason}",
                    revision,
                    StartupSourceDbFallback,
                    "canceled"
                );
            }
        }

        /// <summary>
        /// 検索側で全件 source の再構築まで成功した時だけ、段階ロードの未完了状態を閉じる。
        /// </summary>
        private void MarkStartupSourceCompleteAfterFullReload(
            int requestRevision,
            int sourceCount,
            string fullReloadReason
        )
        {
            if (requestRevision != Volatile.Read(ref _filterAndSortRequestRevision))
            {
                return;
            }

            _startupFeedIsPartialActive = false;
            _startupFeedLoadedAllPages = true;
            ClearStartupContinuationState();

            DebugRuntimeLog.Write(
                "ui-tempo",
                $"startup source completed by full reload: filter_revision={requestRevision} source_count={sourceCount} full_reload_reason={fullReloadReason}"
            );
        }

        private MovieRecords CreateStartupMovieRecordFromSource(
            MainDbMovieReadItemResult source,
            MovieRecordBulkBuildContext bulkContext,
            MovieRecordBulkBuildCache bulkCache
        )
        {
            string[] thumbErrorPath =
            [
                @"errorSmall.jpg",
                @"errorBig.jpg",
                @"errorGrid.jpg",
                @"errorList.jpg",
                @"errorBig.jpg",
            ];
            string[] thumbPath = new string[thumbErrorPath.Length];
            string imagesDirectoryPath = bulkContext.ImagesDirectoryPath;
            // 管理サムネ欠損時の同名source image探索は、6用途で1回だけ共有する。
            LazyThumbnailSourceImagePathResolver sourceImageResolver = new(source.MoviePath);

            for (int i = 0; i < thumbErrorPath.Length; i++)
            {
                thumbPath[i] = ResolveThumbnailDisplayPath(
                    bulkContext.ThumbnailOutPaths[i],
                    bulkCache.ThumbnailFileNamesByTab[i],
                    source.MoviePath,
                    source.MovieName,
                    source.Hash,
                    Path.Combine(imagesDirectoryPath, thumbErrorPath[i]),
                    sourceImageResolver
                );
            }

            string thumbPathDetail = ResolveThumbnailDisplayPath(
                bulkContext.DetailThumbnailOutPath,
                bulkCache.DetailThumbnailFileNames,
                source.MoviePath,
                source.MovieName,
                source.Hash,
                Path.Combine(imagesDirectoryPath, thumbErrorPath[2]),
                sourceImageResolver
            );

            List<string> tagArray = [];
            if (!string.IsNullOrWhiteSpace(source.TagRaw))
            {
                string[] splitTags = source.TagRaw.Split(
                    Environment.NewLine,
                    StringSplitOptions.RemoveEmptyEntries
                );
                foreach (string tagItem in splitTags)
                {
                    tagArray.Add(tagItem);
                }
            }

            string cleanedTags = MyRegex().Replace(source.TagRaw ?? "", "");
            string ext = Path.GetExtension(source.MoviePath);
            string movieBody = Path.GetFileNameWithoutExtension(source.MoviePath);

            return new MovieRecords
            {
                Movie_Id = source.MovieId,
                Movie_Name = $"{source.MovieName}{ext}",
                Movie_Body = movieBody,
                Movie_Path = source.MoviePath,
                Movie_Length = new TimeSpan(0, 0, (int)source.MovieLengthSeconds).ToString(
                    @"hh\:mm\:ss"
                ),
                Movie_Size = source.MovieSize,
                Last_Date = source.LastDate == DateTime.MinValue
                    ? ""
                    : source.LastDate.ToString("yyyy-MM-dd HH:mm:ss"),
                File_Date = source.FileDate == DateTime.MinValue
                    ? ""
                    : source.FileDate.ToString("yyyy-MM-dd HH:mm:ss"),
                Regist_Date = source.RegistDate == DateTime.MinValue
                    ? ""
                    : source.RegistDate.ToString("yyyy-MM-dd HH:mm:ss"),
                Score = source.Score,
                View_Count = source.ViewCount,
                Hash = source.Hash,
                Container = source.Container,
                Video = source.Video,
                Audio = source.Audio,
                Extra = "",
                Title = "",
                Album = "",
                Artist = "",
                Grouping = "",
                Writer = "",
                Genre = "",
                Track = "",
                Camera = "",
                Create_Time = "",
                Kana = source.Kana,
                Roma = "",
                Tags = cleanedTags,
                Tag = tagArray,
                Comment1 = source.Comment1,
                Comment2 = source.Comment2,
                Comment3 = source.Comment3,
                ThumbPathSmall = thumbPath[0],
                ThumbPathBig = thumbPath[1],
                ThumbPathGrid = thumbPath[2],
                ThumbPathList = thumbPath[3],
                ThumbPathBig10 = thumbPath[4],
                ThumbDetail = thumbPathDetail,
                Drive = Path.GetPathRoot(source.MoviePath),
                Dir = Path.GetDirectoryName(source.MoviePath),
                IsExists = true,
                Ext = ext,
            };
        }

        private void RememberStartupContinuationState(
            StartupFeedRequest request,
            MovieRecordBulkBuildContext bulkContext,
            MovieRecordBulkBuildCache bulkCache,
            int revision,
            CancellationToken cancellationToken,
            int nextPageIndex,
            bool hasMorePages
        )
        {
            if (!_startupLoadCoordinator.IsCurrent(revision))
            {
                return;
            }

            _startupContinuationRequest = request;
            _startupContinuationBulkContext = bulkContext;
            _startupContinuationBulkCache = bulkCache;
            _startupContinuationCancellationToken = cancellationToken;
            _startupContinuationRevision = revision;
            _startupNextPageIndex = nextPageIndex;
            _startupHasMorePages = hasMorePages;
            _startupAppendInFlight = false;

            DebugRuntimeLog.Write(
                "ui-tempo",
                $"startup warm path feed parked: revision={revision} trigger={ResolveStartupWarmPathTrigger()} source={StartupSourceDbFallback} feed_state=partial-feed visible_total={MainVM.FilteredMovieRecs.Count} next_page={nextPageIndex} threshold={StartupLoadMoreNearEndThreshold} elapsed_ms={_startupUiStopwatch.ElapsedMilliseconds}"
            );
        }

        private void ClearStartupContinuationState()
        {
            _startupContinuationRequest = null;
            _startupContinuationBulkContext = null;
            _startupContinuationBulkCache = null!;
            _startupContinuationCancellationToken = default;
            _startupContinuationRevision = 0;
            _startupNextPageIndex = 1;
            _startupHasMorePages = false;
            _startupAppendInFlight = false;
            _upperTabStartupAppendSuppressUntilUtcTicks = 0;
            _upperTabStartupAppendRetryTimer?.Stop();
        }

        private void TryScheduleStartupAppendForCurrentViewport(string trigger)
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.InvokeAsync(
                    () => TryScheduleStartupAppendForCurrentViewport(trigger),
                    DispatcherPriority.Background
                );
                return;
            }

            if (
                !ShouldRequestStartupAppend(
                    IsStartupFeedPartialActive,
                    _startupHasMorePages,
                    _startupAppendInFlight,
                    MainVM?.FilteredMovieRecs?.Count ?? 0,
                    _activeUpperTabVisibleRange.HasVisibleItems,
                    _activeUpperTabVisibleRange.LastNearVisibleIndex,
                    StartupLoadMoreNearEndThreshold
                )
            )
            {
                return;
            }

            if (!_startupContinuationRequest.HasValue || !_startupContinuationBulkContext.HasValue)
            {
                return;
            }

            if (
                TryGetStartupAppendRetryDelayMs(
                    DateTime.UtcNow.Ticks,
                    _upperTabStartupAppendSuppressUntilUtcTicks,
                    out int retryDelayMs
                )
            )
            {
                ScheduleStartupAppendRetry(retryDelayMs);
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"startup append suppressed by page scroll: trigger={trigger} retry_in_ms={retryDelayMs} last_near={_activeUpperTabVisibleRange.LastNearVisibleIndex} visible_total={MainVM.FilteredMovieRecs.Count}"
                );
                return;
            }

            int revision = _startupContinuationRevision;
            if (!_startupLoadCoordinator.IsCurrent(revision))
            {
                return;
            }

            StartupFeedRequest request = _startupContinuationRequest.Value;
            MovieRecordBulkBuildContext bulkContext = _startupContinuationBulkContext.Value;
            MovieRecordBulkBuildCache bulkCache = _startupContinuationBulkCache;
            CancellationToken cancellationToken = _startupContinuationCancellationToken;
            int pageIndex = _startupNextPageIndex;
            _startupAppendInFlight = true;

            DebugRuntimeLog.Write(
                "ui-tempo",
                $"startup page request: revision={revision} page={pageIndex} trigger={trigger} last_near={_activeUpperTabVisibleRange.LastNearVisibleIndex} visible_total={MainVM.FilteredMovieRecs.Count}"
            );

            _ = LoadStartupContinuationPageAsync(
                request,
                bulkContext,
                bulkCache,
                pageIndex,
                revision,
                cancellationToken
            );
        }

        private async Task LoadStartupContinuationPageAsync(
            StartupFeedRequest request,
            MovieRecordBulkBuildContext bulkContext,
            MovieRecordBulkBuildCache bulkCache,
            int pageIndex,
            int revision,
            CancellationToken cancellationToken
        )
        {
            try
            {
                StartupFeedPage page = await LoadStartupFeedPageAsync(
                    request,
                    pageIndex,
                    bulkContext,
                    bulkCache,
                    cancellationToken
                );
                if (!_startupLoadCoordinator.IsCurrent(revision))
                {
                    return;
                }

                await Dispatcher.InvokeAsync(
                    () => ApplyStartupAppendPage(page, revision),
                    DispatcherPriority.Background
                );
            }
            catch (OperationCanceledException)
            {
                // 新しい要求へ切り替わっただけなので黙って終える。
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(
                    () =>
                    {
                        if (_startupContinuationRevision == revision)
                        {
                            _startupAppendInFlight = false;
                        }
                    },
                    DispatcherPriority.Background
                );
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"startup page request failed: revision={revision} page={pageIndex} err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

        internal static bool ShouldRequestStartupAppend(
            bool isStartupFeedPartialActive,
            bool hasMorePages,
            bool appendInFlight,
            int loadedCount,
            bool hasVisibleItems,
            int lastNearVisibleIndex,
            int nearEndThreshold
        )
        {
            if (
                !isStartupFeedPartialActive
                || !hasMorePages
                || appendInFlight
                || loadedCount < 1
                || !hasVisibleItems
            )
            {
                return false;
            }

            int safeThreshold = Math.Max(1, nearEndThreshold);
            int triggerIndex = Math.Max(0, loadedCount - safeThreshold);
            return lastNearVisibleIndex >= triggerIndex;
        }
    }
}
