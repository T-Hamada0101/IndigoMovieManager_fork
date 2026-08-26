using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int PlayerTabIndex = 7;
        private const int PlayerTabActivationAutoOpenMaxDelayMs = 250;
        private const int PlayerUserPriorityMaxDurationMs = 250;
        private bool _suppressPlayerThumbnailSelectionChanged;
        private bool _suppressPlayerTabActivationAutoOpen;
        private int _playerTabActivationAutoOpenRevision;
        private bool _hasPendingPlayerPlaybackRequest;
        private int _pendingPlayerStartMilliseconds;
        private bool _pendingPlayerResetPosition;
        private bool _pendingPlayerPlayImmediately;
        private bool _pendingPlayerMute;
        private bool _pendingPlayerFocusTimeSlider;
        private bool _isPlayerUserPriorityReleasePending;
        private int _playerUserPriorityRevision;
        private int _pendingPlayerUserPriorityRevision;
        private DispatcherTimer _playerUserPriorityReleaseTimer;
        private DispatcherOperation _playerBackgroundYieldOperation;
        private string _currentPlayerMoviePath = "";
        private int _playerOpenRequestRevision;
        private int _playerLoadingRequestRevision;
        private int _currentPlayerMediaRequestRevision;
        private long _currentPlayerMediaRequestStartedTimestamp;

        private ListView GetUpperTabPlayerList()
        {
            return PlayerThumbnailList;
        }

        private IEnumerable<ListView> GetAllUpperTabPlayerLists()
        {
            if (PlayerThumbnailList != null)
            {
                yield return PlayerThumbnailList;
            }
        }

        private void SelectFirstUpperTabPlayerItemIfAvailable()
        {
            if (GetUpperTabPlayerList()?.Items.Count > 0 && GetUpperTabPlayerList().SelectedItem == null)
            {
                GetUpperTabPlayerList().SelectedIndex = 0;
            }
        }

        private void SelectUpperTabPlayerAsDefaultView()
        {
            SelectUpperTabByFixedIndex(PlayerTabIndex);
            _suppressPlayerThumbnailSelectionChanged = true;
            try
            {
                SelectFirstUpperTabPlayerItemIfAvailable();
            }
            finally
            {
                _suppressPlayerThumbnailSelectionChanged = false;
            }
        }

        private MovieRecords GetSelectedUpperTabPlayerMovieRecord()
        {
            return GetUpperTabPlayerList()?.SelectedItem as MovieRecords;
        }

        private List<MovieRecords> GetSelectedUpperTabPlayerMovieRecords()
        {
            List<MovieRecords> items = [];
            if (GetUpperTabPlayerList()?.SelectedItems == null)
            {
                return items;
            }

            foreach (MovieRecords item in GetUpperTabPlayerList().SelectedItems)
            {
                if (item != null)
                {
                    items.Add(item);
                }
            }

            return items;
        }

        private bool SelectUpperTabPlayerMovieRecord(MovieRecords record)
        {
            if (record == null)
            {
                return false;
            }

            bool selectionChanged = false;
            bool activeListSelectionChanged = false;
            ListView activeList = GetUpperTabPlayerList();
            foreach (ListView list in GetAllUpperTabPlayerLists())
            {
                if (ReferenceEquals(list.SelectedItem, record))
                {
                    continue;
                }

                list.SelectedItem = record;
                selectionChanged = true;
                if (ReferenceEquals(list, activeList))
                {
                    activeListSelectionChanged = true;
                }
            }

            if (activeListSelectionChanged)
            {
                // 表示中リストの選択が変わった時だけ再スクロールし、裏側List同期だけでは積み直さない。
                GetUpperTabPlayerList()?.ScrollIntoView(record);
            }

            return selectionChanged;
        }

        // プレイヤータブへ飛ばす時だけ、選択イベントの自動再生を一時停止して狙った動画へ揃える。
        private void SyncUpperTabPlayerSelection(MovieRecords record)
        {
            if (record == null || GetUpperTabPlayerList() == null)
            {
                return;
            }

            _suppressPlayerThumbnailSelectionChanged = true;
            bool selectionChanged = false;
            try
            {
                selectionChanged = SelectUpperTabPlayerMovieRecord(record);
            }
            finally
            {
                _suppressPlayerThumbnailSelectionChanged = false;
            }

            if (selectionChanged)
            {
                // 抑止中の選択同期では SelectionChanged 側を走らせないため、
                // 表示中 Player の詳細だけここで最小更新する。
                ShowExtensionDetail(record);
                ShowTagEditor(record);
                RequestUpperTabVisibleRangeRefresh(immediate: true, reason: "player-selection");
            }
        }

        // プレイヤータブ選択時は先頭選択を揃えたうえで、左ペインへ再生内容を同期する。
        private void HandleUpperTabPlayerSelectionChanged(
            Stopwatch selectionStopwatch,
            int tabIndex
        )
        {
            UpdatePlayerTabLayoutMode();

            MovieRecords selectedMovie;
            _suppressPlayerThumbnailSelectionChanged = true;
            try
            {
                selectedMovie = RefreshUpperTabExtensionDetailFromCurrentSelection(
                    selectFirstItem: true
                );
            }
            finally
            {
                _suppressPlayerThumbnailSelectionChanged = false;
            }
            if (selectedMovie == null)
            {
                selectionStopwatch.Stop();
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"tab change end: tab={tabIndex} selected=none player_items={GetUpperTabPlayerList()?.Items.Count ?? 0} total_ms={selectionStopwatch.ElapsedMilliseconds}"
                );
                ShowPlayerEmptyState();
                return;
            }

            selectionStopwatch.Stop();
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"tab change end: tab={tabIndex} selected='{selectedMovie.Movie_Name}' player_items={GetUpperTabPlayerList()?.Items.Count ?? 0} total_ms={selectionStopwatch.ElapsedMilliseconds}"
            );

            if (_suppressPlayerTabActivationAutoOpen)
            {
                return;
            }

            QueuePlayerTabActivationAutoOpen(selectedMovie);
        }

        // タブ表示そのものを先に返し、動画初期化は Dispatcher が落ち着いてから流す。
        private void QueuePlayerTabActivationAutoOpen(MovieRecords selectedMovie)
        {
            if (selectedMovie == null)
            {
                return;
            }

            int revision = ++_playerTabActivationAutoOpenRevision;
            _ = RunPlayerTabActivationAutoOpenAsync(selectedMovie, revision);
        }

        private async Task RunPlayerTabActivationAutoOpenAsync(
            MovieRecords selectedMovie,
            int revision
        )
        {
            try
            {
                await WaitForPlayerDispatcherContextIdleOrDelayAsync();
                if (
                    revision != _playerTabActivationAutoOpenRevision
                    || _suppressPlayerTabActivationAutoOpen
                    || TabPlayer?.IsSelected != true
                    || !IsSamePlayerTabAutoOpenMovie(
                        GetSelectedUpperTabPlayerMovieRecord(),
                        selectedMovie
                    )
                )
                {
                    return;
                }

                await OpenMovieInPlayerTabAsync(
                    selectedMovie,
                    0,
                    playImmediately: true,
                    mute: false,
                    focusTimeSlider: false
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"player activation auto-open failed: {ex.GetType().Name} {ex.Message}"
                );
            }
        }

        private static bool IsSamePlayerTabAutoOpenMovie(
            MovieRecords currentMovie,
            MovieRecords requestedMovie
        )
        {
            if (ReferenceEquals(currentMovie, requestedMovie))
            {
                return true;
            }

            if (currentMovie == null || requestedMovie == null)
            {
                return false;
            }

            if (
                currentMovie.Movie_Id > 0
                && requestedMovie.Movie_Id > 0
                && currentMovie.Movie_Id == requestedMovie.Movie_Id
            )
            {
                return true;
            }

            return string.Equals(
                currentMovie.Movie_Path,
                requestedMovie.Movie_Path,
                System.StringComparison.OrdinalIgnoreCase
            );
        }

        private async void PlayerThumbnailList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e
        )
        {
            if (_suppressPlayerThumbnailSelectionChanged || TabPlayer?.IsSelected != true)
            {
                return;
            }

            MovieRecords selectedMovie = GetSelectedUpperTabPlayerMovieRecord();
            if (selectedMovie == null)
            {
                List_SelectionChanged(sender, e);
                return;
            }

            List_SelectionChanged(sender, e);
            SyncPlayerThumbnailSelectionAcrossViews(sender as ListView, selectedMovie);

            await OpenMovieInPlayerTabAsync(
                selectedMovie,
                0,
                playImmediately: true,
                mute: false,
                focusTimeSlider: false
            );
        }

        private void PlayerTabLayoutRoot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePlayerTabLayoutMode();
            UpdateManualPlayerViewport();
        }

        // プレイヤータブ上では一覧選択と再生面を同じ動画へ揃え、手動サムネ導線もここへ寄せる。
        private async Task OpenMovieInPlayerTabAsync(
            MovieRecords movie,
            int startMilliseconds,
            bool playImmediately,
            bool mute,
            bool focusTimeSlider,
            bool syncPlayerSelection = true
        )
        {
            string moviePath = movie?.Movie_Path;
            if (
                movie == null
                || string.IsNullOrWhiteSpace(moviePath)
                || uxVideoPlayer == null
            )
            {
                return;
            }

            int requestRevision = ++_playerOpenRequestRevision;
            long requestStartedTimestamp = Stopwatch.GetTimestamp();
            bool sourceChangeRequested =
                !string.Equals(
                    _currentPlayerMoviePath,
                    moviePath,
                    System.StringComparison.OrdinalIgnoreCase
                )
                || _playerMediaPlayer == null;
            if (sourceChangeRequested)
            {
                ShowPlayerLoadingState(requestRevision, requestStartedTimestamp);
            }
            else
            {
                // 読込中の別動画から現在動画へ戻した時は、古い要求の表示をその場で退ける。
                ClearPlayerLoadingState();
            }
            Stopwatch pathCheckStopwatch = Stopwatch.StartNew();
            DebugRuntimeLog.Write(
                "player",
                $"player open requested: revision={requestRevision} path='{moviePath}'"
            );

            // ネットワークパス確認は UI から逃がし、存在しない時は優先区間を張らずに戻る。
            if (!await PlayerTabMoviePathExistsInBackgroundAsync(moviePath))
            {
                DebugRuntimeLog.Write(
                    "player",
                    $"player open skipped missing: revision={requestRevision} path_check_ms={pathCheckStopwatch.ElapsedMilliseconds} path='{moviePath}'"
                );
                HidePlayerLoadingState(requestRevision, "missing");
                return;
            }

            if (!IsPlayerOpenRequestCurrent(requestRevision))
            {
                LogStalePlayerOpenRequest(requestRevision, "after-path-check");
                return;
            }

            DebugRuntimeLog.Write(
                "player",
                $"player path check completed: revision={requestRevision} elapsed_ms={pathCheckStopwatch.ElapsedMilliseconds}"
            );

            if (
                Dispatcher?.HasShutdownStarted == true
                || Dispatcher?.HasShutdownFinished == true
            )
            {
                HidePlayerLoadingState(requestRevision, "shutdown");
                ReleasePendingPlayerUserPriorityWork("shutdown");
                return;
            }

            if (
                uxVideoPlayer == null
                || !IsPlayerTabMoviePathStillCurrent(movie, moviePath)
            )
            {
                HidePlayerLoadingState(requestRevision, "selection-changed");
                return;
            }

            if (
                !TryReleasePendingPlayerMovieDeletesForMovieSwitch(
                    moviePath,
                    out string pendingDeleteReleaseFailureReason
                )
            )
            {
                MessageBox.Show(
                    $"再生中動画をPlayerから解放できないため、削除と動画切替を中止しました。{Environment.NewLine}{pendingDeleteReleaseFailureReason}",
                    "削除予約",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                HidePlayerLoadingState(requestRevision, "delete-release-failed");
                return;
            }

            ReleasePendingPlayerUserPriorityWork("superseded");
            BeginUserPriorityWork("player");
            int playerUserPriorityRevision = ++_playerUserPriorityRevision;
            MarkPlayerUserPriorityReleasePending(playerUserPriorityRevision);
            bool releaseUserPriorityOnExit = true;
            try
            {
                EnsureManualPlayerResizeTrackingHooked();
                UpdatePlayerTabLayoutMode();

                if (!ReferenceEquals(Tabs?.SelectedItem, TabPlayer))
                {
                    _suppressPlayerTabActivationAutoOpen = true;
                    try
                    {
                        if (syncPlayerSelection)
                        {
                            SyncUpperTabPlayerSelection(movie);
                        }

                        SelectUpperTabByFixedIndex(PlayerTabIndex);
                        await WaitForPlayerDispatcherBackgroundAsync();
                        if (
                            uxVideoPlayer == null
                            || !IsPlayerOpenRequestCurrent(requestRevision)
                            || !IsPlayerTabMoviePathStillCurrent(movie, moviePath)
                        )
                        {
                            HidePlayerLoadingState(requestRevision, "tab-selection-changed");
                            return;
                        }
                    }
                    finally
                    {
                        _suppressPlayerTabActivationAutoOpen = false;
                    }
                }
                else
                {
                    if (syncPlayerSelection)
                    {
                        SyncUpperTabPlayerSelection(movie);
                    }
                }

                ShowPlayerSurface();
                StopDispatcherTimerSafely(timer, nameof(timer));
                bool sourceChanged = sourceChangeRequested;
                RememberPendingPlayerPlaybackRequest(
                    startMilliseconds,
                    resetPosition: sourceChanged,
                    playImmediately,
                    mute,
                    focusTimeSlider
                );

                if (sourceChanged)
                {
                    _currentPlayerMoviePath = moviePath;
                    PlayerMediaOpenResult openResult = await TryOpenLibVlcMovieAsync(
                        moviePath,
                        requestRevision,
                        requestStartedTimestamp
                    );
                    if (openResult == PlayerMediaOpenResult.Stale)
                    {
                        return;
                    }

                    if (openResult != PlayerMediaOpenResult.Started)
                    {
                        HidePlayerLoadingState(requestRevision, "open-failed");
                        _hasPendingPlayerPlaybackRequest = false;
                        SetPlayerPlaybackActive(false, "libvlc-open-failed");
                        ReleasePendingPlayerUserPriorityWork(
                            "libvlc-open-failed",
                            playerUserPriorityRevision
                        );
                        return;
                    }

                    releaseUserPriorityOnExit = false;
                    return;
                }

                HidePlayerLoadingState(requestRevision, "same-source");
                await ApplyPendingPlayerPlaybackRequestAsync();
            }
            finally
            {
                if (releaseUserPriorityOnExit)
                {
                    ReleasePendingPlayerUserPriorityWork(
                        "request-exit",
                        playerUserPriorityRevision
                    );
                }
            }
        }

        private bool IsPlayerOpenRequestCurrent(int requestRevision)
        {
            return requestRevision == _playerOpenRequestRevision;
        }

        private void LogStalePlayerOpenRequest(int requestRevision, string stage)
        {
            DebugRuntimeLog.Write(
                "player",
                $"player open skipped stale: revision={requestRevision} current_revision={_playerOpenRequestRevision} stage={stage}"
            );
        }

        private static Task<bool> PlayerTabMoviePathExistsInBackgroundAsync(string moviePath)
        {
            if (string.IsNullOrWhiteSpace(moviePath))
            {
                return Task.FromResult(false);
            }

            return Task.Run(() =>
            {
                try
                {
                    return Path.Exists(moviePath);
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write("ui-tempo", $"player path exists failed: {ex.Message}");
                    return false;
                }
            });
        }

        private static bool IsPlayerTabMoviePathStillCurrent(MovieRecords movie, string moviePath)
        {
            return movie != null
                && string.Equals(
                    movie.Movie_Path,
                    moviePath,
                    System.StringComparison.OrdinalIgnoreCase
                );
        }

        // 別動画の選択が確定した時だけ旧Playerを解放し、確認済み削除リストを実行へ移す。
        private bool TryReleasePendingPlayerMovieDeletesForMovieSwitch(
            string nextMoviePath,
            out string failureReason
        )
        {
            failureReason = "";
            string playerMoviePath = _currentPlayerMoviePath;
            if (
                _pendingPlayerMovieDeleteRequests.Count == 0
                || string.Equals(
                    playerMoviePath,
                    nextMoviePath,
                    System.StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return true;
            }

            CloseManualPlayerOverlay(invalidateOpenRequest: false);
            if (!TryReleaseCurrentLibVlcMediaForDelete(out failureReason))
            {
                return false;
            }

            DebugRuntimeLog.Write(
                "delete-action",
                $"playing movie released after next selection: old_path='{playerMoviePath}' next_path='{nextMoviePath}' pending={_pendingPlayerMovieDeleteRequests.Count}"
            );
            QueuePendingPlayerMovieDeletesAfterPlayerRelease();
            return true;
        }

        internal static (
            MovieRecords[] PendingRecords,
            MovieRecords[] ImmediateRecords
        ) PartitionCurrentPlayerMovieDeleteRecords(
            IReadOnlyList<MovieRecords> records,
            string playerMoviePath
        )
        {
            MovieRecords[] normalizedRecords = records?.Where(record => record != null).ToArray() ?? [];
            if (normalizedRecords.Length == 0 || string.IsNullOrWhiteSpace(playerMoviePath))
            {
                return ([], normalizedRecords);
            }

            MovieRecords[] pendingRecords = normalizedRecords
                .Where(record =>
                    string.Equals(
                        record.Movie_Path,
                        playerMoviePath,
                        System.StringComparison.OrdinalIgnoreCase
                    )
                )
                .ToArray();
            MovieRecords[] immediateRecords = normalizedRecords
                .Where(record =>
                    !string.Equals(
                        record.Movie_Path,
                        playerMoviePath,
                        System.StringComparison.OrdinalIgnoreCase
                    )
                )
                .ToArray();
            return (pendingRecords, immediateRecords);
        }

        private void MarkPlayerUserPriorityReleasePending(int revision)
        {
            if (revision != _playerUserPriorityRevision)
            {
                return;
            }

            _isPlayerUserPriorityReleasePending = true;
            _pendingPlayerUserPriorityRevision = revision;
            _playerUserPriorityReleaseTimer ??= new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = System.TimeSpan.FromMilliseconds(PlayerUserPriorityMaxDurationMs),
            };
            _playerUserPriorityReleaseTimer.Tick -= PlayerUserPriorityReleaseTimer_Tick;
            _playerUserPriorityReleaseTimer.Tick += PlayerUserPriorityReleaseTimer_Tick;
            _playerUserPriorityReleaseTimer.Stop();
            _playerUserPriorityReleaseTimer.Start();
        }

        private void PlayerUserPriorityReleaseTimer_Tick(object sender, System.EventArgs e)
        {
            ReleasePendingPlayerUserPriorityWork("timeout", _pendingPlayerUserPriorityRevision);
        }

        private void ReleasePendingPlayerUserPriorityWork(
            string releaseReason = "completed",
            int expectedRevision = 0
        )
        {
            if (
                !_isPlayerUserPriorityReleasePending
                || (expectedRevision != 0 && expectedRevision != _pendingPlayerUserPriorityRevision)
            )
            {
                return;
            }

            _playerUserPriorityReleaseTimer?.Stop();
            int releasedRevision = _pendingPlayerUserPriorityRevision;
            _isPlayerUserPriorityReleasePending = false;
            _pendingPlayerUserPriorityRevision = 0;
            EndUserPriorityWork("player");
            DebugRuntimeLog.Write(
                "ui-priority",
                $"player user priority released: revision={releasedRevision} release_reason={releaseReason}"
            );
        }

        // 同一タイミングの no-op Dispatcher 待機は1本へ畳み、選択連打時の積み過ぎを抑える。
        private async Task WaitForPlayerDispatcherBackgroundAsync()
        {
            if (Dispatcher == null || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            DispatcherOperation pendingOperation = _playerBackgroundYieldOperation;
            if (
                pendingOperation == null
                || pendingOperation.Status == DispatcherOperationStatus.Completed
                || pendingOperation.Status == DispatcherOperationStatus.Aborted
            )
            {
                pendingOperation = Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                _playerBackgroundYieldOperation = pendingOperation;
            }

            try
            {
                await pendingOperation.Task;
            }
            catch (InvalidOperationException)
            {
                // shutdown 競合時はここで止めず、次の操作へ進める。
            }
            catch (TaskCanceledException)
            {
                // Dispatcher 側で中断された待機は安全に捨てる。
            }
        }

        private async Task WaitForPlayerDispatcherContextIdleOrDelayAsync()
        {
            if (Dispatcher == null || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            try
            {
                Task idleTask = Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;
                Task delayTask = Task.Delay(PlayerTabActivationAutoOpenMaxDelayMs);
                Task completedTask = await Task.WhenAny(idleTask, delayTask);
                if (ReferenceEquals(completedTask, idleTask))
                {
                    await idleTask;
                }
            }
            catch (InvalidOperationException)
            {
                // shutdown 競合時はここで止めず、予約再生を捨てる。
            }
            catch (TaskCanceledException)
            {
                // Dispatcher 側で中断された待機は安全に捨てる。
            }
        }

        // メディア差し替えからLibVLCのPlaying通知まで、再生要求を落とさず持ち運ぶ。
        private void RememberPendingPlayerPlaybackRequest(
            int startMilliseconds,
            bool resetPosition,
            bool playImmediately,
            bool mute,
            bool focusTimeSlider
        )
        {
            // 新しい動画のPlaying通知前に同じ行を再選択しても、未適用の0秒リセットを失わない。
            bool preservePendingReset =
                _hasPendingPlayerPlaybackRequest && _pendingPlayerResetPosition;
            _hasPendingPlayerPlaybackRequest = true;
            _pendingPlayerStartMilliseconds = startMilliseconds;
            _pendingPlayerResetPosition = preservePendingReset || resetPosition;
            _pendingPlayerPlayImmediately = playImmediately;
            _pendingPlayerMute = mute;
            _pendingPlayerFocusTimeSlider = focusTimeSlider;
        }

        private async Task ApplyPendingPlayerPlaybackRequestAsync()
        {
            bool shouldReleaseUserPriority =
                _hasPendingPlayerPlaybackRequest || _isPlayerUserPriorityReleasePending;
            if (!_hasPendingPlayerPlaybackRequest || uxVideoPlayer == null)
            {
                if (shouldReleaseUserPriority)
                {
                    ReleasePendingPlayerUserPriorityWork();
                }
                return;
            }

            try
            {
                int startMilliseconds = _pendingPlayerStartMilliseconds;
                bool resetPosition = _pendingPlayerResetPosition;
                bool playImmediately = _pendingPlayerPlayImmediately;
                bool mute = _pendingPlayerMute;
                bool focusTimeSlider = _pendingPlayerFocusTimeSlider;
                _hasPendingPlayerPlaybackRequest = false;

                ApplyCurrentPlayerVolumeToEngine();
                SetPlayerEngineMuted(mute);
                UpdatePlayerVolumeUi();

                // 別動画は0秒も明示適用し、同じ動画の再表示だけ現在位置を維持する。
                if (resetPosition || startMilliseconds > 0)
                {
                    SetPlayerPosition(System.TimeSpan.FromMilliseconds(startMilliseconds));
                }

                StartLibVlcPlayback();
                UpdateManualPlayerViewport();
                UpdatePlayerPositionUi(GetPlayerPosition());

                if (playImmediately)
                {
                    SetPlayerPlaybackActive(true, "player-tab-open");
                    TryStartDispatcherTimer(timer, nameof(timer));
                }
                else
                {
                    await WaitForPlayerDispatcherBackgroundAsync();
                    PauseLibVlcPlayback();
                    SetPlayerPlaybackActive(false, "player-tab-open-paused");
                    StopDispatcherTimerSafely(timer, nameof(timer));
                }

                if (focusTimeSlider)
                {
                    uxTimeSlider.Focus();
                }
            }
            finally
            {
                if (shouldReleaseUserPriority)
                {
                    ReleasePendingPlayerUserPriorityWork();
                }
            }
        }

        private void ShowPlayerSurface()
        {
            SetPlayerVisibilityIfChanged(PlayerArea, Visibility.Visible);
            SetPlayerVisibilityIfChanged(PlayerController, Visibility.Visible);
            SetPlayerVisibilityIfChanged(uxVideoPlayer, Visibility.Visible);
            if (PlayerEmptyState != null)
            {
                SetPlayerVisibilityIfChanged(PlayerEmptyState, Visibility.Collapsed);
            }

            UpdatePlayerPlayPauseUi(IsPlayerPlaybackActive());
            UpdatePlayerVolumeUi();
        }

        private void ShowPlayerLoadingState(int requestRevision, long requestStartedTimestamp)
        {
            _playerLoadingRequestRevision = requestRevision;
            SetPlayerVisibilityIfChanged(PlayerLoadingState, Visibility.Visible);
            long indicatorElapsedMs = (long)
                Stopwatch.GetElapsedTime(requestStartedTimestamp).TotalMilliseconds;
            DebugRuntimeLog.Write(
                "player",
                $"player loading shown: revision={requestRevision} indicator_ms={indicatorElapsedMs}"
            );
        }

        private void HidePlayerLoadingState(int requestRevision, string reason)
        {
            if (requestRevision <= 0 || requestRevision != _playerLoadingRequestRevision)
            {
                return;
            }

            SetPlayerVisibilityIfChanged(PlayerLoadingState, Visibility.Collapsed);
            _playerLoadingRequestRevision = 0;
            DebugRuntimeLog.Write(
                "player",
                $"player loading hidden: revision={requestRevision} reason={reason}"
            );
        }

        private void ClearPlayerLoadingState()
        {
            SetPlayerVisibilityIfChanged(PlayerLoadingState, Visibility.Collapsed);
            _playerLoadingRequestRevision = 0;
        }

        private void ShowPlayerEmptyState()
        {
            ClearPlayerLoadingState();
            if (PlayerEmptyState != null)
            {
                SetPlayerVisibilityIfChanged(PlayerEmptyState, Visibility.Visible);
            }
        }

        private static void SetPlayerVisibilityIfChanged(UIElement element, Visibility visibility)
        {
            if (element == null || element.Visibility == visibility)
            {
                return;
            }

            // 同じ表示状態の再代入を避け、Player 操作後の余計なレイアウト更新を増やさない。
            element.Visibility = visibility;
        }

        // タブを離れたら音だけ残さないよう一旦止め、戻った時は同じ動画を再開しやすくする。
        private void PausePlayerTabPlaybackForBackground()
        {
            ++_playerTabActivationAutoOpenRevision;

            if (_isDetachedPlayerFullscreenActive)
            {
                return;
            }

            // Playerを離れた後に初期化待ちの古い選択が再生を始めないよう、open要求も無効化する。
            ++_playerOpenRequestRevision;
            _hasPendingPlayerPlaybackRequest = false;
            ClearPlayerLoadingState();

            if (PlayerArea?.Visibility != Visibility.Visible)
            {
                return;
            }

            if (_playerMediaPlayer == null)
            {
                return;
            }

            PauseLibVlcPlayback();
            SetPlayerPlaybackActive(false, "background-pause");
            StopDispatcherTimerSafely(timer, nameof(timer));
        }

        private void UpdatePlayerPositionUi(System.TimeSpan position)
        {
            if (uxTimeSlider != null)
            {
                double maximum = System.Math.Max(0d, uxTimeSlider.Maximum);
                double nextValue = System.Math.Clamp(position.TotalMilliseconds, 0d, maximum);
                _isTimeSliderSyncingFromPlayer = true;
                try
                {
                    uxTimeSlider.Value = nextValue;
                }
                finally
                {
                    _isTimeSliderSyncingFromPlayer = false;
                }
            }

            UpdatePlayerTimeLabel(position);
        }

        private void UpdatePlayerTimeLabel(System.TimeSpan position)
        {
            if (uxTime != null)
            {
                double durationMilliseconds = uxTimeSlider?.Maximum ?? 0d;
                uxTime.Text = BuildPlayerTimeLabel(position, durationMilliseconds);
            }
        }

        internal static string BuildPlayerTimeLabel(
            System.TimeSpan position,
            double durationMilliseconds
        )
        {
            System.TimeSpan safePosition = position < System.TimeSpan.Zero
                ? System.TimeSpan.Zero
                : position;
            double safeDurationMilliseconds =
                double.IsNaN(durationMilliseconds)
                || double.IsInfinity(durationMilliseconds)
                || durationMilliseconds < 0d
                    ? 0d
                    : durationMilliseconds;
            System.TimeSpan duration = System.TimeSpan.FromMilliseconds(safeDurationMilliseconds);
            return $"{FormatPlayerClock(safePosition)} / {FormatPlayerClock(duration)}";
        }

        private static string FormatPlayerClock(System.TimeSpan value)
        {
            int totalHours = System.Math.Max(0, (int)System.Math.Floor(value.TotalHours));
            if (totalHours > 0)
            {
                return $"{totalHours}:{value.Minutes:00}:{value.Seconds:00}";
            }

            int totalMinutes = System.Math.Max(0, (int)System.Math.Floor(value.TotalMinutes));
            return $"{totalMinutes}:{value.Seconds:00}";
        }

        private bool TryGetPlayerTabViewportSize(out double availableWidth, out double availableHeight)
        {
            availableWidth = 0d;
            availableHeight = 0d;

            if (TabPlayer?.IsSelected != true || PlayerSurfaceHost == null)
            {
                return false;
            }

            availableWidth = System.Math.Max(0d, PlayerSurfaceHost.ActualWidth - 16d);
            availableHeight = System.Math.Max(0d, PlayerSurfaceHost.ActualHeight - 16d);
            return availableWidth > 0d && availableHeight > 0d;
        }

        // プレイヤータブは右レール固定に寄せ、Grid風サムネを1系統だけ流す。
        private void UpdatePlayerTabLayoutMode()
        {
            if (
                PlayerTabLayoutRoot == null
                || PlayerSurfaceHost == null
                || PlayerThumbnailHost == null
                || PlayerTabMainColumn == null
                || PlayerTabGapColumn == null
                || PlayerTabSideColumn == null
                || PlayerTabTopRow == null
                || PlayerTabGapRow == null
                || PlayerTabBottomRow == null
            )
            {
                return;
            }

            PlayerThumbnailHost.Visibility = Visibility.Visible;
            Grid.SetRow(PlayerSurfaceHost, 0);
            Grid.SetColumn(PlayerSurfaceHost, 0);
            Grid.SetColumnSpan(PlayerSurfaceHost, 1);
            Grid.SetRow(PlayerThumbnailHost, 0);
            Grid.SetColumn(PlayerThumbnailHost, 2);
            Grid.SetColumnSpan(PlayerThumbnailHost, 1);

            PlayerTabMainColumn.Width = new GridLength(1d, GridUnitType.Star);
            PlayerTabGapColumn.Width = new GridLength(12d);
            PlayerTabSideColumn.Width = new GridLength(264d);
            PlayerTabTopRow.Height = new GridLength(1d, GridUnitType.Star);
            PlayerTabGapRow.Height = new GridLength(0d);
            PlayerTabBottomRow.Height = new GridLength(0d);
        }

        private void SyncPlayerThumbnailSelectionAcrossViews(
            ListView sourceList,
            MovieRecords selectedMovie
        )
        {
            if (selectedMovie == null)
            {
                return;
            }

            _suppressPlayerThumbnailSelectionChanged = true;
            try
            {
                foreach (ListView list in GetAllUpperTabPlayerLists())
                {
                    if (ReferenceEquals(list, sourceList))
                    {
                        continue;
                    }

                    if (!ReferenceEquals(list.SelectedItem, selectedMovie))
                    {
                        list.SelectedItem = selectedMovie;
                    }
                }
            }
            finally
            {
                _suppressPlayerThumbnailSelectionChanged = false;
            }
        }

        // プレイヤータブは Grid サムネを借りるので、画像系の扱いだけ Grid 固定IDへ正規化する。
        private static int ResolvePlayerTabGridProxyTabIndex(int tabIndex)
        {
            return tabIndex == PlayerTabIndex ? UpperTabGridFixedIndex : tabIndex;
        }

    }
}
