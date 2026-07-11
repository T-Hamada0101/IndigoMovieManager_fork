using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using IndigoMovieManager.DB;
using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        /// <summary>
        /// DB再取得から検索・並び替え・画面反映まで、一覧要求の流れをまとめて開始する。
        /// </summary>
        public void FilterAndSort(string id, bool IsGetNew = false)
        {
            CancelStartupFeed("filter-sort");
            _ = FilterAndSortAsync(id, IsGetNew);
        }

        private async Task FilterAndSortAsync(
            string id,
            bool isGetNew,
            CancellationToken externalCancellationToken = default
        )
        {
            using IDisposable uiHangScope = TrackUiHangActivity(UiHangActivityKind.Database);
            Stopwatch totalStopwatch = Stopwatch.StartNew();
            int requestRevision = Interlocked.Increment(ref _filterAndSortRequestRevision);
            using CancellationTokenSource filterAndSortCancellation =
                BeginFilterAndSortCancellation();
            using CancellationTokenSource linkedFilterAndSortCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    filterAndSortCancellation.Token,
                    externalCancellationToken
                );
            CancellationToken filterAndSortCancellationToken =
                linkedFilterAndSortCancellation.Token;
            bool deferUiApplyForExternalCancellation =
                externalCancellationToken.CanBeCanceled;
            DataTable latestMovieData = movieData;
            MovieRecords[] latestMovieRecords = null;
            long dbLoadElapsedMs = 0;
            long sourceApplyElapsedMs = 0;
            long filterSnapshotElapsedMs = 0;
            long filterSortElapsedMs = 0;
            long refreshElapsedMs = 0;
            string executionRoute = MainWindow.ResolveFilterSortExecutionRouteLabel(
                hasSnapshotData: latestMovieData != null,
                startupFeedLoadedAllPages: _startupFeedLoadedAllPages,
                isGetNew: isGetNew
            );
            string fullReloadReason = MainWindow.ResolveFilterSortFullReloadReason(
                hasSnapshotData: latestMovieData != null,
                startupFeedLoadedAllPages: _startupFeedLoadedAllPages,
                isGetNew: isGetNew
            );

            DebugRuntimeLog.Write(
                "ui-tempo",
                $"filter start: revision={requestRevision} sort={id} route={executionRoute} full_reload_reason={fullReloadReason} is_get_new={isGetNew} keyword='{MainVM.DbInfo.SearchKeyword}'"
            );

            if ((latestMovieData == null && !_startupFeedLoadedAllPages) || isGetNew)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"filter stage begin: revision={requestRevision} stage=db-reload sort={id} is_get_new={isGetNew}"
                );
                Stopwatch dbLoadStopwatch = Stopwatch.StartNew();
                string dbFullPath = MainVM.DbInfo.DBFullPath;
                // full reload の movie 読みは facade へ寄せ、並び順の SQL を UI から剥がす。
                try
                {
                    filterAndSortCancellationToken.ThrowIfCancellationRequested();
                    latestMovieData = await Task.Run(
                        () => _mainDbMovieReadFacade.LoadMovieTableForSort(dbFullPath, id),
                        filterAndSortCancellationToken
                    );
                    filterAndSortCancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                    when (filterAndSortCancellationToken.IsCancellationRequested)
                {
                    dbLoadStopwatch.Stop();
                    DebugRuntimeLog.Write(
                        "ui-tempo",
                        $"filter canceled: revision={requestRevision} stage=db-reload elapsed_ms={totalStopwatch.ElapsedMilliseconds}"
                    );
                    return;
                }
                dbLoadStopwatch.Stop();
                dbLoadElapsedMs = dbLoadStopwatch.ElapsedMilliseconds;
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"filter stage end: revision={requestRevision} stage=db-reload rows={latestMovieData?.Rows.Count ?? -1} elapsed_ms={dbLoadElapsedMs}"
                );
                if (latestMovieData == null)
                {
                    DebugRuntimeLog.Write(
                        "ui-tempo",
                        $"filter abort: revision={requestRevision} reason=db_reload_returned_null elapsed_ms={totalStopwatch.ElapsedMilliseconds}"
                    );
                    return;
                }
                if (requestRevision != _filterAndSortRequestRevision)
                {
                    DebugRuntimeLog.Write(
                        "ui-tempo",
                        $"filter skip stale reload: revision={requestRevision} current_revision={_filterAndSortRequestRevision} db_reload_ms={dbLoadElapsedMs}"
                    );
                    return;
                }
                movieData = latestMovieData;
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"filter stage begin: revision={requestRevision} stage=source-apply rows={latestMovieData.Rows.Count}"
                );
                Stopwatch sourceApplyStopwatch = Stopwatch.StartNew();
                MovieRecordSourceApplyResult sourceApplyResult;
                try
                {
                    sourceApplyResult = await SetRecordsToSource(
                        latestMovieData,
                        requestRevision,
                        filterAndSortCancellationToken,
                        deferUiApplyForExternalCancellation
                    );
                }
                catch (OperationCanceledException)
                    when (filterAndSortCancellationToken.IsCancellationRequested)
                {
                    movieData = null;
                    sourceApplyStopwatch.Stop();
                    DebugRuntimeLog.Write(
                        "ui-tempo",
                        $"filter canceled: revision={requestRevision} stage=source-apply elapsed_ms={totalStopwatch.ElapsedMilliseconds}"
                    );
                    return;
                }
                latestMovieRecords = sourceApplyResult.Items;
                // DB読み込みと変換が完了したので、rawなDataTable参照を残さずに解放する。
                movieData = null;
                if (requestRevision != _filterAndSortRequestRevision)
                {
                    sourceApplyStopwatch.Stop();
                    DebugRuntimeLog.Write(
                        "ui-tempo",
                        $"source apply: revision={requestRevision} rows={latestMovieData.Rows.Count} items={latestMovieRecords?.Length ?? -1} bulk_cache_ms={sourceApplyResult.BulkCacheElapsedMs} row_convert_ms={sourceApplyResult.RowConvertElapsedMs} source_image_probe_ms={sourceApplyResult.SourceImageProbeElapsedMs} source_image_probe_count={sourceApplyResult.SourceImageProbeCount} source_image_probe_hit={sourceApplyResult.SourceImageCacheHitCount} replace_movie_recs_ms={sourceApplyResult.ReplaceMovieRecsElapsedMs} queue_movie_exists_ms={sourceApplyResult.QueueMovieExistsElapsedMs} invalidate_thumbnail_error_ms=0 background_ms={sourceApplyResult.BackgroundElapsedMs} ui_ms={sourceApplyResult.ReplaceMovieRecsElapsedMs + sourceApplyResult.QueueMovieExistsElapsedMs} total_ms={sourceApplyStopwatch.ElapsedMilliseconds} stale=true"
                    );
                    return;
                }
                MarkStartupSourceCompleteAfterFullReload(
                    requestRevision,
                    latestMovieRecords?.Length ?? 0,
                    fullReloadReason
                );
                Stopwatch invalidateThumbnailErrorStopwatch = Stopwatch.StartNew();
                InvalidateThumbnailErrorRecords(refreshIfVisible: true);
                invalidateThumbnailErrorStopwatch.Stop();
                sourceApplyStopwatch.Stop();
                sourceApplyElapsedMs = sourceApplyStopwatch.ElapsedMilliseconds;
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"source apply: revision={requestRevision} rows={latestMovieData.Rows.Count} items={latestMovieRecords?.Length ?? -1} bulk_cache_ms={sourceApplyResult.BulkCacheElapsedMs} row_convert_ms={sourceApplyResult.RowConvertElapsedMs} source_image_probe_ms={sourceApplyResult.SourceImageProbeElapsedMs} source_image_probe_count={sourceApplyResult.SourceImageProbeCount} source_image_probe_hit={sourceApplyResult.SourceImageCacheHitCount} replace_movie_recs_ms={sourceApplyResult.ReplaceMovieRecsElapsedMs} queue_movie_exists_ms={sourceApplyResult.QueueMovieExistsElapsedMs} invalidate_thumbnail_error_ms={invalidateThumbnailErrorStopwatch.ElapsedMilliseconds} background_ms={sourceApplyResult.BackgroundElapsedMs} ui_ms={sourceApplyResult.ReplaceMovieRecsElapsedMs + sourceApplyResult.QueueMovieExistsElapsedMs + invalidateThumbnailErrorStopwatch.ElapsedMilliseconds} total_ms={sourceApplyElapsedMs} stale=false"
                );
            }

            if (requestRevision != _filterAndSortRequestRevision)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"filter skip stale apply: revision={requestRevision} current_revision={_filterAndSortRequestRevision} elapsed_ms={totalStopwatch.ElapsedMilliseconds}"
                );
                return;
            }

            Stopwatch filterSortStopwatch = Stopwatch.StartNew();
            string searchKeyword = MainVM.DbInfo.SearchKeyword;
            Stopwatch filterSnapshotStopwatch = Stopwatch.StartNew();
            MovieRecords[] filterSource = (latestMovieRecords?.AsEnumerable() ?? MainVM.MovieRecs)
                .Where(movie => movie != null)
                .ToArray();
            filterSnapshotStopwatch.Stop();
            filterSnapshotElapsedMs = filterSnapshotStopwatch.ElapsedMilliseconds;
            bool runOnBackground = MainWindow.ShouldRunFilterSortOnBackground(filterSource.Length);
            bool allowExpensiveAsciiPhoneticFallback =
                !MainWindow.ShouldUseFastAsciiSearchProjection(filterSource.Length);
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"filter stage begin: revision={requestRevision} stage=filter-sort-compute route={executionRoute} source={filterSource.Length} keyword='{searchKeyword}' background={runOnBackground} ascii_fast_projection={!allowExpensiveAsciiPhoneticFallback} snapshot_ms={filterSnapshotElapsedMs}"
            );

            MovieViewReadModelRequest readModelRequest = new()
            {
                RequestRevision = requestRevision,
                SortId = id,
                SearchKeyword = searchKeyword,
                RouteLabel = executionRoute,
                SourceMovies = filterSource,
                AllowExpensiveAsciiPhoneticFallback = allowExpensiveAsciiPhoneticFallback,
                CancellationToken = filterAndSortCancellationToken,
                FilterMovies = (movies, keyword, token, allowFallback) =>
                    MainVM.FilterMovies(movies, keyword, token, allowFallback),
                SortMovies = (movies, sortId) => MainVM.SortMovies(movies, sortId),
                Log = message => DebugRuntimeLog.Write("ui-tempo", message),
            };

            MovieViewReadModelResult readModelResult;
            try
            {
                readModelResult = runOnBackground
                    ? await Task.Run(
                        () => MovieViewReadModelBuilder.Build(readModelRequest),
                        filterAndSortCancellationToken
                    )
                    : MovieViewReadModelBuilder.Build(readModelRequest);
            }
            catch (OperationCanceledException)
                when (filterAndSortCancellationToken.IsCancellationRequested)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"filter canceled: revision={requestRevision} elapsed_ms={totalStopwatch.ElapsedMilliseconds}"
                );
                return;
            }
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"filter stage end: revision={requestRevision} stage=filter-sort-compute route={executionRoute} sorted={readModelResult.SortedMovies.Count} search_count={readModelResult.SearchCount} elapsed_ms={filterSortStopwatch.ElapsedMilliseconds}"
            );
            if (requestRevision != _filterAndSortRequestRevision)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"filter skip stale filter-sort: revision={requestRevision} current_revision={_filterAndSortRequestRevision} elapsed_ms={totalStopwatch.ElapsedMilliseconds}"
                );
                return;
            }
            filterSortStopwatch.Stop();
            filterSortElapsedMs = filterSortStopwatch.ElapsedMilliseconds;
            MovieViewReadModelApplyResult applyResult;
            if (
                deferUiApplyForExternalCancellation
                && Dispatcher != null
                && !Dispatcher.HasShutdownStarted
                && !Dispatcher.HasShutdownFinished
            )
            {
                // partial全件整合のUI反映前にInputを通し、Playerスクロールを必ず先行させる。
                try
                {
                    await Dispatcher.InvokeAsync(
                        () => { },
                        DispatcherPriority.Background,
                        filterAndSortCancellationToken
                    );
                }
                catch (OperationCanceledException)
                    when (filterAndSortCancellationToken.IsCancellationRequested)
                {
                    DebugRuntimeLog.Write(
                        "ui-tempo",
                        $"filter canceled: revision={requestRevision} stage=apply-dispatch elapsed_ms={totalStopwatch.ElapsedMilliseconds}"
                    );
                    return;
                }
            }

            if (filterAndSortCancellationToken.IsCancellationRequested)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"filter canceled: revision={requestRevision} stage=apply elapsed_ms={totalStopwatch.ElapsedMilliseconds}"
                );
                return;
            }

            if (
                !TryApplyMovieViewReadModelResultOnUiThread(
                    requestRevision,
                    readModelResult,
                    id,
                    "filter",
                    isSortOnly: false,
                    fallbackReason: fullReloadReason,
                    out applyResult
                )
            )
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"filter skip stale apply: revision={requestRevision} current_revision={_filterAndSortRequestRevision} elapsed_ms={totalStopwatch.ElapsedMilliseconds}"
                );
                return;
            }

            refreshElapsedMs = applyResult.SelectionRefreshElapsedMs;
            QueueVisibleSourceImageProbe("filter-apply");

            totalStopwatch.Stop();
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"filter end: revision={requestRevision} sort={id} route={executionRoute} full_reload_reason={fullReloadReason} is_get_new={isGetNew} count={MainVM.DbInfo.SearchCount} changed={applyResult.CollectionResult.HasChanges} update_mode={applyResult.UpdateMode} refresh_applied={applyResult.SelectionRefreshApplied} prefix={applyResult.CollectionResult.RetainedPrefixCount} suffix={applyResult.CollectionResult.RetainedSuffixCount} removed={applyResult.CollectionResult.RemovedCount} inserted={applyResult.CollectionResult.InsertedCount} moved={applyResult.CollectionResult.MovedCount} db_reload_ms={dbLoadElapsedMs} source_apply_ms={sourceApplyElapsedMs} snapshot_ms={filterSnapshotElapsedMs} filter_sort_ms={filterSortElapsedMs} refresh_ms={refreshElapsedMs} apply_ms={applyResult.ApplyElapsedMs} total_ms={totalStopwatch.ElapsedMilliseconds}"
            );
        }

        // in-memory に載っている一覧だけで再検索・再整列し、DB再読込なしで表示を更新する。
        private async Task RefreshMovieViewFromCurrentSourceAsync(
            string sortId,
            string traceName,
            UiHangActivityKind uiHangActivityKind,
            IReadOnlyList<WatchChangedMovie> changedMovies = null
        )
        {
            string resolvedTraceName = string.IsNullOrWhiteSpace(traceName)
                ? "memory-refresh"
                : traceName;
            using IDisposable uiHangScope = TrackUiHangActivity(uiHangActivityKind);
            Stopwatch totalStopwatch = Stopwatch.StartNew();
            int requestRevision = Interlocked.Increment(ref _filterAndSortRequestRevision);
            using CancellationTokenSource refreshCancellation = BeginFilterAndSortCancellation();
            CancellationToken refreshCancellationToken = refreshCancellation.Token;
            string resolvedSortId = string.IsNullOrWhiteSpace(sortId)
                ? MainVM?.DbInfo?.Sort ?? ""
                : sortId;
            string searchKeyword = MainVM?.DbInfo?.SearchKeyword ?? "";
            Stopwatch snapshotStopwatch = Stopwatch.StartNew();
            MovieViewReadModelSnapshot snapshot;
            try
            {
                snapshot = await CaptureMovieViewReadModelSnapshotOnUiThreadAsync(
                    changedMovies,
                    refreshCancellationToken
                );
            }
            catch (OperationCanceledException)
                when (refreshCancellationToken.IsCancellationRequested)
            {
                snapshotStopwatch.Stop();
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"{resolvedTraceName} refresh canceled: revision={requestRevision} current_revision={_filterAndSortRequestRevision} stage=snapshot elapsed_ms={totalStopwatch.ElapsedMilliseconds}"
                );
                return;
            }
            snapshotStopwatch.Stop();
            long snapshotElapsedMs = snapshotStopwatch.ElapsedMilliseconds;
            MovieRecords[] sourceMovies = snapshot.SourceMovies;
            MovieRecords[] currentFilteredMovies = snapshot.CurrentFilteredMovies;
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"{resolvedTraceName} refresh start: revision={requestRevision} sort={resolvedSortId} keyword='{searchKeyword}' source={sourceMovies.Length} changed_paths={changedMovies?.Count ?? 0} snapshot_ms={snapshotElapsedMs}"
            );

            Stopwatch filterSortStopwatch = Stopwatch.StartNew();
            bool allowExpensiveAsciiPhoneticFallback =
                !MainWindow.ShouldUseFastAsciiSearchProjection(sourceMovies.Length);
            MovieViewReadModelRequest readModelRequest = new()
            {
                RequestRevision = requestRevision,
                SortId = resolvedSortId,
                SearchKeyword = searchKeyword,
                RouteLabel = resolvedTraceName,
                SourceMovies = sourceMovies,
                CurrentFilteredMovies = currentFilteredMovies,
                ChangedMovies = changedMovies ?? [],
                UseChangedPathRefresh = true,
                AllowExpensiveAsciiPhoneticFallback = allowExpensiveAsciiPhoneticFallback,
                CancellationToken = refreshCancellationToken,
                FilterMovies = (movies, keyword, token, allowFallback) =>
                    MainVM.FilterMovies(movies, keyword, token, allowFallback),
                SortMovies = (movies, sortId) => MainVM.SortMovies(movies, sortId),
            };
            MovieViewReadModelResult readModelResult;
            try
            {
                readModelResult = await Task.Run(
                    () => MovieViewReadModelBuilder.Build(readModelRequest),
                    refreshCancellationToken
                );
            }
            catch (OperationCanceledException)
                when (refreshCancellationToken.IsCancellationRequested)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"{resolvedTraceName} refresh canceled: revision={requestRevision} current_revision={_filterAndSortRequestRevision} elapsed_ms={totalStopwatch.ElapsedMilliseconds}"
                );
                return;
            }
            catch (ObjectDisposedException) when (refreshCancellationToken.IsCancellationRequested)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"{resolvedTraceName} refresh canceled: revision={requestRevision} current_revision={_filterAndSortRequestRevision} reason=token-disposed elapsed_ms={totalStopwatch.ElapsedMilliseconds}"
                );
                return;
            }

            if (requestRevision != _filterAndSortRequestRevision)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"{resolvedTraceName} refresh skip stale compute: revision={requestRevision} current_revision={_filterAndSortRequestRevision}"
                );
                return;
            }

            MovieViewReadModelApplyResult applyResult = default;
            bool applied = false;
            try
            {
                if (Dispatcher == null || Dispatcher.CheckAccess())
                {
                    refreshCancellationToken.ThrowIfCancellationRequested();
                    // 既にUIスレッドなら再ディスパッチせず、その場で適用して待機時間を増やさない。
                    applied = TryApplyMovieViewReadModelResultOnUiThread(
                        requestRevision,
                        readModelResult,
                        resolvedSortId,
                        resolvedTraceName,
                        isSortOnly: false,
                        fallbackReason: readModelResult.ChangedPathFallbackReason,
                        out applyResult
                    );
                }
                else
                {
                    await Dispatcher.InvokeAsync(
                        () =>
                        {
                            refreshCancellationToken.ThrowIfCancellationRequested();
                            applied = TryApplyMovieViewReadModelResultOnUiThread(
                                requestRevision,
                                readModelResult,
                                resolvedSortId,
                                resolvedTraceName,
                                isSortOnly: false,
                                fallbackReason: readModelResult.ChangedPathFallbackReason,
                                out applyResult
                            );
                        },
                        DispatcherPriority.Background,
                        refreshCancellationToken
                    );
                }
            }
            catch (OperationCanceledException)
                when (refreshCancellationToken.IsCancellationRequested)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"{resolvedTraceName} refresh canceled: revision={requestRevision} current_revision={_filterAndSortRequestRevision} stage=apply-dispatch elapsed_ms={totalStopwatch.ElapsedMilliseconds}"
                );
                return;
            }

            if (!applied)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"{resolvedTraceName} refresh skip stale apply: revision={requestRevision} current_revision={_filterAndSortRequestRevision}"
                );
                return;
            }

            filterSortStopwatch.Stop();
            totalStopwatch.Stop();
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"{resolvedTraceName} refresh end: revision={requestRevision} sort={resolvedSortId} count={readModelResult.SearchCount} changed={applyResult.CollectionResult.HasChanges} changed_path_mode={(readModelResult.UsedChangedPathRefresh ? "partial" : "full")} changed_path_fallback={readModelResult.ChangedPathFallbackReason} reuse_order={readModelResult.CanReuseCurrentOrder} prefix={applyResult.CollectionResult.RetainedPrefixCount} suffix={applyResult.CollectionResult.RetainedSuffixCount} removed={applyResult.CollectionResult.RemovedCount} inserted={applyResult.CollectionResult.InsertedCount} moved={applyResult.CollectionResult.MovedCount} snapshot_ms={snapshotElapsedMs} filter_sort_ms={filterSortStopwatch.ElapsedMilliseconds} apply_ms={applyResult.ApplyElapsedMs} total_ms={totalStopwatch.ElapsedMilliseconds}"
            );
        }

        // rename 後は DB を読み直さず、いまメモリ上にある一覧だけで再検索・再整列する。
        private Task RefreshMovieViewAfterRenameAsync(
            string sortId,
            IReadOnlyList<WatchChangedMovie> changedMovies = null
        )
        {
            return RefreshMovieViewFromCurrentSourceAsync(
                sortId,
                "rename",
                UiHangActivityKind.Watch,
                changedMovies
            );
        }

        // 今の検索文字列が dirty fields に依存しないなら、現在の一致状態をそのまま再利用できる。
        internal static bool DoesSearchDependOnDirtyFields(
            string searchKeyword,
            WatchMovieDirtyFields dirtyFields
        )
        {
            return MovieViewReadModelBuilder.DoesSearchDependOnDirtyFields(
                searchKeyword,
                dirtyFields
            );
        }

        // 小件数は同期処理で済ませ、Task.Run の切り替えコストを避ける。
        internal static bool ShouldRunFilterSortOnBackground(int sourceCount)
        {
            return MovieViewReadModelBuilder.ShouldRunFilterSortOnBackground(sourceCount);
        }

        // 大件数検索では、ASCII入力のたびに名前/パスから読み仮名解析へ戻さない。
        internal static bool ShouldUseFastAsciiSearchProjection(int sourceCount)
        {
            return MovieViewReadModelBuilder.ShouldUseFastAsciiSearchProjection(sourceCount);
        }

        private CancellationTokenSource BeginFilterAndSortCancellation()
        {
            CancellationTokenSource current = new();
            CancellationTokenSource previous;
            lock (_filterAndSortCancellationGate)
            {
                previous = _filterAndSortCancellation;
                _filterAndSortCancellation = current;
            }

            try
            {
                previous?.Cancel();
            }
            catch (ObjectDisposedException) { }

            return current;
        }

        // query-only と full reload を短い札にして、ui-tempo ログで経路を追いやすくする。
        internal static string ResolveFilterSortExecutionRouteLabel(
            bool hasSnapshotData,
            bool startupFeedLoadedAllPages,
            bool isGetNew
        )
        {
            return ((!hasSnapshotData && !startupFeedLoadedAllPages) || isGetNew)
                ? "full-reload"
                : "query-only";
        }

        // full reload へ戻る理由を短い札にし、次の差分化候補をログから拾えるようにする。
        internal static string ResolveFilterSortFullReloadReason(
            bool hasSnapshotData,
            bool startupFeedLoadedAllPages,
            bool isGetNew
        )
        {
            if (isGetNew)
            {
                return "is-get-new";
            }

            if (!hasSnapshotData && !startupFeedLoadedAllPages)
            {
                return "no-snapshot-startup-partial";
            }

            return "none";
        }

        // changed movie が現在の sort key に触っていないなら、既存の並び順をそのまま使える。
        internal static bool DoesCurrentSortDependOnDirtyFields(
            string sortId,
            WatchMovieDirtyFields dirtyFields
        )
        {
            return MovieViewReadModelBuilder.DoesCurrentSortDependOnDirtyFields(
                sortId,
                dirtyFields
            );
        }
    }
}
