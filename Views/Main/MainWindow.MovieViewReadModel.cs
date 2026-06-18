using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using IndigoMovieManager.DB;
using IndigoMovieManager.Infrastructure;
using IndigoMovieManager.UpperTabs.Common;
using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private readonly record struct MovieViewReadModelSnapshot(
            MovieRecords[] SourceMovies,
            MovieRecords[] CurrentFilteredMovies
        );

        private async Task<MovieViewReadModelSnapshot> CaptureMovieViewReadModelSnapshotOnUiThreadAsync(
            IReadOnlyList<WatchChangedMovie> changedMovies,
            CancellationToken cancellationToken
        )
        {
            if (Dispatcher == null || Dispatcher.CheckAccess())
            {
                cancellationToken.ThrowIfCancellationRequested();
                return CaptureMovieViewReadModelSnapshot(changedMovies);
            }

            return await Dispatcher.InvokeAsync(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return CaptureMovieViewReadModelSnapshot(changedMovies);
                },
                DispatcherPriority.Background,
                cancellationToken
            );
        }

        private MovieViewReadModelSnapshot CaptureMovieViewReadModelSnapshot(
            IReadOnlyList<WatchChangedMovie> changedMovies
        )
        {
            MovieRecords[] sourceMovies = MainVM?
                .MovieRecs?
                .Where(movie => movie != null)
                .ToArray() ?? [];

            ApplyObservedStatesToMovieRecords(sourceMovies, changedMovies);

            MovieRecords[] currentFilteredMovies = MainVM?
                .FilteredMovieRecs?
                .Where(movie => movie != null)
                .ToArray() ?? [];

            return new MovieViewReadModelSnapshot(sourceMovies, currentFilteredMovies);
        }

        private static void ApplyObservedStatesToMovieRecords(
            IReadOnlyList<MovieRecords> sourceMovies,
            IReadOnlyList<WatchChangedMovie> changedMovies
        )
        {
            if (sourceMovies == null || changedMovies == null || changedMovies.Count < 1)
            {
                return;
            }

            List<WatchChangedMovie> observedMovies = changedMovies
                .Where(changedMovie => changedMovie.ObservedState.HasValue)
                .ToList();
            if (observedMovies.Count < 1)
            {
                return;
            }

            Dictionary<string, MovieRecords> sourceByPath = BuildChangedSourceMovieLookup(
                sourceMovies,
                observedMovies
            );
            foreach (WatchChangedMovie changedMovie in observedMovies)
            {
                if (
                    !string.IsNullOrWhiteSpace(changedMovie.MoviePath)
                    && sourceByPath.TryGetValue(changedMovie.MoviePath, out MovieRecords sourceMovie)
                )
                {
                    ApplyObservedStateToMovieRecord(sourceMovie, changedMovie.ObservedState);
                }
            }
        }

        private readonly record struct MovieViewReadModelApplyResult(
            FilteredMovieRecsUpdateResult CollectionResult,
            FilteredMovieRecsUpdateMode UpdateMode,
            MovieViewDiff Diff,
            bool SelectionRefreshApplied,
            long ApplyElapsedMs,
            long SelectionRefreshElapsedMs
        );

        // ReadModel 計算結果の UI 反映を 1 箇所へ寄せ、呼び出し元は計算と待機だけに集中させる。
        private bool TryApplyMovieViewReadModelResultOnUiThread(
            int requestRevision,
            MovieViewReadModelResult readModelResult,
            string resolvedSortId,
            string visibleRefreshReason,
            bool isSortOnly,
            string fallbackReason,
            out MovieViewReadModelApplyResult applyResult
        )
        {
            applyResult = default;
            if (requestRevision != _filterAndSortRequestRevision || readModelResult == null)
            {
                return false;
            }

            Stopwatch applyStopwatch = Stopwatch.StartNew();
            string resolvedVisibleRefreshReason = string.IsNullOrWhiteSpace(visibleRefreshReason)
                ? "memory-refresh"
                : visibleRefreshReason;
            string resolvedFallbackReason = string.IsNullOrWhiteSpace(fallbackReason)
                ? "none"
                : fallbackReason;
            int currentTabIndex = TryGetCurrentUpperTabFixedIndex(out int resolvedTabIndex)
                ? resolvedTabIndex
                : UpperTabGridFixedIndex;
            FilteredMovieRecsUpdateMode updateMode =
                UpperTabCollectionUpdatePolicy.ResolveUpdateMode(
                    currentTabIndex,
                    isSortOnly: isSortOnly
                );
            IReadOnlyList<MovieRecords> sortedMovies = readModelResult.SortedMovies ?? [];

            DebugRuntimeLog.Write(
                "ui-tempo",
                $"readmodel apply begin: request_revision={requestRevision} reason={resolvedVisibleRefreshReason} result_count={sortedMovies.Count} update_mode={updateMode} fallback_reason={resolvedFallbackReason}"
            );
            MovieRecords selectedBeforeCollectionApply = GetSelectedItemByTabIndex();
            MainVM.DbInfo.SearchCount = readModelResult.SearchCount;
            filterList = sortedMovies;
            FilteredMovieRecsUpdateResult collectionResult = MainVM.ReplaceFilteredMovieRecs(
                sortedMovies,
                updateMode: updateMode
            );
            UpdateExtensionDetailVisibilityBySearchCount();

            Stopwatch selectionRefreshStopwatch = Stopwatch.StartNew();
            bool shouldRefresh = RefreshSelectionDetailAfterCollectionApplyIfNeeded(
                selectedBeforeCollectionApply,
                collectionResult,
                currentTabIndex,
                updateMode
            );
            selectionRefreshStopwatch.Stop();

            if (collectionResult.HasChanges)
            {
                NotifyUpperTabViewportSourceChanged();
                RequestUpperTabVisibleRangeRefresh(
                    immediate: true,
                    reason: resolvedVisibleRefreshReason
                );
            }

            if (!isSortOnly && string.Equals(resolvedSortId, "28", StringComparison.Ordinal))
            {
                RefreshThumbnailErrorRecords(force: true);
            }

            applyStopwatch.Stop();
            MovieViewDiff diff = MovieViewDiffFactory.FromCollectionUpdate(
                sourceRevision: requestRevision,
                viewRevision: _filterAndSortRequestRevision,
                updateMode,
                collectionResult,
                shouldRefresh,
                resolvedFallbackReason
            );
            applyResult = new MovieViewReadModelApplyResult(
                collectionResult,
                updateMode,
                diff,
                shouldRefresh,
                applyStopwatch.ElapsedMilliseconds,
                selectionRefreshStopwatch.ElapsedMilliseconds
            );
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"readmodel apply end: request_revision={requestRevision} result_count={sortedMovies.Count} changed={collectionResult.HasChanges} update_mode={updateMode} fallback_reason={resolvedFallbackReason} {MovieViewDiffApplyPolicy.BuildDiffLogFields(diff)} refresh_applied={shouldRefresh} apply_ms={applyResult.ApplyElapsedMs}"
            );
            return true;
        }

        /// <summary>
        /// 今表示中の一覧だけを並べ直し、XAML バインディングを壊さず中身だけ更新する。
        /// </summary>
        private void SortData(string id)
        {
            _ = SortDataFromLegacyCallerAsync(id);
        }

        private async Task SortDataFromLegacyCallerAsync(string id)
        {
            try
            {
                await SortDataAsync(id);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"sort legacy caller failed: sort={id} message={ex.Message}"
                );
            }
        }

        private async Task<bool> SortDataAsync(string id)
        {
            Stopwatch sw = Stopwatch.StartNew();
            int requestRevision = Interlocked.Increment(ref _filterAndSortRequestRevision);
            using CancellationTokenSource sortCancellation = BeginFilterAndSortCancellation();
            CancellationToken sortCancellationToken = sortCancellation.Token;
            Stopwatch snapshotStopwatch = Stopwatch.StartNew();
            MovieRecords[] source = MainVM.FilteredMovieRecs
                .Where(movie => movie != null)
                .ToArray();
            snapshotStopwatch.Stop();
            long snapshotElapsedMs = snapshotStopwatch.ElapsedMilliseconds;
            bool runOnBackground = ShouldRunFilterSortOnBackground(source.Length);
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"sort start: revision={requestRevision} sort={id} source={source.Length} background={runOnBackground} snapshot_ms={snapshotElapsedMs}"
            );
            try
            {
                MovieRecords[] sorted = runOnBackground
                    ? await Task.Run(
                        () =>
                        {
                            sortCancellationToken.ThrowIfCancellationRequested();
                            MovieRecords[] result = MainVM.SortMovies(source, id).ToArray();
                            sortCancellationToken.ThrowIfCancellationRequested();
                            return result;
                        },
                        sortCancellationToken
                    )
                    : MainVM.SortMovies(source, id).ToArray();
                sortCancellationToken.ThrowIfCancellationRequested();
                if (requestRevision != _filterAndSortRequestRevision)
                {
                    DebugRuntimeLog.Write(
                        "ui-tempo",
                        $"sort skip stale: revision={requestRevision} current_revision={_filterAndSortRequestRevision} sort={id} total_ms={sw.ElapsedMilliseconds}"
                    );
                    return false;
                }

                MovieViewReadModelApplyResult applyResult;
                if (
                    !TryApplyMovieViewReadModelResultOnUiThread(
                        requestRevision,
                        MovieViewReadModelResult.FromSorted(sorted, sorted.Length, "sort-only"),
                        id,
                        "sort",
                        isSortOnly: true,
                        fallbackReason: "sort-only",
                        out applyResult
                    )
                )
                {
                    DebugRuntimeLog.Write(
                        "ui-tempo",
                        $"sort skip stale apply: revision={requestRevision} current_revision={_filterAndSortRequestRevision} sort={id} total_ms={sw.ElapsedMilliseconds}"
                    );
                    return false;
                }
                sw.Stop();
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"sort end: revision={requestRevision} sort={id} changed={applyResult.CollectionResult.HasChanges} prefix={applyResult.CollectionResult.RetainedPrefixCount} suffix={applyResult.CollectionResult.RetainedSuffixCount} removed={applyResult.CollectionResult.RemovedCount} inserted={applyResult.CollectionResult.InsertedCount} moved={applyResult.CollectionResult.MovedCount} update_mode={applyResult.UpdateMode} refresh_applied={applyResult.SelectionRefreshApplied} count={sorted.Length} background={runOnBackground} snapshot_ms={snapshotElapsedMs} apply_ms={applyResult.ApplyElapsedMs} total_ms={sw.ElapsedMilliseconds}"
                );
                return true;
            }
            catch (OperationCanceledException) when (sortCancellationToken.IsCancellationRequested)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"sort canceled: revision={requestRevision} current_revision={_filterAndSortRequestRevision} sort={id} total_ms={sw.ElapsedMilliseconds}"
                );
                return false;
            }
            catch (Exception err)
            {
                MessageBox.Show(
                    err.Message,
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                throw;
            }
        }
    }
}
