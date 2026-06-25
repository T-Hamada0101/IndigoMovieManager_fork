using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using IndigoMovieManager.DB;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int KanaBackfillBatchSize = 100;
        private const int KanaBackfillInterBatchDelayMs = 50;

        private CancellationTokenSource _kanaBackfillCts = new();
        private Task _kanaBackfillTask;

        private void StartKanaBackfillIfNeeded(string trigger)
        {
            string dbPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                return;
            }

            if (_kanaBackfillTask != null && !_kanaBackfillTask.IsCompleted)
            {
                return;
            }

            _kanaBackfillCts.Dispose();
            _kanaBackfillCts = new CancellationTokenSource();
            DebugRuntimeLog.TaskStart(nameof(RunKanaBackfillAsync), $"trigger={trigger} db='{dbPath}'");
            _kanaBackfillTask = RunKanaBackfillAsync(dbPath, _kanaBackfillCts.Token);
        }

        private void CancelKanaBackfill(string reason)
        {
            try
            {
                if (_kanaBackfillCts.IsCancellationRequested)
                {
                    return;
                }

                DebugRuntimeLog.Write("kana", $"backfill cancel requested: reason={reason}");
                _kanaBackfillCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 終了処理中の多重停止は静かに吸収する。
            }
        }

        private async Task RunKanaBackfillAsync(string dbPath, CancellationToken cancellationToken)
        {
            int totalMovieUpdated = 0;
            int totalBookmarkUpdated = 0;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    List<KanaBackfillTarget> movieTargets = await Task.Run(
                        () => SQLite.ReadMovieKanaBackfillTargets(dbPath, KanaBackfillBatchSize),
                        cancellationToken
                    );
                    List<KanaBackfillTarget> bookmarkTargets = await Task.Run(
                        () => SQLite.ReadBookmarkKanaBackfillTargets(dbPath, KanaBackfillBatchSize),
                        cancellationToken
                    );

                    if (movieTargets.Count < 1 && bookmarkTargets.Count < 1)
                    {
                        break;
                    }

                    List<KanaBackfillUpdate> movieUpdates = BuildReadingUpdates(movieTargets);
                    List<KanaBackfillUpdate> bookmarkUpdates = BuildReadingUpdates(bookmarkTargets);

                    if (movieUpdates.Count > 0)
                    {
                        totalMovieUpdated += await Task.Run(
                            () => SQLite.UpdateMovieKanaBatch(dbPath, movieUpdates),
                            cancellationToken
                        );
                    }

                    if (bookmarkUpdates.Count > 0)
                    {
                        totalBookmarkUpdated += await Task.Run(
                            () => SQLite.UpdateBookmarkKanaBatch(dbPath, bookmarkUpdates),
                            cancellationToken
                        );
                    }

                    await Dispatcher.InvokeAsync(
                        () => ApplyKanaBackfillToUi(movieUpdates, bookmarkUpdates),
                        DispatcherPriority.Background,
                        cancellationToken
                    );

                    await Task.Delay(KanaBackfillInterBatchDelayMs, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                DebugRuntimeLog.Write("kana", $"backfill canceled: db='{dbPath}'");
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "kana",
                    $"backfill failed: db='{dbPath}' err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
            finally
            {
                DebugRuntimeLog.TaskEnd(
                    nameof(RunKanaBackfillAsync),
                    $"db='{dbPath}' movie_updated={totalMovieUpdated} bookmark_updated={totalBookmarkUpdated}"
                );
            }
        }

        private static List<KanaBackfillUpdate> BuildReadingUpdates(
            IReadOnlyList<KanaBackfillTarget> targets
        )
        {
            List<KanaBackfillUpdate> updates = [];
            if (targets == null || targets.Count < 1)
            {
                return updates;
            }

            foreach (KanaBackfillTarget target in targets)
            {
                string kana = string.IsNullOrWhiteSpace(target.Kana)
                    ? JapaneseKanaProvider.GetKanaForPersistence(target.MovieName, target.MoviePath)
                    : JapaneseKanaProvider.GetKanaForPersistence(target.Kana);
                string roma = string.IsNullOrWhiteSpace(target.Roma)
                    ? JapaneseKanaProvider.GetRomaFromKanaForPersistence(kana)
                    : target.Roma;
                if (string.IsNullOrWhiteSpace(kana) && string.IsNullOrWhiteSpace(roma))
                {
                    continue;
                }

                updates.Add(new KanaBackfillUpdate(target.MovieId, kana ?? "", roma ?? ""));
            }

            return updates;
        }

        private void ApplyKanaBackfillToUi(
            IReadOnlyList<KanaBackfillUpdate> movieUpdates,
            IReadOnlyList<KanaBackfillUpdate> bookmarkUpdates
        )
        {
            Dictionary<long, KanaBackfillUpdate> movieReadingMap = movieUpdates?
                .GroupBy(x => x.MovieId)
                .ToDictionary(x => x.Key, x => x.Last()) ?? new Dictionary<long, KanaBackfillUpdate>();
            Dictionary<long, KanaBackfillUpdate> bookmarkReadingMap = bookmarkUpdates?
                .GroupBy(x => x.MovieId)
                .ToDictionary(x => x.Key, x => x.Last()) ?? new Dictionary<long, KanaBackfillUpdate>();
            List<WatchChangedMovie> changedMovies = [];
            HashSet<string> changedMoviePaths = new(StringComparer.OrdinalIgnoreCase);

            if (movieReadingMap.Count > 0)
            {
                foreach (MovieRecords item in MainVM.MovieRecs)
                {
                    if (movieReadingMap.TryGetValue(item.Movie_Id, out KanaBackfillUpdate update))
                    {
                        item.Kana = update.Kana;
                        item.Roma = update.Roma;
                        AddKanaBackfillChangedMovie(item, changedMovies, changedMoviePaths);
                    }
                }

                foreach (MovieRecords item in MainVM.FilteredMovieRecs)
                {
                    if (movieReadingMap.TryGetValue(item.Movie_Id, out KanaBackfillUpdate update))
                    {
                        item.Kana = update.Kana;
                        item.Roma = update.Roma;
                        AddKanaBackfillChangedMovie(item, changedMovies, changedMoviePaths);
                    }
                }
            }

            if (bookmarkReadingMap.Count > 0)
            {
                foreach (MovieRecords item in MainVM.BookmarkRecs)
                {
                    if (
                        bookmarkReadingMap.TryGetValue(
                            item.Movie_Id,
                            out KanaBackfillUpdate update
                        )
                    )
                    {
                        item.Kana = update.Kana;
                        item.Roma = update.Roma;
                    }
                }
            }

            QueueKanaBackfillMovieViewRefresh(movieReadingMap.Count, changedMovies);
        }

        private void QueueKanaBackfillMovieViewRefresh(
            int updatedMovieCount,
            IReadOnlyList<WatchChangedMovie> changedMovies
        )
        {
            if (updatedMovieCount < 1)
            {
                return;
            }

            string sortId = MainVM?.DbInfo?.Sort ?? "";
            string searchKeyword = MainVM?.DbInfo?.SearchKeyword ?? "";
            bool affectsCurrentView =
                sortId is "10" or "11" || !string.IsNullOrWhiteSpace(searchKeyword);
            if (!affectsCurrentView)
            {
                return;
            }

            UiWorkRequest request =
                UiWorkRequestPolicy.CreateKanaBackfillMovieViewRefreshRequest();
            if (!TryAdmitKanaBackfillMovieViewRefresh(request, out UiWorkRequest admittedRequest))
            {
                return;
            }

            bool hasChangedMovies = changedMovies != null && changedMovies.Count > 0;
            if (changedMovies == null || changedMovies.Count < 1)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"kana backfill local refresh fallback: {UiWorkRequestPolicy.BuildRequestAdmissionLogFields(admittedRequest, UiWorkRequestPolicy.ReleaseReasonDeferred)} reason=missing-path updated={updatedMovieCount}"
                );
            }

            // かな補完は DB 反映済みの値を UI モデルにも当てた後なので、
            // DB再読込ではなく現在 snapshot の再検索・再整列だけを予約する。
            _ = RefreshMovieViewFromCurrentSourceAsync(
                sortId,
                hasChangedMovies ? "kana-backfill" : "kana-backfill-query",
                UiHangActivityKind.Database,
                hasChangedMovies ? changedMovies : null
            );
        }

        private bool TryAdmitKanaBackfillMovieViewRefresh(
            UiWorkRequest request,
            out UiWorkRequest admittedRequest
        )
        {
            admittedRequest = default;
            UiWorkSchedulerRuntimeQueueResult queueResult;
            UiWorkSchedulerRuntimeTakeResult takeResult = default;

            // runtimeは実行器にせず、既存のReadModel refresh入口へ渡す1件を選ぶだけに留める。
            lock (_uiWorkSchedulerRuntimeSyncRoot)
            {
                queueResult = _uiWorkSchedulerRuntime.Queue(request);
                if (queueResult.Decision.Accepted)
                {
                    takeResult = _uiWorkSchedulerRuntime.TryTakeNext();
                }
            }

            if (!queueResult.Decision.Accepted)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"kana backfill scheduler rejected: {UiWorkSchedulerPolicy.BuildAdmissionLogFields(request, queueResult.Decision)} pending_count={queueResult.PendingCount}"
                );
                return false;
            }

            if (!takeResult.HasRequest)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"kana backfill scheduler empty: {UiWorkRequestPolicy.BuildRequestAdmissionLogFields(request, UiWorkRequestPolicy.ReleaseReasonRejected)} next_reason={takeResult.Decision.Reason} pending_count={takeResult.PendingCount}"
                );
                return false;
            }

            DebugRuntimeLog.Write(
                "ui-tempo",
                $"kana backfill scheduler admitted: {UiWorkSchedulerPolicy.BuildAdmissionLogFields(request, queueResult.Decision)} pending_count={queueResult.PendingCount}"
            );
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"kana backfill scheduler released: {UiWorkSchedulerPolicy.BuildTakeLogFields(takeResult.PendingRequest, takeResult.Decision, takeResult.PendingCount, UiWorkRequestPolicy.ReleaseReasonReleased)}"
            );

            admittedRequest = takeResult.PendingRequest.Request;
            return true;
        }

        private static void AddKanaBackfillChangedMovie(
            MovieRecords item,
            List<WatchChangedMovie> changedMovies,
            HashSet<string> changedMoviePaths
        )
        {
            if (
                item == null
                || string.IsNullOrWhiteSpace(item.Movie_Path)
                || changedMovies == null
                || changedMoviePaths == null
                || !changedMoviePaths.Add(item.Movie_Path)
            )
            {
                return;
            }

            changedMovies.Add(
                new WatchChangedMovie(
                    item.Movie_Path,
                    WatchMovieChangeKind.None,
                    WatchMovieDirtyFields.Kana
                )
            );
        }
    }
}
