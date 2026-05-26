using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using IndigoMovieManager.BottomTabs.ThumbnailError;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int ThumbnailErrorUiIntervalMs = 1000;

        private DispatcherTimer _thumbnailErrorUiTimer;
        private ThumbnailErrorTabPresenter _thumbnailErrorTabPresenter;
        private int _thumbnailErrorRefreshQueued;
        private int _thumbnailErrorRefreshRequested;
        private int _thumbnailErrorUiDirtyWhileHidden;
        private int _thumbnailErrorPendingRescueWorkCached;
        private int _thumbnailErrorPendingRescueWorkRefreshRunning;
        private int _thumbnailErrorPendingRescueWorkRefreshRequested;
        private string _thumbnailErrorPendingRescueWorkCachedDbFullPath = "";
        private IReadOnlyList<string> _thumbnailErrorPreferredViewportKeysSnapshot =
            Array.Empty<string>();
        private DateTime _thumbnailErrorViewportPriorityLastUtc = DateTime.MinValue;

        // サムネ失敗タブは、前面の間だけ軽いポーリングで進行状況を追う。
        private void InitializeThumbnailErrorUiSupport()
        {
            if (!HasThumbnailErrorBottomTabHost())
            {
                UpdateThumbnailErrorTabVisibilityState();
                UpdateThumbnailErrorUiTimerState();
                return;
            }

            InitializeThumbnailErrorTabVisibilityMonitoring();
            InitializeThumbnailErrorUiTimer();
        }

        private void InitializeThumbnailErrorUiTimer()
        {
            if (_thumbnailErrorUiTimer != null)
            {
                return;
            }

            _thumbnailErrorUiTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ThumbnailErrorUiIntervalMs),
            };
            _thumbnailErrorUiTimer.Tick += ThumbnailErrorUiTimer_Tick;
            if (_thumbnailErrorTabPresenter == null && ThumbnailErrorBottomTab != null)
            {
                _thumbnailErrorTabPresenter = new ThumbnailErrorTabPresenter(
                    HasThumbnailErrorBottomTabHost,
                    ThumbnailErrorBottomTab,
                    _thumbnailErrorUiTimer,
                    RequestThumbnailErrorSnapshotRefresh,
                    UpdateThumbnailErrorUiTimerState,
                    ClearThumbnailErrorInactiveUiState
                );
            }
            UpdateThumbnailErrorUiTimerState();
        }

        private void InitializeThumbnailErrorTabVisibilityMonitoring()
        {
            if (!HasThumbnailErrorBottomTabHost())
            {
                _thumbnailErrorTabPresenter?.UpdateActiveState();
                UpdateThumbnailErrorUiTimerState();
                return;
            }

            if (_thumbnailErrorTabPresenter != null)
            {
                _thumbnailErrorTabPresenter.InitializeMonitoring();
            }
        }

        private void UpdateThumbnailErrorTabVisibilityState()
        {
            _thumbnailErrorTabPresenter?.UpdateActiveState();
        }

        private bool IsThumbnailErrorTabActiveCached()
        {
            return _thumbnailErrorTabPresenter?.IsActiveCached() == true;
        }

        // 既存 partial からの呼び出し名は残し、意味だけ「アクティブ時」に寄せる。
        private bool IsThumbnailErrorTabVisibleOrSelectedCached()
        {
            return IsThumbnailErrorTabActiveCached();
        }

        private void UpdateThumbnailErrorUiTimerState()
        {
            if (_thumbnailErrorUiTimer == null)
            {
                return;
            }

            if (HasThumbnailErrorBottomTabHost() && IsThumbnailErrorTabActiveCached())
            {
                if (!_thumbnailErrorUiTimer.IsEnabled)
                {
                    TryStartDispatcherTimer(
                        _thumbnailErrorUiTimer,
                        nameof(_thumbnailErrorUiTimer)
                    );
                }

                return;
            }

            if (_thumbnailErrorUiTimer.IsEnabled)
            {
                StopDispatcherTimerSafely(
                    _thumbnailErrorUiTimer,
                    nameof(_thumbnailErrorUiTimer)
                );
            }
        }

        // 他スレッドからの更新要求はここへ束ね、UI再構築の連打を避ける。
        private void RequestThumbnailErrorSnapshotRefresh()
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            bool shouldRefreshSortCountsOnly = ShouldRefreshThumbnailErrorSortCountsWithoutBottomTab();
            if (!HasThumbnailErrorBottomTabHost())
            {
                if (!shouldRefreshSortCountsOnly)
                {
                    return;
                }

                // 下側ERROR UIが無い構成でも、Sort=28中は件数だけ軽い集計へ通す。
                Interlocked.Exchange(ref _thumbnailErrorRecordsDirty, 1);
                RefreshThumbnailErrorRecords();
                return;
            }

            Interlocked.Exchange(ref _thumbnailErrorRefreshRequested, 1);
            Interlocked.Exchange(ref _thumbnailErrorRecordsDirty, 1);
            if (!IsThumbnailErrorTabActiveCached())
            {
                Interlocked.Exchange(ref _thumbnailErrorUiDirtyWhileHidden, 1);
                return;
            }

            QueueThumbnailErrorSnapshotRefresh();
        }

        private void QueueThumbnailErrorSnapshotRefresh()
        {
            if (!HasThumbnailErrorBottomTabHost())
            {
                return;
            }

            if (Interlocked.Exchange(ref _thumbnailErrorRefreshQueued, 1) == 1)
            {
                return;
            }

            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(ProcessThumbnailErrorSnapshotRefreshQueue)
            );
        }

        private void ProcessThumbnailErrorSnapshotRefreshQueue()
        {
            try
            {
                if (Interlocked.Exchange(ref _thumbnailErrorRefreshRequested, 0) != 1)
                {
                    return;
                }

                if (!HasThumbnailErrorBottomTabHost() || !IsThumbnailErrorTabActiveCached())
                {
                    Interlocked.Exchange(ref _thumbnailErrorUiDirtyWhileHidden, 1);
                    return;
                }

                RefreshThumbnailErrorRecords();
                Interlocked.Exchange(ref _thumbnailErrorUiDirtyWhileHidden, 0);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "thumbnail-error-tab",
                    $"error tab refresh failed: {ex.Message}"
                );
            }
            finally
            {
                Interlocked.Exchange(ref _thumbnailErrorRefreshQueued, 0);
                if (Interlocked.CompareExchange(ref _thumbnailErrorRefreshRequested, 0, 0) == 1)
                {
                    QueueThumbnailErrorSnapshotRefresh();
                }
            }
        }

        // 見えている間だけ 1 秒周期で再読込し、待機中→救済中→反映待ちを追えるようにする。
        private void ThumbnailErrorUiTimer_Tick(object sender, EventArgs e)
        {
            _thumbnailErrorTabPresenter?.HandleTimerTick(
                onInactive: () =>
                {
                    UpdateThumbnailErrorUiTimerState();
                    Interlocked.Exchange(ref _thumbnailErrorUiDirtyWhileHidden, 1);
                },
                onPoll: () =>
                {
                    TryPromoteVisibleThumbnailErrorRecords();

                    if (!ShouldPollThumbnailErrorProgress())
                    {
                        return;
                    }

                    RequestThumbnailErrorSnapshotRefresh();
                }
            );
        }

        private bool ShouldPollThumbnailErrorProgress()
        {
            if (!HasThumbnailErrorBottomTabHost())
            {
                return false;
            }

            if (
                MainVM?.ThumbnailErrorRecs?.Any(x => x != null && x.ProgressSummaryKey != "unqueued")
                == true
            )
            {
                return true;
            }

            string dbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            RequestThumbnailErrorPendingRescueWorkRefresh(dbFullPath);
            return HasCachedThumbnailErrorPendingRescueWork(dbFullPath);
        }

        // UI tick では前回の軽量 cache だけを見て、FailureDb の実読込は背景へ逃がす。
        private void RequestThumbnailErrorPendingRescueWorkRefresh(string dbFullPath)
        {
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                ClearThumbnailErrorPendingRescueWorkCache();
                return;
            }

            if (
                Interlocked.CompareExchange(
                    ref _thumbnailErrorPendingRescueWorkRefreshRunning,
                    1,
                    0
                )
                != 0
            )
            {
                Interlocked.Exchange(ref _thumbnailErrorPendingRescueWorkRefreshRequested, 1);
                return;
            }

            Interlocked.Exchange(ref _thumbnailErrorPendingRescueWorkRefreshRequested, 0);
            _ = RunThumbnailErrorPendingRescueWorkRefreshAsync(dbFullPath);
        }

        private async Task RunThumbnailErrorPendingRescueWorkRefreshAsync(string dbFullPath)
        {
            try
            {
                string currentDbFullPath = dbFullPath ?? "";
                if (string.IsNullOrWhiteSpace(currentDbFullPath))
                {
                    return;
                }

                bool hasPendingRescueWork = await Task
                    .Run(() => LoadThumbnailErrorPendingRescueWorkCore(currentDbFullPath))
                    .ConfigureAwait(false);

                if (
                    Dispatcher == null
                    || Dispatcher.HasShutdownStarted
                    || Dispatcher.HasShutdownFinished
                )
                {
                    return;
                }

                await Dispatcher
                    .InvokeAsync(
                        () =>
                            ApplyThumbnailErrorPendingRescueWorkResult(
                                currentDbFullPath,
                                hasPendingRescueWork
                            ),
                        DispatcherPriority.Background
                    )
                    .Task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "thumbnail-error-tab",
                    $"pending rescue cache refresh failed: {ex.Message}"
                );
            }
            finally
            {
                Interlocked.Exchange(ref _thumbnailErrorPendingRescueWorkRefreshRunning, 0);
                if (
                    Interlocked.CompareExchange(
                        ref _thumbnailErrorPendingRescueWorkRefreshRequested,
                        0,
                        0
                    )
                    == 1
                    && Dispatcher != null
                    && !Dispatcher.HasShutdownStarted
                    && !Dispatcher.HasShutdownFinished
                )
                {
                    Interlocked.Exchange(ref _thumbnailErrorPendingRescueWorkRefreshRequested, 0);
                    _ = Dispatcher.BeginInvoke(
                        DispatcherPriority.Background,
                        new Action(
                            () =>
                                RequestThumbnailErrorPendingRescueWorkRefresh(
                                    MainVM?.DbInfo?.DBFullPath ?? ""
                                )
                        )
                    );
                }
            }
        }

        private static bool LoadThumbnailErrorPendingRescueWorkCore(string dbFullPath)
        {
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return false;
            }

            ThumbnailFailureDbService failureDbService = new(dbFullPath);
            return failureDbService.HasPendingRescueWork(DateTime.UtcNow);
        }

        private void ApplyThumbnailErrorPendingRescueWorkResult(
            string dbFullPath,
            bool hasPendingRescueWork
        )
        {
            if (
                Dispatcher.HasShutdownStarted
                || Dispatcher.HasShutdownFinished
                || !AreSameMainDbPath(dbFullPath, MainVM?.DbInfo?.DBFullPath ?? "")
            )
            {
                return;
            }

            _thumbnailErrorPendingRescueWorkCachedDbFullPath = dbFullPath ?? "";
            Interlocked.Exchange(
                ref _thumbnailErrorPendingRescueWorkCached,
                hasPendingRescueWork ? 1 : 0
            );
            if (hasPendingRescueWork && IsThumbnailErrorTabActiveCached())
            {
                // pending を検出した時だけ、次の表示 snapshot を予約して一覧の残像を短くする。
                RequestThumbnailErrorSnapshotRefresh();
            }
        }

        private bool HasCachedThumbnailErrorPendingRescueWork(string dbFullPath)
        {
            return Volatile.Read(ref _thumbnailErrorPendingRescueWorkCached) == 1
                && AreSameMainDbPath(_thumbnailErrorPendingRescueWorkCachedDbFullPath, dbFullPath);
        }

        private void ClearThumbnailErrorPendingRescueWorkCache()
        {
            _thumbnailErrorPendingRescueWorkCachedDbFullPath = "";
            Interlocked.Exchange(ref _thumbnailErrorPendingRescueWorkCached, 0);
        }

        private void ClearThumbnailErrorInactiveUiState()
        {
            _thumbnailErrorPreferredViewportKeysSnapshot = Array.Empty<string>();
            _thumbnailErrorViewportPriorityLastUtc = DateTime.MinValue;
        }
    }
}
