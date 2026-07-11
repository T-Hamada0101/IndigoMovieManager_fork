using System.Collections.Generic;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IndigoMovieManager.UpperTabs.Common;
using IndigoMovieManager.UpperTabs.Player;
using IndigoMovieManager.Thumbnail.QueueDb;
using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // viewport 更新本体へ渡す最小コンテキストだけを束ね、引数の散らばりを防ぐ。
        private readonly record struct UpperTabViewportRefreshContext(
            int CurrentTabIndex,
            UpperTabVisibleRange NextRange
        );

        // Reset 前に保持する UI 専用情報を、現在タブと純粋 policy の anchor だけへ絞る。
        private readonly record struct MovieViewScrollAnchorContext(
            int TabIndex,
            MovieViewScrollAnchor Anchor
        );

        private const int UpperTabViewportOverscanItemCount = 24;
        private const int UpperTabViewportThrottleMs = 33;
        private const int UpperTabFollowupScrollRefreshSuppressMs = 120;
        private const int UpperTabStartupAppendSuppressAfterPageScrollMs = 200;
        private const int UiOperationRecentViewportInteractionWindowMs = 250;
        private const int PlayerThumbnailScrollUserPriorityWindowMs = 250;

        private readonly HashSet<ScrollViewer> _upperTabViewportAttachedScrollViewers = [];
        private readonly Dictionary<ItemsControl, Panel> _upperTabViewportItemsHostPanels = [];
        private readonly Dictionary<ItemsControl, ScrollViewer> _upperTabViewportScrollViewers = [];
        private DispatcherTimer _upperTabStartupAppendRetryTimer;
        private DispatcherTimer _upperTabViewportRefreshTimer;
        private DispatcherTimer _playerThumbnailScrollUserPriorityTimer;
        private DispatcherOperation _playerThumbnailScrollRenderMeasureOperation;
        private DispatcherOperation _playerRightRailWarmRefreshOperation;
        private UpperTabVisibleRange _activeUpperTabVisibleRange = UpperTabVisibleRange.Empty;
        private IReadOnlyList<string> _preferredVisibleMoviePathKeysSnapshot = Array.Empty<string>();
        private bool _isUpperTabPreferredMoviePathKeysSnapshotPublished;
        private int _preferredVisibleMoviePathKeysTabIndex = -1;
        private int _upperTabViewportSourceRevision;
        private int _preferredVisibleMoviePathKeysSourceRevision;
        private long _upperTabFollowupScrollRefreshSuppressUntilUtcTicks;
        private long _upperTabStartupAppendSuppressUntilUtcTicks;
        private long _recentViewportInteractionUntilUtcTicks;
        private bool _isPlayerThumbnailScrollUserPriorityActive;
        private long _playerThumbnailScrollStartedTimestamp;
        private long _playerThumbnailScrollFirstRenderElapsedMilliseconds = -1;
        private int _playerThumbnailScrollInputCount;
        private long _playerThumbnailScrollBurstSessionSequence;
        private long _playerThumbnailScrollBurstSessionId;
        private int _playerThumbnailScrollGeneratorStatusChangedCount;
        private int _playerThumbnailScrollLayoutUpdatedCount;
        private int _playerThumbnailScrollRenderingCount;
        private long _playerThumbnailScrollFirstLayoutMilliseconds = -1;
        private long _playerThumbnailScrollFirstRenderingMilliseconds = -1;
        private long _playerThumbnailScrollLastLayoutTimestamp;
        private long _playerThumbnailScrollLastRenderingTimestamp;
        private long _playerThumbnailScrollMaxLayoutGapMilliseconds;
        private long _playerThumbnailScrollMaxRenderingGapMilliseconds;
        private int _playerThumbnailScrollRealizedCountBefore;
        private int _playerThumbnailScrollRevisionBefore;
        private EventHandler _playerThumbnailScrollGeneratorStatusChangedHandler;
        private EventHandler _playerThumbnailScrollLayoutUpdatedHandler;
        private EventHandler _playerThumbnailScrollRenderingHandler;
        private bool _playerRightRailWarmCompletionHooked;
        private bool _playerRightRailViewportRevisionPending;
        private readonly HashSet<string> _playerRightRailWarmCompletedMoviePathKeys = new(
            StringComparer.OrdinalIgnoreCase
        );

        public static readonly DependencyProperty UpperTabPreferredMoviePathKeysRevisionProperty =
            DependencyProperty.Register(
                nameof(UpperTabPreferredMoviePathKeysRevision),
                typeof(int),
                typeof(MainWindow),
                new PropertyMetadata(0)
            );

        public int UpperTabPreferredMoviePathKeysRevision
        {
            get => (int)GetValue(UpperTabPreferredMoviePathKeysRevisionProperty);
            private set => SetValue(UpperTabPreferredMoviePathKeysRevisionProperty, value);
        }

        public static readonly DependencyProperty PlayerRightRailImageRevisionProperty =
            DependencyProperty.Register(
                nameof(PlayerRightRailImageRevision),
                typeof(int),
                typeof(MainWindow),
                new PropertyMetadata(0)
            );

        public int PlayerRightRailImageRevision
        {
            get => (int)GetValue(PlayerRightRailImageRevisionProperty);
            private set => SetValue(PlayerRightRailImageRevisionProperty, value);
        }

        // 上側タブの visible 範囲追跡を初期化する。
        private void InitializeUpperTabViewportSupport()
        {
            if (_upperTabViewportRefreshTimer != null)
            {
                return;
            }

            _upperTabViewportRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(UpperTabViewportThrottleMs),
            };
            _upperTabViewportRefreshTimer.Tick += UpperTabViewportRefreshTimer_Tick;
            _upperTabStartupAppendRetryTimer = new DispatcherTimer();
            _upperTabStartupAppendRetryTimer.Tick += UpperTabStartupAppendRetryTimer_Tick;
            _playerThumbnailScrollUserPriorityTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(PlayerThumbnailScrollUserPriorityWindowMs),
            };
            _playerThumbnailScrollUserPriorityTimer.Tick +=
                PlayerThumbnailScrollUserPriorityTimer_Tick;
            PlayerRightRailImageSourceConverter.ImageWarmCompleted +=
                PlayerRightRailImageSourceConverter_ImageWarmCompleted;
            _playerRightRailWarmCompletionHooked = true;
            Dispatcher.ShutdownStarted += PlayerThumbnailScrollDispatcher_ShutdownStarted;

            Loaded += (_, _) =>
            {
                EnsureUpperTabViewportHandlersAttached();
                RequestUpperTabVisibleRangeRefresh(immediate: true, reason: "loaded");
            };
        }

        // スクロールやタブ切替の後で、active tab の visible 範囲を取り直す。
        private void RequestUpperTabVisibleRangeRefresh(bool immediate = false, string reason = "")
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.InvokeAsync(() => RequestUpperTabVisibleRangeRefresh(immediate, reason));
                return;
            }

            EnsureUpperTabViewportHandlersAttached();
            MarkRecentViewportInteraction(reason);
            if (immediate)
            {
                StopDispatcherTimerSafely(
                    _upperTabViewportRefreshTimer,
                    nameof(_upperTabViewportRefreshTimer)
                );
                ApplyUpperTabVisibleRangeRefresh(reason);
                return;
            }

            StopDispatcherTimerSafely(
                _upperTabViewportRefreshTimer,
                nameof(_upperTabViewportRefreshTimer)
            );
            TryStartDispatcherTimer(
                _upperTabViewportRefreshTimer,
                nameof(_upperTabViewportRefreshTimer)
            );
        }

        // PageUp / PageDown 直後の ScrollChanged は同じ refresh を積みやすいので少しだけ無視する。
        private void SuppressUpperTabFollowupScrollRefreshBriefly()
        {
            _upperTabFollowupScrollRefreshSuppressUntilUtcTicks =
                DateTime.UtcNow.AddMilliseconds(UpperTabFollowupScrollRefreshSuppressMs).Ticks;
        }

        // ページ送り直後だけ startup append を寝かせ、スクロール直後の重い仕事を後ろへ逃がす。
        private void SuppressStartupAppendAfterPageScrollBriefly()
        {
            _upperTabStartupAppendSuppressUntilUtcTicks =
                DateTime.UtcNow.AddMilliseconds(UpperTabStartupAppendSuppressAfterPageScrollMs).Ticks;
        }

        private void MarkRecentViewportInteraction(string reason)
        {
            if (!ShouldMarkRecentViewportInteraction(reason))
            {
                return;
            }

            Volatile.Write(
                ref _recentViewportInteractionUntilUtcTicks,
                DateTime.UtcNow.AddMilliseconds(UiOperationRecentViewportInteractionWindowMs).Ticks
            );
        }

        private bool IsRecentViewportInteractionActive()
        {
            return IsRecentViewportInteractionActive(
                DateTime.UtcNow.Ticks,
                Volatile.Read(ref _recentViewportInteractionUntilUtcTicks)
            );
        }

        internal static bool ShouldMarkRecentViewportInteraction(string reason)
        {
            return string.Equals(reason, "scroll", StringComparison.Ordinal)
                || string.Equals(reason, "page-up", StringComparison.Ordinal)
                || string.Equals(reason, "page-down", StringComparison.Ordinal);
        }

        internal static bool IsRecentViewportInteractionActive(
            long nowUtcTicks,
            long activeUntilUtcTicks
        )
        {
            return activeUntilUtcTicks > 0 && nowUtcTicks <= activeUntilUtcTicks;
        }

        internal static bool ShouldSuppressUpperTabFollowupScrollRefresh(
            long nowUtcTicks,
            long suppressUntilUtcTicks
        )
        {
            return suppressUntilUtcTicks > 0 && nowUtcTicks <= suppressUntilUtcTicks;
        }

        internal static bool TryGetStartupAppendRetryDelayMs(
            long nowUtcTicks,
            long suppressUntilUtcTicks,
            out int retryDelayMs
        )
        {
            if (suppressUntilUtcTicks <= 0 || nowUtcTicks > suppressUntilUtcTicks)
            {
                retryDelayMs = 0;
                return false;
            }

            long remainingTicks = suppressUntilUtcTicks - nowUtcTicks;
            retryDelayMs = Math.Max(
                1,
                (int)Math.Ceiling(TimeSpan.FromTicks(remainingTicks).TotalMilliseconds)
            );
            return true;
        }

        internal static bool ShouldPublishPreferredMoviePathKeysSnapshot(
            UpperTabVisibleRange visibleRange
        )
        {
            return visibleRange.HasVisibleItems;
        }

        internal static bool ShouldPreservePreferredMoviePathKeysOnUnavailableViewport(
            bool hasPublishedSnapshot,
            int currentTabIndex,
            int snapshotTabIndex,
            int viewportSourceRevision,
            int snapshotSourceRevision
        )
        {
            return hasPublishedSnapshot
                && currentTabIndex >= 0
                && currentTabIndex == snapshotTabIndex
                && viewportSourceRevision == snapshotSourceRevision;
        }

        private void ClearUpperTabVisibleRange()
        {
            _activeUpperTabVisibleRange = UpperTabVisibleRange.Empty;
            _preferredVisibleMoviePathKeysSnapshot = Array.Empty<string>();
            _preferredVisibleMoviePathKeysTabIndex = -1;
            _preferredVisibleMoviePathKeysSourceRevision = _upperTabViewportSourceRevision;
            UpperTabActivationGate.ClearPreferredMoviePathKeys();
            if (_isUpperTabPreferredMoviePathKeysSnapshotPublished)
            {
                _isUpperTabPreferredMoviePathKeysSnapshotPublished = false;
                RefreshActiveUpperTabImageRevision();
            }

            _activeUpperTabVisibleErrorMoviePathKeysSnapshot = Array.Empty<string>();
            _thumbnailVisibleErrorRescueRequestVersion++;
        }

        // 一覧ソース更新時だけ revision を進め、range 不変時の snapshot 再構築を避ける。
        private void NotifyUpperTabViewportSourceChanged()
        {
            Interlocked.Increment(ref _upperTabViewportSourceRevision);
        }

        // 背景スレッドからは UI スレッドで作った snapshot を返し、クロススレッド参照を避ける。
        private IReadOnlyList<string> ResolvePreferredVisibleMoviePathKeys()
        {
            if (!Dispatcher.CheckAccess())
            {
                return _preferredVisibleMoviePathKeysSnapshot;
            }

            return BuildPreferredVisibleMoviePathKeysSnapshot(_activeUpperTabVisibleRange);
        }

        // active tab の visible -> near-visible 順で、優先リース用の MoviePathKey snapshot を組む。
        private IReadOnlyList<string> BuildPreferredVisibleMoviePathKeysSnapshot(
            UpperTabVisibleRange visibleRange
        )
        {
            if (!TryGetCurrentUpperTabContext(out int currentTabIndex, out bool isStandardUpperTab))
            {
                return Array.Empty<string>();
            }

            if (currentTabIndex == ThumbnailErrorTabIndex)
            {
                return BuildPreferredRescueMoviePathKeysSnapshot(
                    GetDisplayedUpperTabRescueItems().Select(item => item?.MoviePath ?? "")
                );
            }

            if (
                !isStandardUpperTab
                || !visibleRange.HasVisibleItems
                || MainVM?.FilteredMovieRecs == null
            )
            {
                return Array.Empty<string>();
            }

            List<string> result = [];
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            int totalCount = MainVM.FilteredMovieRecs.Count;
            if (totalCount < 1)
            {
                return result;
            }

            AppendMoviePathKeysInRange(
                result,
                seen,
                visibleRange.FirstVisibleIndex,
                visibleRange.LastVisibleIndex,
                totalCount
            );
            AppendMoviePathKeysInRange(
                result,
                seen,
                visibleRange.FirstNearVisibleIndex,
                visibleRange.FirstVisibleIndex - 1,
                totalCount
            );
            AppendMoviePathKeysInRange(
                result,
                seen,
                visibleRange.LastVisibleIndex + 1,
                visibleRange.LastNearVisibleIndex,
                totalCount
            );
            return result;
        }

        // 救済タブでは表示中の行をそのまま優先キーへ落とし、通常再試行を先頭へ寄せる。
        internal static IReadOnlyList<string> BuildPreferredRescueMoviePathKeysSnapshot(
            IEnumerable<string> moviePaths
        )
        {
            if (moviePaths == null)
            {
                return Array.Empty<string>();
            }

            List<string> result = [];
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (string moviePath in moviePaths)
            {
                if (string.IsNullOrWhiteSpace(moviePath))
                {
                    continue;
                }

                string moviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath);
                if (string.IsNullOrWhiteSpace(moviePathKey) || !seen.Add(moviePathKey))
                {
                    continue;
                }

                result.Add(moviePathKey);
            }

            return result;
        }

        private void UpperTabViewportRefreshTimer_Tick(object sender, EventArgs e)
        {
            StopDispatcherTimerSafely(
                _upperTabViewportRefreshTimer,
                nameof(_upperTabViewportRefreshTimer)
            );
            ApplyUpperTabVisibleRangeRefresh("throttled");
        }

        private void UpperTabStartupAppendRetryTimer_Tick(object sender, EventArgs e)
        {
            StopDispatcherTimerSafely(
                _upperTabStartupAppendRetryTimer,
                nameof(_upperTabStartupAppendRetryTimer)
            );
            DebugRuntimeLog.Write("ui-tempo", "startup append retry fired");
            RequestUpperTabVisibleRangeRefresh(reason: "startup-append-retry");
        }

        private void ScheduleStartupAppendRetry(int retryDelayMs)
        {
            if (_upperTabStartupAppendRetryTimer == null)
            {
                return;
            }

            StopDispatcherTimerSafely(
                _upperTabStartupAppendRetryTimer,
                nameof(_upperTabStartupAppendRetryTimer)
            );
            _upperTabStartupAppendRetryTimer.Interval = TimeSpan.FromMilliseconds(
                Math.Max(1, retryDelayMs)
            );
            TryStartDispatcherTimer(
                _upperTabStartupAppendRetryTimer,
                nameof(_upperTabStartupAppendRetryTimer)
            );
        }

        private void EnsureUpperTabViewportHandlersAttached()
        {
            AttachUpperTabScrollViewer(SmallList);
            AttachUpperTabScrollViewer(BigList);
            AttachUpperTabScrollViewer(GridList);
            AttachUpperTabScrollViewer(PlayerThumbnailList);
            AttachUpperTabScrollViewer(ListDataGrid);
            AttachUpperTabScrollViewer(BigList10);
        }

        private void AttachUpperTabScrollViewer(ItemsControl itemsControl)
        {
            if (itemsControl == null)
            {
                return;
            }

            ScrollViewer scrollViewer = GetUpperTabViewportScrollViewer(itemsControl);
            if (scrollViewer == null || !_upperTabViewportAttachedScrollViewers.Add(scrollViewer))
            {
                return;
            }

            scrollViewer.ScrollChanged += UpperTabScrollViewer_ScrollChanged;
            if (ReferenceEquals(itemsControl, PlayerThumbnailList))
            {
                scrollViewer.PreviewMouseWheel += PlayerThumbnailScrollViewer_PreviewMouseWheel;
            }
        }

        // Playerのサムネ操作は描画前に優先区間へ入り、背後の新規仕事を先に譲らせる。
        private void PlayerThumbnailScrollViewer_PreviewMouseWheel(
            object sender,
            MouseWheelEventArgs e
        )
        {
            BeginOrExtendPlayerThumbnailScrollUserPriority("mouse-wheel");
        }

        private void BeginOrExtendPlayerThumbnailScrollUserPriority(string triggerReason)
        {
            if (TabPlayer?.IsSelected != true || _playerThumbnailScrollUserPriorityTimer == null)
            {
                return;
            }

            if (!_isPlayerThumbnailScrollUserPriorityActive)
            {
                _isPlayerThumbnailScrollUserPriorityActive = true;
                _playerThumbnailScrollStartedTimestamp = Stopwatch.GetTimestamp();
                _playerThumbnailScrollFirstRenderElapsedMilliseconds = -1;
                _playerThumbnailScrollInputCount = 0;
                BeginUserPriorityWork("player-thumbnail-scroll");
                BeginPlayerThumbnailScrollBurstMetrics();
                DebugRuntimeLog.Write(
                    "ui-priority",
                    $"player thumbnail scroll priority begin: trigger_reason={triggerReason}"
                );
                QueuePlayerThumbnailScrollRenderMeasure();
            }

            _playerThumbnailScrollInputCount++;

            StopDispatcherTimerSafely(
                _playerThumbnailScrollUserPriorityTimer,
                nameof(_playerThumbnailScrollUserPriorityTimer)
            );
            TryStartDispatcherTimer(
                _playerThumbnailScrollUserPriorityTimer,
                nameof(_playerThumbnailScrollUserPriorityTimer)
            );
        }

        private void PlayerThumbnailScrollUserPriorityTimer_Tick(object sender, EventArgs e)
        {
            ReleasePlayerThumbnailScrollUserPriority("idle");
        }

        private void PlayerThumbnailScrollDispatcher_ShutdownStarted(object sender, EventArgs e)
        {
            ReleasePlayerThumbnailScrollUserPriority("shutdown");
            _playerThumbnailScrollRenderMeasureOperation?.Abort();
            _playerThumbnailScrollRenderMeasureOperation = null;
            if (_playerRightRailWarmCompletionHooked)
            {
                PlayerRightRailImageSourceConverter.ImageWarmCompleted -=
                    PlayerRightRailImageSourceConverter_ImageWarmCompleted;
                _playerRightRailWarmCompletionHooked = false;
            }

            _playerRightRailWarmCompletedMoviePathKeys.Clear();
            _playerRightRailViewportRevisionPending = false;
            _playerRightRailWarmRefreshOperation?.Abort();
            _playerRightRailWarmRefreshOperation = null;
        }

        private void ReleasePlayerThumbnailScrollUserPriority(string releaseReason)
        {
            StopDispatcherTimerSafely(
                _playerThumbnailScrollUserPriorityTimer,
                nameof(_playerThumbnailScrollUserPriorityTimer)
            );
            if (!_isPlayerThumbnailScrollUserPriorityActive)
            {
                DetachPlayerThumbnailScrollBurstMetricsHandlers();
                return;
            }

            _isPlayerThumbnailScrollUserPriorityActive = false;
            EndUserPriorityWork("player-thumbnail-scroll");
            long totalElapsedMilliseconds = _playerThumbnailScrollStartedTimestamp > 0
                ? (long)Stopwatch.GetElapsedTime(_playerThumbnailScrollStartedTimestamp).TotalMilliseconds
                : 0;
            DebugRuntimeLog.Write(
                "ui-priority",
                $"player thumbnail scroll priority end: release_reason={releaseReason}"
            );
            EndPlayerThumbnailScrollBurstMetrics(releaseReason, totalElapsedMilliseconds);
            _playerThumbnailScrollStartedTimestamp = 0;
            _playerThumbnailScrollFirstRenderElapsedMilliseconds = -1;
            _playerThumbnailScrollInputCount = 0;
            if (string.Equals(releaseReason, "idle", StringComparison.Ordinal))
            {
                QueuePlayerRightRailWarmRefresh();
            }
        }

        // Playerスクロール中だけWPF描画イベントを観測し、通常時のUI経路へ負荷を残さない。
        private void BeginPlayerThumbnailScrollBurstMetrics()
        {
            DetachPlayerThumbnailScrollBurstMetricsHandlers();
            long sessionId = Interlocked.Increment(
                ref _playerThumbnailScrollBurstSessionSequence
            );
            _playerThumbnailScrollBurstSessionId = sessionId;
            _playerThumbnailScrollGeneratorStatusChangedCount = 0;
            _playerThumbnailScrollLayoutUpdatedCount = 0;
            _playerThumbnailScrollRenderingCount = 0;
            _playerThumbnailScrollFirstLayoutMilliseconds = -1;
            _playerThumbnailScrollFirstRenderingMilliseconds = -1;
            _playerThumbnailScrollLastLayoutTimestamp = 0;
            _playerThumbnailScrollLastRenderingTimestamp = 0;
            _playerThumbnailScrollMaxLayoutGapMilliseconds = 0;
            _playerThumbnailScrollMaxRenderingGapMilliseconds = 0;
            _playerThumbnailScrollRealizedCountBefore = GetPlayerThumbnailRealizedCount();
            _playerThumbnailScrollRevisionBefore = PlayerRightRailImageRevision;
            PlayerRightRailImageBurstMetrics.Begin(sessionId);

            _playerThumbnailScrollGeneratorStatusChangedHandler = (_, _) =>
                RecordPlayerThumbnailScrollGeneratorStatusChanged(sessionId);
            _playerThumbnailScrollLayoutUpdatedHandler = (_, _) =>
                RecordPlayerThumbnailScrollLayoutUpdated(sessionId);
            _playerThumbnailScrollRenderingHandler = (_, _) =>
                RecordPlayerThumbnailScrollRendering(sessionId);
            PlayerThumbnailList.ItemContainerGenerator.StatusChanged +=
                _playerThumbnailScrollGeneratorStatusChangedHandler;
            PlayerThumbnailList.LayoutUpdated += _playerThumbnailScrollLayoutUpdatedHandler;
            CompositionTarget.Rendering += _playerThumbnailScrollRenderingHandler;
        }

        private void RecordPlayerThumbnailScrollGeneratorStatusChanged(long sessionId)
        {
            if (sessionId == _playerThumbnailScrollBurstSessionId)
            {
                _playerThumbnailScrollGeneratorStatusChangedCount++;
            }
        }

        private void RecordPlayerThumbnailScrollLayoutUpdated(long sessionId)
        {
            if (sessionId == _playerThumbnailScrollBurstSessionId)
            {
                _playerThumbnailScrollLayoutUpdatedCount++;
                RecordPlayerThumbnailScrollEventTiming(
                    ref _playerThumbnailScrollFirstLayoutMilliseconds,
                    ref _playerThumbnailScrollLastLayoutTimestamp,
                    ref _playerThumbnailScrollMaxLayoutGapMilliseconds
                );
            }
        }

        private void RecordPlayerThumbnailScrollRendering(long sessionId)
        {
            if (sessionId == _playerThumbnailScrollBurstSessionId)
            {
                _playerThumbnailScrollRenderingCount++;
                RecordPlayerThumbnailScrollEventTiming(
                    ref _playerThumbnailScrollFirstRenderingMilliseconds,
                    ref _playerThumbnailScrollLastRenderingTimestamp,
                    ref _playerThumbnailScrollMaxRenderingGapMilliseconds
                );
            }
        }

        private void RecordPlayerThumbnailScrollEventTiming(
            ref long firstElapsedMilliseconds,
            ref long lastTimestamp,
            ref long maxGapMilliseconds
        )
        {
            long now = Stopwatch.GetTimestamp();
            if (firstElapsedMilliseconds < 0 && _playerThumbnailScrollStartedTimestamp > 0)
            {
                firstElapsedMilliseconds = (long)Stopwatch
                    .GetElapsedTime(_playerThumbnailScrollStartedTimestamp, now)
                    .TotalMilliseconds;
            }

            if (lastTimestamp > 0)
            {
                long gapMilliseconds = (long)Stopwatch
                    .GetElapsedTime(lastTimestamp, now)
                    .TotalMilliseconds;
                maxGapMilliseconds = Math.Max(maxGapMilliseconds, gapMilliseconds);
            }

            lastTimestamp = now;
        }

        private void EndPlayerThumbnailScrollBurstMetrics(
            string releaseReason,
            long totalElapsedMilliseconds
        )
        {
            long sessionId = _playerThumbnailScrollBurstSessionId;
            int realizedCountAfter = GetPlayerThumbnailRealizedCount();
            int revisionAfter = PlayerRightRailImageRevision;
            DetachPlayerThumbnailScrollBurstMetricsHandlers();
            _playerThumbnailScrollBurstSessionId = 0;

            if (!PlayerRightRailImageBurstMetrics.End(sessionId, out var converterMetrics))
            {
                return;
            }

            DebugRuntimeLog.Write(
                "ui-tempo",
                $"player thumbnail scroll burst: release_reason={releaseReason} input_count={_playerThumbnailScrollInputCount} first_render_ms={_playerThumbnailScrollFirstRenderElapsedMilliseconds} first_layout_ms={_playerThumbnailScrollFirstLayoutMilliseconds} first_composition_ms={_playerThumbnailScrollFirstRenderingMilliseconds} max_layout_gap_ms={_playerThumbnailScrollMaxLayoutGapMilliseconds} max_composition_gap_ms={_playerThumbnailScrollMaxRenderingGapMilliseconds} total_ms={totalElapsedMilliseconds} converter_count={converterMetrics.ConvertCount} cache_hit_count={converterMetrics.CacheHitCount} cache_miss_count={converterMetrics.CacheMissCount} queue_enqueued_count={converterMetrics.QueueEnqueuedCount} queue_duplicate_count={converterMetrics.QueueDuplicateCount} generator_delta={_playerThumbnailScrollGeneratorStatusChangedCount} layout_delta={_playerThumbnailScrollLayoutUpdatedCount} render_delta={_playerThumbnailScrollRenderingCount} realized_delta={realizedCountAfter - _playerThumbnailScrollRealizedCountBefore} revision_delta={revisionAfter - _playerThumbnailScrollRevisionBefore} viewport_revision_pending={_playerRightRailViewportRevisionPending} revision_flush_state={(_playerRightRailViewportRevisionPending ? "pending-before-idle-flush" : "not-pending")}"
            );
        }

        private int GetPlayerThumbnailRealizedCount()
        {
            // 全itemは走査せず、仮想化items hostが現在持つ子数だけを読む。
            return GetUpperTabItemsHostPanel(PlayerThumbnailList)?.Children.Count ?? 0;
        }

        private void DetachPlayerThumbnailScrollBurstMetricsHandlers()
        {
            if (_playerThumbnailScrollGeneratorStatusChangedHandler != null)
            {
                PlayerThumbnailList.ItemContainerGenerator.StatusChanged -=
                    _playerThumbnailScrollGeneratorStatusChangedHandler;
                _playerThumbnailScrollGeneratorStatusChangedHandler = null;
            }

            if (_playerThumbnailScrollLayoutUpdatedHandler != null)
            {
                PlayerThumbnailList.LayoutUpdated -= _playerThumbnailScrollLayoutUpdatedHandler;
                _playerThumbnailScrollLayoutUpdatedHandler = null;
            }

            if (_playerThumbnailScrollRenderingHandler != null)
            {
                CompositionTarget.Rendering -= _playerThumbnailScrollRenderingHandler;
                _playerThumbnailScrollRenderingHandler = null;
            }
        }

        // 連続入力を1バーストへ束ね、最初に画面へ届いたRenderだけを計測する。
        private void QueuePlayerThumbnailScrollRenderMeasure()
        {
            if (
                Dispatcher == null
                || Dispatcher.HasShutdownStarted
                || Dispatcher.HasShutdownFinished
                || _playerThumbnailScrollRenderMeasureOperation != null
            )
            {
                return;
            }

            _playerThumbnailScrollRenderMeasureOperation = Dispatcher.InvokeAsync(
                () =>
                {
                    _playerThumbnailScrollRenderMeasureOperation = null;
                    if (
                        !_isPlayerThumbnailScrollUserPriorityActive
                        || _playerThumbnailScrollStartedTimestamp <= 0
                    )
                    {
                        return;
                    }

                    _playerThumbnailScrollFirstRenderElapsedMilliseconds = (long)Stopwatch
                        .GetElapsedTime(_playerThumbnailScrollStartedTimestamp)
                        .TotalMilliseconds;
                },
                DispatcherPriority.Render
            );
        }

        // 背景warm完了はUIへ戻し、現在のPlayer visible項目だけを再評価候補にする。
        private void PlayerRightRailImageSourceConverter_ImageWarmCompleted(
            object sender,
            ImageRequest request
        )
        {
            if (
                Dispatcher == null
                || Dispatcher.HasShutdownStarted
                || Dispatcher.HasShutdownFinished
            )
            {
                return;
            }

            _ = Dispatcher.InvokeAsync(
                () => HandlePlayerRightRailImageWarmCompleted(request),
                DispatcherPriority.Background
            );
        }

        private void HandlePlayerRightRailImageWarmCompleted(ImageRequest request)
        {
            if (
                TabPlayer?.IsSelected != true
                || request.ThumbnailRole != ImageRequestThumbnailRole.PlayerRightRail
                || !ContainsMoviePathKey(
                    _preferredVisibleMoviePathKeysSnapshot,
                    request.MoviePathKey
                )
            )
            {
                return;
            }

            _playerRightRailWarmCompletedMoviePathKeys.Add(request.MoviePathKey);
            if (!_isPlayerThumbnailScrollUserPriorityActive)
            {
                QueuePlayerRightRailWarmRefresh();
            }
        }

        private void QueuePlayerRightRailWarmRefresh()
        {
            if (
                (
                    _playerRightRailWarmCompletedMoviePathKeys.Count == 0
                    && !_playerRightRailViewportRevisionPending
                )
                || Dispatcher == null
                || Dispatcher.HasShutdownStarted
                || Dispatcher.HasShutdownFinished
                || (
                    _playerRightRailWarmRefreshOperation != null
                    && _playerRightRailWarmRefreshOperation.Status == DispatcherOperationStatus.Pending
                )
            )
            {
                return;
            }

            _playerRightRailWarmRefreshOperation = Dispatcher.InvokeAsync(
                ApplyPlayerRightRailWarmRefresh,
                DispatcherPriority.Background
            );
        }

        private void ApplyPlayerRightRailWarmRefresh()
        {
            _playerRightRailWarmRefreshOperation = null;
            if (_isPlayerThumbnailScrollUserPriorityActive)
            {
                return;
            }

            if (TabPlayer?.IsSelected != true)
            {
                _playerRightRailWarmCompletedMoviePathKeys.Clear();
                _playerRightRailViewportRevisionPending = false;
                return;
            }

            if (
                !_isUpperTabPreferredMoviePathKeysSnapshotPublished
                || _preferredVisibleMoviePathKeysSourceRevision
                    != _upperTabViewportSourceRevision
            )
            {
                _playerRightRailWarmCompletedMoviePathKeys.Clear();
                _playerRightRailViewportRevisionPending = false;
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    "player right rail warm refresh skipped: reason=stale viewport_revision_pending=False"
                );
                return;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            bool scrollPriorityActive = _isPlayerThumbnailScrollUserPriorityActive;
            int visibleCompletionCount = 0;
            foreach (string moviePathKey in _playerRightRailWarmCompletedMoviePathKeys)
            {
                if (ContainsMoviePathKey(_preferredVisibleMoviePathKeysSnapshot, moviePathKey))
                {
                    visibleCompletionCount++;
                }
            }

            _playerRightRailWarmCompletedMoviePathKeys.Clear();
            bool viewportRevisionPending = _playerRightRailViewportRevisionPending;
            _playerRightRailViewportRevisionPending = false;
            int playerRevisionBefore = PlayerRightRailImageRevision;
            if (visibleCompletionCount > 0 || viewportRevisionPending)
            {
                RefreshPlayerRightRailImageRevision();
            }

            bool playerRevisionUpdated = PlayerRightRailImageRevision != playerRevisionBefore;
            string revisionReason = viewportRevisionPending
                ? visibleCompletionCount > 0
                    ? "viewport-pending+visible-warm"
                    : "viewport-pending"
                : "visible-warm";
            stopwatch.Stop();
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"player right rail warm refresh: visible_completions={visibleCompletionCount} viewport_revision_pending={viewportRevisionPending} revision_reason={revisionReason} shared_revision_updated=False player_revision_updated={playerRevisionUpdated} elapsed_ms={stopwatch.ElapsedMilliseconds} scroll_priority_active={scrollPriorityActive}"
            );
        }

        internal static bool ContainsMoviePathKey(
            IReadOnlyList<string> moviePathKeys,
            string moviePathKey
        )
        {
            if (moviePathKeys == null || string.IsNullOrWhiteSpace(moviePathKey))
            {
                return false;
            }

            for (int index = 0; index < moviePathKeys.Count; index++)
            {
                if (
                    string.Equals(
                        moviePathKeys[index],
                        moviePathKey,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return true;
                }
            }

            return false;
        }

        private ScrollViewer GetUpperTabViewportScrollViewer(ItemsControl itemsControl)
        {
            if (itemsControl == null)
            {
                return null;
            }

            if (_upperTabViewportScrollViewers.TryGetValue(itemsControl, out ScrollViewer cached))
            {
                return cached;
            }

            ScrollViewer resolved = UpperTabViewportTracker.FindScrollViewer(itemsControl);
            if (resolved != null)
            {
                _upperTabViewportScrollViewers[itemsControl] = resolved;
            }

            return resolved;
        }

        private Panel GetUpperTabItemsHostPanel(ItemsControl itemsControl)
        {
            if (itemsControl == null)
            {
                return null;
            }

            if (_upperTabViewportItemsHostPanels.TryGetValue(itemsControl, out Panel cached))
            {
                return cached;
            }

            Panel resolved = UpperTabViewportTracker.FindItemsHostPanel(itemsControl);
            if (resolved != null)
            {
                _upperTabViewportItemsHostPanels[itemsControl] = resolved;
            }

            return resolved;
        }

        private void UpperTabScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (!TryGetCurrentUpperTabContext(out int currentTabIndex, out bool isStandardUpperTab) || !isStandardUpperTab)
            {
                return;
            }

            if (
                ShouldSuppressUpperTabFollowupScrollRefresh(
                    DateTime.UtcNow.Ticks,
                    _upperTabFollowupScrollRefreshSuppressUntilUtcTicks
                )
            )
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"upper tab scroll follow-up suppressed: tab={currentTabIndex}"
                );
                return;
            }

            RequestUpperTabVisibleRangeRefresh(reason: "scroll");
        }

        private void ApplyUpperTabVisibleRangeRefresh(string reason)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            if (
                !TryResolveUpperTabViewportRefreshContext(
                    reason,
                    stopwatch.ElapsedMilliseconds,
                    out UpperTabViewportRefreshContext refreshContext
                )
            )
            {
                return;
            }

            (
                bool rangeChanged,
                IReadOnlyList<string> nextPreferredMoviePathKeys,
                bool preferredMoviePathKeysChanged
            ) = ResolveUpperTabViewportPreferredMoviePathKeys(refreshContext.NextRange);

            FinalizeUpperTabViewportRefresh(
                refreshContext,
                reason,
                nextPreferredMoviePathKeys,
                rangeChanged,
                preferredMoviePathKeysChanged,
                stopwatch.ElapsedMilliseconds
            );
        }

        private ItemsControl GetActiveUpperTabItemsControl()
        {
            return TryGetCurrentUpperTabItemsControl(
                out _,
                out ItemsControl itemsControl
            )
                ? itemsControl
                : null;
        }

        // viewport 更新に必要な入力をまとめて解決し、取れない時のログもここで揃える。
        private bool TryResolveUpperTabViewportRefreshContext(
            string reason,
            long elapsedMilliseconds,
            out UpperTabViewportRefreshContext refreshContext
        )
        {
            refreshContext = default;
            if (
                !TryGetCurrentUpperTabItemsControl(
                    out int currentTabIndex,
                    out ItemsControl activeItemsControl
                )
            )
            {
                HandleUnavailableUpperTabViewport(
                    currentTabIndex,
                    reason,
                    "visible=empty",
                    elapsedMilliseconds
                );
                return false;
            }

            ScrollViewer scrollViewer = GetUpperTabViewportScrollViewer(activeItemsControl);
            if (scrollViewer == null)
            {
                HandleUnavailableUpperTabViewport(
                    currentTabIndex,
                    reason,
                    "scrollviewer=missing",
                    elapsedMilliseconds
                );
                return false;
            }

            refreshContext = new UpperTabViewportRefreshContext(
                currentTabIndex,
                ResolveUpperTabVisibleRange(activeItemsControl, scrollViewer)
            );
            return true;
        }

        // viewport 計測に必要な host 解決と visible range 計算を 1 か所へまとめる。
        private UpperTabVisibleRange ResolveUpperTabVisibleRange(
            ItemsControl activeItemsControl,
            ScrollViewer scrollViewer
        )
        {
            if (ReferenceEquals(activeItemsControl, PlayerThumbnailList))
            {
                return UpperTabViewportTracker.GetVerticalItemVisibleRange(
                    scrollViewer,
                    activeItemsControl.Items.Count,
                    UpperTabViewportOverscanItemCount
                );
            }

            Panel itemsHostPanel = GetUpperTabItemsHostPanel(activeItemsControl);
            return UpperTabViewportTracker.GetVisibleRange(
                activeItemsControl,
                scrollViewer,
                itemsHostPanel,
                UpperTabViewportOverscanItemCount
            );
        }

        private MovieViewScrollAnchorContext? CaptureMovieViewScrollAnchor()
        {
            if (
                !TryGetCurrentUpperTabContext(out int currentTabIndex, out bool isStandardUpperTab)
                || !isStandardUpperTab
                || !TryGetItemsControlByUpperTabFixedIndex(
                    currentTabIndex,
                    out ItemsControl itemsControl
                )
            )
            {
                return null;
            }

            ScrollViewer scrollViewer = GetUpperTabViewportScrollViewer(itemsControl);
            if (scrollViewer == null)
            {
                return null;
            }

            UpperTabVisibleRange visibleRange = ResolveUpperTabVisibleRange(
                itemsControl,
                scrollViewer
            );
            int firstVisibleIndex = visibleRange.FirstVisibleIndex;
            if (
                !visibleRange.HasVisibleItems
                || firstVisibleIndex < 0
                || firstVisibleIndex >= itemsControl.Items.Count
                || itemsControl.Items[firstVisibleIndex] is not MovieRecords firstVisibleMovie
                || itemsControl.ItemContainerGenerator.ContainerFromIndex(firstVisibleIndex)
                    is not FrameworkElement container
                || !UpperTabViewportTracker.TryGetContainerTopRelativeToViewport(
                    container,
                    scrollViewer,
                    out double containerTop
                )
                || !MovieViewScrollAnchorPolicy.TryCapture(
                    firstVisibleMovie,
                    containerTop,
                    out MovieViewScrollAnchor anchor
                )
            )
            {
                return null;
            }

            return new MovieViewScrollAnchorContext(currentTabIndex, anchor);
        }

        private void RestoreMovieViewScrollAnchor(
            MovieViewScrollAnchorContext? anchorContext,
            FilteredMovieRecsUpdateMode updateMode,
            FilteredMovieRecsUpdateResult collectionResult
        )
        {
            if (
                anchorContext is not MovieViewScrollAnchorContext captured
                || !TryGetCurrentUpperTabContext(out int currentTabIndex, out bool isStandardUpperTab)
                || !isStandardUpperTab
                || currentTabIndex != captured.TabIndex
                || !TryGetItemsControlByUpperTabFixedIndex(
                    currentTabIndex,
                    out ItemsControl itemsControl
                )
            )
            {
                return;
            }

            MovieRecords anchorMovie = MovieViewScrollAnchorPolicy.ResolveAfterCollectionApply(
                captured.Anchor,
                MainVM?.FilteredMovieRecs,
                updateMode,
                collectionResult.HasChanges
            );
            ScrollViewer scrollViewer = GetUpperTabViewportScrollViewer(itemsControl);
            if (anchorMovie == null || scrollViewer == null)
            {
                return;
            }

            SuppressUpperTabFollowupScrollRefreshBriefly();
            try
            {
                // ScrollIntoView で仮想化コンテナを実現してから、Reset 前の上端へ微調整する。
                if (itemsControl is ListBox listBox)
                {
                    listBox.ScrollIntoView(anchorMovie);
                }
                else if (itemsControl is DataGrid dataGrid)
                {
                    dataGrid.ScrollIntoView(anchorMovie);
                }
                else
                {
                    return;
                }

                itemsControl.UpdateLayout();
                if (
                    itemsControl.ItemContainerGenerator.ContainerFromItem(anchorMovie)
                        is not FrameworkElement container
                    || !UpperTabViewportTracker.TryGetContainerTopRelativeToViewport(
                        container,
                        scrollViewer,
                        out double currentContainerTop
                    )
                )
                {
                    return;
                }

                double restoredOffset = MovieViewScrollAnchorPolicy.CalculateRestoredVerticalOffset(
                    scrollViewer.VerticalOffset,
                    currentContainerTop,
                    captured.Anchor.TopOffset
                );
                scrollViewer.ScrollToVerticalOffset(restoredOffset);
            }
            catch (InvalidOperationException)
            {
                // teardown や再仮想化と競合した場合は、現在位置を壊さず次の viewport 更新へ任せる。
            }
            finally
            {
                RequestUpperTabVisibleRangeRefresh(
                    immediate: true,
                    reason: "reset-scroll-anchor"
                );
            }
        }

        // viewport 差分と source revision を見て、preferred key の再計算要否をここで揃える。
        private (
            bool RangeChanged,
            IReadOnlyList<string> NextPreferredMoviePathKeys,
            bool PreferredMoviePathKeysChanged
        ) ResolveUpperTabViewportPreferredMoviePathKeys(UpperTabVisibleRange nextRange)
        {
            bool rangeChanged = !nextRange.Equals(_activeUpperTabVisibleRange);
            bool sourceChanged =
                _upperTabViewportSourceRevision != _preferredVisibleMoviePathKeysSourceRevision;
            IReadOnlyList<string> nextPreferredMoviePathKeys = _preferredVisibleMoviePathKeysSnapshot;
            bool preferredMoviePathKeysChanged = false;
            if (!rangeChanged && !sourceChanged)
            {
                return (rangeChanged, nextPreferredMoviePathKeys, preferredMoviePathKeysChanged);
            }

            nextPreferredMoviePathKeys = BuildPreferredVisibleMoviePathKeysSnapshot(nextRange);
            preferredMoviePathKeysChanged = !AreMoviePathKeyListsEqual(
                _preferredVisibleMoviePathKeysSnapshot,
                nextPreferredMoviePathKeys
            );
            return (rangeChanged, nextPreferredMoviePathKeys, preferredMoviePathKeysChanged);
        }

        // snapshot反映からログ、follow-up までの後半処理を 1 か所へ寄せる。
        private void FinalizeUpperTabViewportRefresh(
            UpperTabViewportRefreshContext refreshContext,
            string reason,
            IReadOnlyList<string> nextPreferredMoviePathKeys,
            bool rangeChanged,
            bool preferredMoviePathKeysChanged,
            long elapsedMilliseconds
        )
        {
            ApplyUpperTabViewportSnapshot(
                refreshContext.CurrentTabIndex,
                refreshContext.NextRange,
                nextPreferredMoviePathKeys,
                preferredMoviePathKeysChanged
            );
            TryScheduleStartupAppendForCurrentViewport($"viewport:{reason}");
            WriteUpperTabViewportRefreshLog(
                refreshContext.CurrentTabIndex,
                reason,
                refreshContext.NextRange,
                nextPreferredMoviePathKeys.Count,
                elapsedMilliseconds
            );

            if (!rangeChanged && !preferredMoviePathKeysChanged)
            {
                return;
            }

            TryQueueUpperTabVisibleErrorRescue(
                refreshContext.CurrentTabIndex,
                refreshContext.NextRange
            );
        }

        // viewport の計測結果を snapshot へ反映し、保持フィールド更新を 1 か所へ寄せる。
        private void ApplyUpperTabViewportSnapshot(
            int currentTabIndex,
            UpperTabVisibleRange nextRange,
            IReadOnlyList<string> nextPreferredMoviePathKeys,
            bool preferredMoviePathKeysChanged
        )
        {
            bool shouldPublishSnapshot = ShouldPublishPreferredMoviePathKeysSnapshot(nextRange);
            bool publishStateChanged =
                _isUpperTabPreferredMoviePathKeysSnapshotPublished != shouldPublishSnapshot;

            _activeUpperTabVisibleRange = nextRange;
            _preferredVisibleMoviePathKeysSnapshot = nextPreferredMoviePathKeys;
            _preferredVisibleMoviePathKeysTabIndex = shouldPublishSnapshot ? currentTabIndex : -1;
            _preferredVisibleMoviePathKeysSourceRevision = _upperTabViewportSourceRevision;
            _isUpperTabPreferredMoviePathKeysSnapshotPublished = shouldPublishSnapshot;
            if (shouldPublishSnapshot)
            {
                bool preferredMoviePathKeysGateChanged =
                    UpperTabActivationGate.UpdatePreferredMoviePathKeys(nextPreferredMoviePathKeys);
                if (publishStateChanged || preferredMoviePathKeysGateChanged)
                {
                    RefreshActiveUpperTabImageRevision();
                }

                return;
            }

            // Empty range は未計測の瞬間も含むため、空確定として全画像を落とさない。
            UpperTabActivationGate.ClearPreferredMoviePathKeys();
            if (publishStateChanged)
            {
                RefreshActiveUpperTabImageRevision();
            }
        }

        // viewport由来の更新はactiveタブだけへ通知し、非表示側の画像再評価を起こさない。
        private void RefreshActiveUpperTabImageRevision()
        {
            bool playerActive = TabPlayer?.IsSelected == true;
            if (playerActive)
            {
                if (_isPlayerThumbnailScrollUserPriorityActive)
                {
                    // scroll中のviewport差分はidle後のwarm反映へ畳み、再layoutを1回に抑える。
                    _playerRightRailViewportRevisionPending = true;
                }
                else
                {
                    RefreshPlayerRightRailImageRevision();
                }
            }
            else
            {
                _playerRightRailViewportRevisionPending = false;
                RefreshSharedUpperTabImageRevision();
            }

            DebugRuntimeLog.Write(
                "ui-tempo",
                $"upper tab image revision refreshed: shared_revision_updated={!playerActive} player_revision_updated={playerActive && !_playerRightRailViewportRevisionPending} viewport_revision_pending={_playerRightRailViewportRevisionPending} revision_reason={(playerActive ? _playerRightRailViewportRevisionPending ? "viewport-deferred-scroll-priority" : "viewport-immediate" : "shared-viewport")}"
            );
        }

        // サムネ実体変更など外部更新は、通常タブとPlayerの両方を確実に再評価する。
        private void RefreshUpperTabPreferredMoviePathKeysRevision()
        {
            RefreshSharedUpperTabImageRevision();
            RefreshPlayerRightRailImageRevision();
            DebugRuntimeLog.Write(
                "ui-tempo",
                "upper tab image revision refreshed: shared_revision_updated=True player_revision_updated=True"
            );
        }

        private void RefreshSharedUpperTabImageRevision()
        {
            // binding の軽い再評価だけを起こし、画像 decode そのものは converter の gate に任せる。
            UpperTabPreferredMoviePathKeysRevision = unchecked(
                UpperTabPreferredMoviePathKeysRevision + 1
            );
        }

        private void RefreshPlayerRightRailImageRevision()
        {
            // Player右レールだけを再評価し、通常タブの画像bindingを巻き込まない。
            PlayerRightRailImageRevision = unchecked(PlayerRightRailImageRevision + 1);
        }

        // viewport 計測不能時の後始末とログを 1 か所へ寄せ、早期 return を読みやすくする。
        private void HandleUnavailableUpperTabViewport(
            int currentTabIndex,
            string reason,
            string state,
            long elapsedMilliseconds
        )
        {
            bool shouldPreservePreferredMoviePathKeys =
                ShouldPreservePreferredMoviePathKeysOnUnavailableViewport(
                    _isUpperTabPreferredMoviePathKeysSnapshotPublished,
                    currentTabIndex,
                    _preferredVisibleMoviePathKeysTabIndex,
                    _upperTabViewportSourceRevision,
                    _preferredVisibleMoviePathKeysSourceRevision
                );

            if (!shouldPreservePreferredMoviePathKeys)
            {
                ClearUpperTabVisibleRange();
            }

            // 同一タブの一時的な未計測では前回 snapshot を残し、余分な画像再評価を増やさない。
            string preservedText = shouldPreservePreferredMoviePathKeys ? "true" : "false";
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"upper tab viewport: tab={currentTabIndex} reason={reason} {state} preserved={preservedText} preferred={_preferredVisibleMoviePathKeysSnapshot.Count} elapsed_ms={elapsedMilliseconds}"
            );
        }

        // viewport 更新ログの形を 1 か所へ寄せ、呼び出し側では流れだけ追えるようにする。
        private void WriteUpperTabViewportRefreshLog(
            int currentTabIndex,
            string reason,
            UpperTabVisibleRange nextRange,
            int preferredMoviePathKeyCount,
            long elapsedMilliseconds
        )
        {
            DebugRuntimeLog.Write(
                "ui-tempo",
                nextRange.HasVisibleItems
                    ? $"upper tab viewport: tab={currentTabIndex} reason={reason} visible={nextRange.FirstVisibleIndex}-{nextRange.LastVisibleIndex} near={nextRange.FirstNearVisibleIndex}-{nextRange.LastNearVisibleIndex} preferred={preferredMoviePathKeyCount} elapsed_ms={elapsedMilliseconds}"
                    : $"upper tab viewport: tab={currentTabIndex} reason={reason} visible=empty preferred={preferredMoviePathKeyCount} elapsed_ms={elapsedMilliseconds}"
            );
        }

        // 通常タブだけ error rescue の自動投入対象にし、特殊タブ分岐を呼び出し側から外す。
        private void TryQueueUpperTabVisibleErrorRescue(
            int currentTabIndex,
            UpperTabVisibleRange nextRange
        )
        {
            if (!IsStandardUpperTabFixedIndex(currentTabIndex))
            {
                return;
            }

            QueueVisibleUpperTabThumbnailErrorsToRescue(currentTabIndex, nextRange);
        }

        private void AppendMoviePathKeysInRange(
            List<string> result,
            HashSet<string> seen,
            int startIndex,
            int endIndex,
            int totalCount
        )
        {
            if (result == null || seen == null || totalCount < 1)
            {
                return;
            }

            int safeStartIndex = Math.Max(0, startIndex);
            int safeEndIndex = Math.Min(totalCount - 1, endIndex);
            if (safeEndIndex < safeStartIndex)
            {
                return;
            }

            for (int index = safeStartIndex; index <= safeEndIndex; index++)
            {
                MovieRecords movie = MainVM.FilteredMovieRecs[index];
                string moviePathKey = QueueDbPathResolver.CreateMoviePathKey(movie?.Movie_Path ?? "");
                if (string.IsNullOrWhiteSpace(moviePathKey) || !seen.Add(moviePathKey))
                {
                    continue;
                }

                result.Add(moviePathKey);
            }
        }

        private static bool AreMoviePathKeyListsEqual(
            IReadOnlyList<string> left,
            IReadOnlyList<string> right
        )
        {
            int leftCount = left?.Count ?? 0;
            int rightCount = right?.Count ?? 0;
            if (leftCount != rightCount)
            {
                return false;
            }

            for (int index = 0; index < leftCount; index++)
            {
                if (!string.Equals(left[index], right[index], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
