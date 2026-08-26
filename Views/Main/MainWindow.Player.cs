using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using IndigoMovieManager.Thumbnail;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const double ManualPlayerPreferredLandscapeWidth = 900d;
        private const double ManualPlayerHorizontalPadding = 96d;
        private const double ManualPlayerVerticalPadding = 120d;
        private const string PlayerPlayGlyph = "\uE768";
        private const string PlayerPauseGlyph = "\uE769";
        private const string PlayerVolumeOnGlyph = "\uE767";
        private const string PlayerMutedGlyph = "\uE74F";
        private bool _isTimeSliderSyncingFromPlayer;
        private bool _isTimeSliderDragging;
        private bool _isManualPlayerResizeTrackingHooked;
        private bool _isPlayerVolumeApplyingToUi;
        private bool _isPlayerVolumeInitialized;
        private double _currentPlayerVolume = PlayerVolumePolicy.DefaultVolume;
        private DispatcherTimer _playerVolumeSaveDebounceTimer;
        private readonly object _playerVolumeSettingsSaveSync = new();
        private Task _playerVolumeSettingsSaveTask = Task.CompletedTask;

        // 保存済み設定が壊れていても、音量は常に 0.0 から 1.0 の安全域へ戻す。
        private static double ClampPlayerVolumeSetting(double volume)
        {
            return PlayerVolumePolicy.Normalize(volume);
        }

        // 起動時の復元も中央入口へ通し、保存値・UI・プレイヤーの正本を分散させない。
        private void RestorePlayerVolumeFromSettings()
        {
            double rawSavedVolume = Properties.Settings.Default.PlayerVolume;
            double savedPlayerVolume = PlayerVolumePolicy.Normalize(rawSavedVolume);
            bool repairSavedVolume = PlayerVolumePolicy.RequiresRepair(rawSavedVolume);

            ApplyPlayerVolumeSetting(
                savedPlayerVolume,
                updateSlider: true,
                save: repairSavedVolume,
                applyToEngine: true
            );
            _isPlayerVolumeInitialized = true;
        }

        // 他ファイルから音量を参照する時も、スライダー値ではなくこの正本を使う。
        private double GetCurrentPlayerVolumeSetting()
        {
            return ClampPlayerVolumeSetting(_currentPlayerVolume);
        }

        // 画面表示・保存・LibVLC反映をここへ集約し、動画切り替えごとの音量ぶれを防ぐ。
        private void ApplyPlayerVolumeSetting(
            double volume,
            bool updateSlider,
            bool save,
            bool applyToEngine
        )
        {
            double resolvedVolume = ClampPlayerVolumeSetting(volume);
            _currentPlayerVolume = resolvedVolume;

            if (applyToEngine)
            {
                ApplyCurrentPlayerVolumeToEngine();
            }

            UpdatePlayerVolumeUi();

            if (uxVolume != null)
            {
                uxVolume.Text = ((int)(resolvedVolume * 100)).ToString();
            }

            if (
                updateSlider
                && uxVolumeSlider != null
                && Math.Abs(uxVolumeSlider.Value - resolvedVolume) > 0.0001d
            )
            {
                _isPlayerVolumeApplyingToUi = true;
                try
                {
                    uxVolumeSlider.Value = resolvedVolume;
                }
                finally
                {
                    _isPlayerVolumeApplyingToUi = false;
                }
            }

            bool shouldPersistPlayerVolume = save && !App.IsDiagnosticNoPersistEnabled();
            if (save && !shouldPersistPlayerVolume)
            {
                DebugRuntimeLog.Write(
                    "player",
                    "player volume settings save skipped: diagnostic_no_persist=1"
                );
            }

            // 保存が必要な入口だけここで畳み、設定ファイルへ直接触る場所を増やさない。
            if (
                shouldPersistPlayerVolume
                && Math.Abs(Properties.Settings.Default.PlayerVolume - resolvedVolume) > 0.0001d
            )
            {
                lock (_playerVolumeSettingsSaveSync)
                {
                    Properties.Settings.Default.PlayerVolume = resolvedVolume;
                }
                QueuePlayerVolumeSettingSave();
            }

        }

        private void UpdatePlayerVolumeUi()
        {
            if (PlayerVolumeGlyph == null)
            {
                return;
            }

            bool isMuted = IsPlayerEngineMuted() || GetCurrentPlayerVolumeSetting() <= 0.0001d;
            PlayerVolumeGlyph.Text = isMuted ? PlayerMutedGlyph : PlayerVolumeOnGlyph;
        }

        // ユーザー操作は保存し、現在のLibVLCプレイヤーにも即時反映する。
        private void SetPlayerVolumeFromUser(double volume)
        {
            ApplyPlayerVolumeSetting(
                volume,
                updateSlider: true,
                save: true,
                applyToEngine: true
            );
        }

        // スライダー操作中の連続保存を畳み、UIスレッドの細かい詰まりを避ける。
        private void QueuePlayerVolumeSettingSave()
        {
            if (_playerVolumeSaveDebounceTimer == null)
            {
                _playerVolumeSaveDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(500),
                };
                _playerVolumeSaveDebounceTimer.Tick += PlayerVolumeSaveDebounceTimer_Tick;
            }

            StopDispatcherTimerSafely(
                _playerVolumeSaveDebounceTimer,
                nameof(_playerVolumeSaveDebounceTimer)
            );
            TryStartDispatcherTimer(
                _playerVolumeSaveDebounceTimer,
                nameof(_playerVolumeSaveDebounceTimer)
            );
        }

        private void PlayerVolumeSaveDebounceTimer_Tick(object sender, EventArgs e)
        {
            StopDispatcherTimerSafely(
                _playerVolumeSaveDebounceTimer,
                nameof(_playerVolumeSaveDebounceTimer)
            );
            QueuePlayerVolumeSettingSaveInBackground();
        }

        // 設定ファイル保存はディスクI/Oなので、UI操作の余韻を邪魔しないよう直列の背景保存へ逃がす。
        private void QueuePlayerVolumeSettingSaveInBackground()
        {
            PersistenceWriteRequest writeRequest = BuildPlayerVolumeSettingsWriteRequest();
            if (App.IsDiagnosticNoPersistEnabled())
            {
                DebugRuntimeLog.Write(
                    "player",
                    $"player volume settings save skipped in background: {writeRequest.BuildLogFields()} diagnostic_no_persist=1"
                );
                return;
            }

            lock (_playerVolumeSettingsSaveSync)
            {
                _playerVolumeSettingsSaveTask = _playerVolumeSettingsSaveTask.ContinueWith(
                    _ => SavePlayerVolumeSettingInBackground(writeRequest),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default
                );
            }
        }

        private static PersistenceWriteRequest BuildPlayerVolumeSettingsWriteRequest()
        {
            return PersistenceWriteRequest.Create(
                PersistenceWriteKind.ApplicationSettings,
                "player-volume",
                "player-volume-settings",
                retryable: true
            );
        }

        private void SavePlayerVolumeSettingInBackground(PersistenceWriteRequest writeRequest)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                if (App.IsDiagnosticNoPersistEnabled())
                {
                    DebugRuntimeLog.Write(
                        "player",
                        $"player volume settings save skipped in save task: {writeRequest.BuildLogFields()} diagnostic_no_persist=1"
                    );
                    return;
                }

                lock (_playerVolumeSettingsSaveSync)
                {
                    Properties.Settings.Default.Save();
                }

                PersistenceWriteResult result = PersistenceWriteResult.FromSuccess(
                    writeRequest,
                    stopwatch.Elapsed
                );
                DebugRuntimeLog.Write(
                    "player",
                    $"player volume settings save succeeded: {result.LogFields}"
                );
            }
            catch (Exception ex)
            {
                PersistenceWriteResult result = PersistenceWriteResult.FromFailure(
                    writeRequest,
                    stopwatch.Elapsed,
                    PersistenceFailureKind.ApplicationSettings
                );
                DebugRuntimeLog.Write(
                    "player",
                    $"player volume settings save failed: {result.LogFields} err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

        // 終了直前の音量変更を落とさないよう、shutdown では短時間だけ保存完了を回収する。
        private void WaitForPlayerVolumeSettingSaveForShutdown(int timeoutMs = 1000)
        {
            PersistenceWriteRequest writeRequest = BuildPlayerVolumeSettingsWriteRequest();
            Task saveTask;
            lock (_playerVolumeSettingsSaveSync)
            {
                saveTask = _playerVolumeSettingsSaveTask;
            }

            try
            {
                if (!saveTask.Wait(Math.Max(0, timeoutMs)))
                {
                    DebugRuntimeLog.Write(
                        "player",
                        $"player volume settings save drain timeout: {writeRequest.BuildLogFields()} timeout_ms={timeoutMs}"
                    );
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "player",
                    $"player volume settings save drain failed: {writeRequest.BuildLogFields()} err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

        public async void PlayMovie_Click(object sender, RoutedEventArgs e)
        {
            var playerPrg = SelectSystemTable("playerPrg");
            var playerParam = SelectSystemTable("playerParam");

            //設定DBごとのプレイヤーが空
            if (string.IsNullOrEmpty(playerPrg))
            {
                //全体設定のプレイヤーを設定
                playerPrg = Properties.Settings.Default.DefaultPlayerPath;
            }

            //設定DBごとのプレイヤーパラメータが空
            if (string.IsNullOrEmpty(playerParam))
            {
                //全体設定のプレイヤーパラメータを設定
                playerParam = Properties.Settings.Default.DefaultPlayerParam;
            }

            int msec = 0;
            string moviePath = "";
            string playbackMoviePath = "";
            MovieRecords mv = new();
            bool notBookmark = true;

            if (sender is Label labelObj)
            {
                if (labelObj.Name == "LabelBookMark")
                {
                    var item = (Label)sender;
                    if (item != null)
                    {
                        notBookmark = false;
                        mv = item.DataContext as MovieRecords;
                        //実ムービーファイルのパスを取得する。Movie_Bodyに入っているファイル名の一部で検索する。
                        MovieRecords bookmarkedMv = MainVM
                            .MovieRecs.Where(x =>
                                x.Movie_Name.Contains(
                                    mv.Movie_Body,
                                    StringComparison.CurrentCultureIgnoreCase
                                )
                            )
                            .First();
                        var BookMarkedFilePath = bookmarkedMv.Movie_Path;
                        playbackMoviePath = BookMarkedFilePath;
                        msec = await ResolveBookmarkPlaybackMillisecondsAsync(
                            BookMarkedFilePath,
                            mv.Score
                        );
                        moviePath = $"\"{BookMarkedFilePath}\"";
                        QueueBookmarkViewCountUpdate(MainVM?.DbInfo?.DBFullPath ?? "", mv.Movie_Id);
                    }
                }
            }

            if (notBookmark)
            {
                if (Tabs.SelectedItem == null)
                {
                    return;
                }

                mv = GetSelectedItemByTabIndex();
                if (mv == null)
                {
                    return;
                }

                string selectedMoviePath = mv.Movie_Path;
                playbackMoviePath = selectedMoviePath;
                moviePath = $"\"{selectedMoviePath}\"";

                if (!await MoviePathExistsForPlaybackAsync(selectedMoviePath))
                {
                    return;
                }

                if (sender is MenuItem senderObj)
                {
                    if (senderObj.Name == "PlayFromThumb")
                    {
                        var playbackPosition = await ResolveSelectedThumbnailPlaybackPositionAsync(
                            GetCurrentThumbnailActionTabIndex(),
                            mv
                        );
                        if (playbackPosition.HasPosition)
                        {
                            msec = playbackPosition.Milliseconds;
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(playerParam))
            {
                playerParam = playerParam.Replace("<file>", $"{playbackMoviePath}");
                playerParam = playerParam.Replace("<ms>", $"{msec}");
            }

            var arg = $"{moviePath} {playerParam}";

            try
            {
                using Process ps1 = new();
                //設定ファイルのプログラムも既定のプログラムも空だった場合にはここのはず。
                if (string.IsNullOrEmpty(playerPrg))
                {
                    ps1.StartInfo.UseShellExecute = true;
                    ps1.StartInfo.FileName = moviePath;
                }
                else
                {
                    ps1.StartInfo.Arguments = arg;
                    ps1.StartInfo.FileName = playerPrg;
                }
                ps1.Start();

                var psName = ps1.ProcessName;
                Process ps2 = Process.GetProcessById(ps1.Id);
                foreach (Process p in Process.GetProcessesByName(psName))
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        if (
                            p.MainWindowTitle.Contains(
                                mv.Movie_Name,
                                StringComparison.CurrentCultureIgnoreCase
                            )
                        )
                        {
                            p.Kill();
                            await p.WaitForExitAsync();
                        }
                    }
                }
                mv.View_Count += 1;
                mv.Score += 1;
                var now = DateTime.Now;
                var result = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
                mv.Last_Date = result.ToString("yyyy-MM-dd HH:mm:ss");

                QueueMoviePlaybackStatsPersist(
                    MainVM?.DbInfo?.DBFullPath ?? "",
                    mv.Movie_Id,
                    mv.Score,
                    mv.View_Count,
                    result
                );
            }
            catch (Exception err)
            {
                MessageBox.Show(
                    err.Message,
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                return;
            }
        }

        private static Task<bool> MoviePathExistsForPlaybackAsync(string movieFullPath)
        {
            if (string.IsNullOrWhiteSpace(movieFullPath))
            {
                return Task.FromResult(false);
            }

            // 再生入口の実ファイル確認は環境次第で詰まるため、UIスレッドから切り離す。
            return Task.Run(() => Path.Exists(movieFullPath));
        }

        private static Task<int> ResolveBookmarkPlaybackMillisecondsAsync(
            string movieFullPath,
            long bookmarkFrame
        )
        {
            return Task.Run(() => ResolveBookmarkPlaybackMilliseconds(movieFullPath, bookmarkFrame));
        }

        private static int ResolveBookmarkPlaybackMilliseconds(string movieFullPath, long bookmarkFrame)
        {
            if (string.IsNullOrWhiteSpace(movieFullPath))
            {
                return 0;
            }

            // Bookmark再生位置のFPS取得は動画メタを読むため、UIスレッドから切り離す。
            MovieInfo movieInfo = new(movieFullPath, true);
            int fps = Math.Max(1, (int)movieInfo.FPS);
            return (int)bookmarkFrame / fps * 1000;
        }

        // クリック座標に対するサムネイルシート解析はUI外へ逃がし、再生クリックの待ちを減らす。
        private Task<(int Milliseconds, int Position, bool HasPosition)> ResolveSelectedThumbnailPlaybackPositionAsync(
            int tabIndex,
            MovieRecords movie
        )
        {
            string thumbPath = ResolveMovieThumbnailPathByTabIndex(tabIndex, movie);
            if (string.IsNullOrWhiteSpace(thumbPath))
            {
                return Task.FromResult((0, 0, false));
            }

            System.Windows.Point clickPointSnapshot = lbClickPoint;
            return Task.Run(() =>
            {
                bool resolved = TryResolveThumbnailPlaybackPosition(
                    thumbPath,
                    clickPointSnapshot,
                    out int playbackMilliseconds,
                    out int position
                );
                return resolved
                    ? (playbackMilliseconds, position, true)
                    : (0, 0, false);
            });
        }

        // 再生開始後の軽い統計保存もDB I/Oなので、UIクリック処理から外す。
        private void QueueMoviePlaybackStatsPersist(
            string dbFullPath,
            long movieId,
            long score,
            long viewCount,
            DateTime lastDate
        )
        {
            if (string.IsNullOrWhiteSpace(dbFullPath) || movieId <= 0)
            {
                return;
            }

            PersistenceWriteRequest writeRequest = PersistenceWriteRequest.Create(
                PersistenceWriteKind.BackgroundDbWrite,
                "player-playback-stats",
                "main-db-playback-stats",
                retryable: true
            );
            _ = Task.Run(
                () =>
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    try
                    {
                        _mainDbMovieMutationFacade.UpdateScore(dbFullPath, movieId, score);
                        _mainDbMovieMutationFacade.UpdateViewCount(dbFullPath, movieId, viewCount);
                        _mainDbMovieMutationFacade.UpdateLastDate(dbFullPath, movieId, lastDate);
                        PersistenceWriteResult result = PersistenceWriteResult.FromSuccess(
                            writeRequest,
                            stopwatch.Elapsed
                        );
                        DebugRuntimeLog.Write(
                            "player",
                            $"playback stats persist succeeded: db='{dbFullPath}' movie_id={movieId} {result.LogFields}"
                        );
                    }
                    catch (Exception ex)
                    {
                        PersistenceWriteResult result = PersistenceWriteResult.FromFailure(
                            writeRequest,
                            stopwatch.Elapsed,
                            PersistenceFailureKind.BackgroundDbWrite
                        );
                        DebugRuntimeLog.Write(
                            "player",
                            $"playback stats persist failed: db='{dbFullPath}' movie_id={movieId} {result.LogFields} err='{ex.GetType().Name}'"
                        );
                    }
                }
            );
        }

        private static void QueueBookmarkViewCountUpdate(string dbFullPath, long movieId)
        {
            if (string.IsNullOrWhiteSpace(dbFullPath) || movieId <= 0)
            {
                return;
            }

            _ = Task.Run(
                () =>
                {
                    try
                    {
                        if (
                            TryUpdateBookmarkViewCount(
                                dbFullPath,
                                movieId,
                                out string failureReason
                            )
                        )
                        {
                            return;
                        }

                        BookmarkPersistenceState failureState =
                            BuildBookmarkPersistenceFailureState(
                                "view-count",
                                dbFullPath,
                                movieId,
                                "",
                                failureReason
                            );
                        DebugRuntimeLog.Write(
                            "player",
                            BuildBookmarkPersistenceFailureLog(failureState)
                        );
                    }
                    catch (Exception ex)
                    {
                        BookmarkPersistenceState failureState =
                            BuildBookmarkPersistenceFailureState(
                                "view-count",
                                dbFullPath,
                                movieId,
                                "",
                                ex.GetType().Name
                            );
                        DebugRuntimeLog.Write(
                            "player",
                            BuildBookmarkPersistenceFailureLog(failureState)
                        );
                    }
                }
            );
        }

        private int _isPlayerPlaybackActiveFlag;

        // 背後の poll loop からも読むため、再生中フラグは必ずこの入口で扱う。
        private bool IsPlayerPlaybackActive()
        {
            return Volatile.Read(ref _isPlayerPlaybackActiveFlag) != 0;
        }

        private string BuildPlayerPlaybackCoreRouteLogFields(bool isActive, string reason)
        {
            return $"core_route=player-playback "
                + $"operation_reason={UiOperationPriorityPolicy.OperationReasonPlayerPlayback} "
                + $"player_surface={ResolvePlayerPlaybackSurfaceLogValue()} "
                + $"player_surface_ready={FormatLogBool(IsPlayerPlaybackSurfaceReady())} "
                + $"active={FormatLogBool(isActive)} "
                + $"player_transition={ResolvePlayerPlaybackTransitionLogValue(isActive)} "
                + $"reason={reason ?? ""}";
        }

        private static string ResolvePlayerPlaybackTransitionLogValue(bool isActive)
        {
            return isActive ? "start" : "stop";
        }

        private string ResolvePlayerPlaybackSurfaceLogValue()
        {
            return "libvlc";
        }

        private bool IsPlayerPlaybackSurfaceReady()
        {
            return _isLibVlcPlayerInitialized && _playerMediaPlayer != null;
        }

        private void SetPlayerPlaybackActive(bool isActive, string reason = "")
        {
            int nextValue = isActive ? 1 : 0;
            int previousValue = Interlocked.Exchange(
                ref _isPlayerPlaybackActiveFlag,
                nextValue
            );
            UpdatePlayerPlayPauseUi(isActive);
            if (previousValue == nextValue)
            {
                return;
            }

            // 実際の遷移だけをログ化し、Everything poll の延期理由と Player 操作を同じ語彙で結ぶ。
            UiOperationSnapshot snapshot = CaptureUserPriorityOperationSnapshot(
                IsUserPriorityWorkActive(),
                isManualMode: false
            );
            DebugRuntimeLog.Write(
                "ui-priority",
                BuildUiShellInputLogMessage(
                    UiOperationPriorityPolicy.OperationReasonPlayerPlayback,
                    reason,
                    snapshot
                )
            );
            DebugRuntimeLog.Write(
                "player",
                $"player playback state changed: {BuildPlayerPlaybackCoreRouteLogFields(isActive, reason)}"
            );
        }

        private void UpdatePlayerPlayPauseUi(bool isPlaying)
        {
            if (PlayPauseGlyph != null)
            {
                PlayPauseGlyph.Text = isPlaying ? PlayerPauseGlyph : PlayerPlayGlyph;
            }

            if (PlayButton != null)
            {
                PlayButton.ToolTip = isPlaying ? "一時停止" : "再生";
            }
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (IsPlayerPlaybackActive())
            {
                Pause_Click(sender, e);
                return;
            }

            Start_Click(sender, e);
        }

        /// <summary>
        /// 動画再生の号砲。LibVLCの同じ再生実体を再開する。
        /// </summary>
        private void Start_Click(object sender, RoutedEventArgs e)
        {
            ShowPlayerSurface();
            StartLibVlcPlayback();
            uxTimeSlider.Value = GetPlayerPosition().TotalMilliseconds;
        }

        /// <summary>
        /// ちょい待ち！一時停止ボタンで時を止めるぜ！⏸️
        /// </summary>
        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            PauseLibVlcPlayback();
        }

        private void PlayerMuteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_playerMediaPlayer == null)
            {
                return;
            }

            SetPlayerEngineMuted(!IsPlayerEngineMuted());
            UpdatePlayerVolumeUi();
        }

        private void PlayerFullscreen_Click(object sender, RoutedEventArgs e)
        {
            _ = OpenMainWindowPlayerFullscreenAsync();
        }

        private void PlayerMoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (PlayerMoreButton?.ContextMenu == null)
            {
                return;
            }

            PlayerMoreButton.ContextMenu.PlacementTarget = PlayerMoreButton;
            PlayerMoreButton.ContextMenu.Placement =
                System.Windows.Controls.Primitives.PlacementMode.Top;
            PlayerMoreButton.ContextMenu.IsOpen = true;
        }

        private void UxVideoPlayer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (IsPlayerPlaybackActive())
            {
                PauseLibVlcPlayback();
            }
            else
            {
                StartLibVlcPlayback();
            }
        }

        /// <summary>
        /// 再生完全終了！ストップボタンでプレイヤーをサクッと隠し、裏方に下げるぜ！⏹️
        /// </summary>
        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            CloseManualPlayerOverlay();
        }

        /// <summary>
        /// タイムラインスライダーを動かしたな！指定の秒数へ動画のポジションを即座にワープさせるぜ！🚀
        /// </summary>
        private void UxTimeSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e
        )
        {
            if (_isTimeSliderSyncingFromPlayer)
            {
                return;
            }

            TimeSpan requestedPosition = TimeSpan.FromMilliseconds(uxTimeSlider.Value);
            UpdatePlayerTimeLabel(requestedPosition);

            DateTime now = DateTime.Now;
            TimeSpan timeSinceLastUpdate = now - _lastSliderTime;
            if (!_isTimeSliderDragging || timeSinceLastUpdate >= _timeSliderInterval)
            {
                SetPlayerPosition(requestedPosition);
                _lastSliderTime = now;
            }
        }

        internal static Size ResolveManualPlayerViewportSize(
            double availableWidth,
            double availableHeight,
            double naturalVideoWidth,
            double naturalVideoHeight,
            double preferredLandscapeWidth = ManualPlayerPreferredLandscapeWidth
        )
        {
            double safeAvailableWidth = Math.Max(0d, availableWidth);
            double safeAvailableHeight = Math.Max(0d, availableHeight);
            if (safeAvailableWidth <= 0d || safeAvailableHeight <= 0d)
            {
                return new Size(0d, 0d);
            }

            if (naturalVideoWidth <= 0d || naturalVideoHeight <= 0d)
            {
                double fallbackWidth = Math.Min(preferredLandscapeWidth, safeAvailableWidth);
                double fallbackHeight = Math.Min(safeAvailableHeight, fallbackWidth * 9d / 16d);
                return new Size(Math.Max(0d, fallbackWidth), Math.Max(0d, fallbackHeight));
            }

            double widthLimit = safeAvailableWidth;
            if (naturalVideoWidth >= naturalVideoHeight && preferredLandscapeWidth > 0d)
            {
                widthLimit = Math.Min(widthLimit, preferredLandscapeWidth);
            }

            double scale = Math.Min(
                widthLimit / naturalVideoWidth,
                safeAvailableHeight / naturalVideoHeight
            );
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0d)
            {
                return new Size(0d, 0d);
            }

            return new Size(
                Math.Max(0d, Math.Floor(naturalVideoWidth * scale)),
                Math.Max(0d, Math.Floor(naturalVideoHeight * scale))
            );
        }

        private void UpdateManualPlayerViewport()
        {
            if (uxVideoPlayer == null || PlayerArea == null)
            {
                return;
            }

            if (_isDetachedPlayerFullscreenActive)
            {
                SetPlayerElementSizeIfChanged(uxVideoPlayer, double.NaN, double.NaN);
                return;
            }

            double availableWidth;
            double availableHeight;
            bool hasPlayerTabViewport = TryGetPlayerTabViewportSize(
                out availableWidth,
                out availableHeight
            );
            if (!hasPlayerTabViewport)
            {
                availableWidth = Math.Max(0d, ActualWidth - ManualPlayerHorizontalPadding);
                availableHeight = Math.Max(
                    0d,
                    ActualHeight - ManualPlayerVerticalPadding
                );
            }
            // LibVLCのVideoViewをPlayer枠いっぱいに使い、縦横比と黒帯はエンジンへ任せる。
            Size viewportSize = hasPlayerTabViewport
                ? new Size(availableWidth, availableHeight)
                : ResolveManualPlayerViewportSize(
                    availableWidth,
                    availableHeight,
                    16d,
                    9d,
                    ManualPlayerPreferredLandscapeWidth
                );
            if (viewportSize.Width <= 0d || viewportSize.Height <= 0d)
            {
                return;
            }

            SetPlayerElementSizeIfChanged(uxVideoPlayer, viewportSize.Width, viewportSize.Height);
            SetPlayerElementSizeIfChanged(
                PlayerArea,
                viewportSize.Width,
                viewportSize.Height
            );
        }

        private static void SetPlayerElementSizeIfChanged(
            FrameworkElement element,
            double width,
            double height
        )
        {
            if (element == null)
            {
                return;
            }

            SetPlayerElementWidthIfChanged(element, width);
            SetPlayerElementHeightIfChanged(element, height);
        }

        private static void SetPlayerElementWidthIfChanged(FrameworkElement element, double width)
        {
            if (element == null || ArePlayerLayoutLengthsEqual(element.Width, width))
            {
                return;
            }

            // 同じサイズの再設定を避け、Player user-priority 後の余計な measure を抑える。
            element.Width = width;
        }

        private static void SetPlayerElementHeightIfChanged(FrameworkElement element, double height)
        {
            if (element == null || ArePlayerLayoutLengthsEqual(element.Height, height))
            {
                return;
            }

            element.Height = height;
        }

        private static bool ArePlayerLayoutLengthsEqual(double current, double next)
        {
            if (double.IsNaN(current) && double.IsNaN(next))
            {
                return true;
            }

            return Math.Abs(current - next) < 0.0001d;
        }

        private void EnsureManualPlayerResizeTrackingHooked()
        {
            if (_isManualPlayerResizeTrackingHooked)
            {
                return;
            }

            SizeChanged += ManualPlayerHost_SizeChanged;
            _isManualPlayerResizeTrackingHooked = true;
        }

        private void ManualPlayerHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (PlayerArea?.Visibility != Visibility.Visible)
            {
                return;
            }

            UpdateManualPlayerViewport();
        }

        private void CloseManualPlayerOverlay(bool invalidateOpenRequest = true)
        {
            if (invalidateOpenRequest)
            {
                // 停止後に初期化待ちの古い動画が再び開かないよう、明示終了でopen要求を無効化する。
                ++_playerOpenRequestRevision;
            }

            _ = ForceCloseMainWindowPlayerFullscreenAsync();
            PlayerArea.Visibility = Visibility.Collapsed;
            PlayerController.Visibility = Visibility.Collapsed;
            uxVideoPlayer.Visibility = Visibility.Collapsed;
            StopLibVlcPlayback();
            _hasPendingPlayerPlaybackRequest = false;
            _currentPlayerMoviePath = "";
            ReleasePendingPlayerUserPriorityWork("player-close");
            SetPlayerPlaybackActive(false, "close-overlay");
            uxTimeSlider.Value = 0;
            uxTimeSlider.Maximum = 0;
            uxTime.Text = "0:00 / 0:00";
            StopDispatcherTimerSafely(timer, nameof(timer));
            ShowPlayerEmptyState();
        }

        private bool TryHandleManualPlayerShortcut(KeyEventArgs e)
        {
            if (TryHandleMainWindowPlayerFullscreenShortcut(e))
            {
                return true;
            }

            if (
                e == null
                || e.Key != Key.Escape
                || PlayerArea?.Visibility != Visibility.Visible
            )
            {
                return false;
            }

            CloseManualPlayerOverlay();
            e.Handled = true;
            return true;
        }

        /// <summary>
        /// ボリュームスライダー調整！音量もテンションも、今の気分に合わせて自由自在だ！🔊
        /// </summary>
        private void UxVolumeSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e
        )
        {
            if (!_isPlayerVolumeInitialized || _isPlayerVolumeApplyingToUi)
            {
                return;
            }

            SetPlayerVolumeFromUser(uxVolumeSlider.Value);
        }

        /// <summary>
        /// 最高の瞬間を切り取れ！キャプチャボタンで現在のフレームをバシッとサムネイル化するぜ！📸✨
        /// </summary>
        private async void Capture_Click(object sender, RoutedEventArgs e)
        {
            //QueueObj 作って、サムネ作成する。どのパネルか、秒数はどこか、差し替える画像はどれか。
            //その辺は、サムネ作成側の処理で判断。

            if (Tabs.SelectedItem == null)
            {
                return;
            }

            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null)
            {
                return;
            }

            StopDispatcherTimerSafely(timer, nameof(timer));
            PauseLibVlcPlayback();

            QueueObj queueObj = new()
            {
                MovieId = mv.Movie_Id,
                MovieFullPath = mv.Movie_Path,
                Hash = mv.Hash,
                Tabindex = GetCurrentThumbnailActionTabIndex(),
                ThumbPanelPos = manualPos,
                ThumbTimePos = (int)GetPlayerPosition().TotalSeconds,
            };
            CloseManualPlayerOverlay();

            try
            {
                await Task.Delay(10);
                await CreateThumbAsync(queueObj, true, default);
            }
            catch (Exception ex)
            {
                string message = ResolveManualThumbnailCaptureFailureMessage(ex);
                DebugRuntimeLog.Write(
                    "thumbnail",
                    $"manual capture failed: movie='{queueObj.MovieFullPath}', tab={queueObj.Tabindex}, reason='{message}'"
                );
                MessageBox.Show(
                    message,
                    "サムネイル取得に失敗",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
        }

        // manual 取得失敗は英語の内部理由を、そのままではなく操作に沿った文面へ寄せる。
        internal static string ResolveManualThumbnailCaptureFailureMessage(Exception ex)
        {
            string rawReason = ex switch
            {
                ThumbnailCreateFailureException failureEx
                    when !string.IsNullOrWhiteSpace(failureEx.FailureReason) =>
                    failureEx.FailureReason,
                _ => ex?.Message ?? "",
            };

            if (
                string.Equals(
                    rawReason,
                    "manual target thumbnail does not exist",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return "手動サムネイル取得は既存サムネイルの差し替えです。先に通常のサムネイルを作成してください。";
            }

            if (
                string.Equals(
                    rawReason,
                    "manual source thumbnail metadata is missing",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return "既存サムネイルの情報を読めないため、手動サムネイル取得を続行できませんでした。通常サムネイルを再作成してからやり直してください。";
            }

            if (ex is TimeoutException)
            {
                return "サムネイル取得が時間内に完了しませんでした。動画が重い可能性があります。";
            }

            if (!string.IsNullOrWhiteSpace(rawReason))
            {
                return $"手動サムネイル取得に失敗しました。\n{rawReason}";
            }

            return "手動サムネイル取得に失敗しました。";
        }

        private async void ManualThumbnail_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null)
            {
                ShowThumbnailUserActionPopup(
                    "マニュアルサムネイル作成",
                    "対象タブを選択してから実行してください。",
                    MessageBoxImage.Warning
                );
                return;
            }

            MovieRecords mv = ResolveSelectedMovieRecordForThumbnailUserAction(sender);
            if (mv == null)
            {
                ShowThumbnailUserActionPopup(
                    "マニュアルサムネイル作成",
                    "対象動画が選択されていません。",
                    MessageBoxImage.Warning
                );
                return;
            }

            EnsureManualPlayerResizeTrackingHooked();

            int msec = 0;
            if (sender is MenuItem senderObj)
            {
                if (senderObj.Name == "ManualThumbnail")
                {
                    var playbackPosition = await ResolveSelectedThumbnailPlaybackPositionAsync(
                        GetCurrentThumbnailActionTabIndex(),
                        mv
                    );
                    if (playbackPosition.HasPosition)
                    {
                        msec = playbackPosition.Milliseconds;
                        manualPos = playbackPosition.Position;
                    }
                }
            }

            await OpenMovieInPlayerTabAsync(
                mv,
                msec,
                playImmediately: false,
                mute: true,
                focusTimeSlider: true
            );
        }

        private void FR_Click(object sender, RoutedEventArgs e)
        {
            var tempSlider = (int)uxTimeSlider.Value - 100;
            if (tempSlider < 0)
            {
                tempSlider = 0;
            }
            FF_FR(tempSlider);
        }

        private void FF_Click(object sender, RoutedEventArgs e)
        {
            var tempSlider = (int)uxTimeSlider.Value + 100;
            if (tempSlider > uxTimeSlider.Maximum)
            {
                tempSlider = (int)uxTimeSlider.Maximum;
            }
            FF_FR(tempSlider);
        }

        private void FF_FR(int tempSlider)
        {
            uxTimeSlider.Value = tempSlider;
            TimeSpan nextPosition = TimeSpan.FromMilliseconds(tempSlider);
            SetPlayerPosition(nextPosition);
            UpdatePlayerPositionUi(nextPosition);
        }

        /// <summary>
        /// ユーザーがスライダーを掴んでいない時は、動画の再生位置に合わせてスライダーを自動で追従させる滑らか処理！🏄‍♂️
        /// </summary>
        private void Timer_Tick(object sender, EventArgs e)
        {
            if (!_isTimeSliderDragging)
            {
                _isTimeSliderSyncingFromPlayer = true;
                try
                {
                    TimeSpan position = GetPlayerPosition();
                    uxTimeSlider.Value = position.TotalMilliseconds;
                    UpdatePlayerPositionUi(position);
                }
                finally
                {
                    _isTimeSliderSyncingFromPlayer = false;
                }
            }
        }

        private void UxTimeSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isTimeSliderDragging = true;
        }

        private void UxTimeSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            CommitTimeSliderSeek();
        }

        private void UxTimeSlider_LostMouseCapture(object sender, MouseEventArgs e)
        {
            CommitTimeSliderSeek();
        }

        // スライダー操作の確定時だけ再生位置へ反映し、相互更新ループを防ぐ。
        private void CommitTimeSliderSeek()
        {
            if (!_isTimeSliderDragging)
            {
                return;
            }

            _isTimeSliderDragging = false;

            TimeSpan nextPosition = TimeSpan.FromMilliseconds(uxTimeSlider.Value);
            SetPlayerPosition(nextPosition);
            UpdatePlayerPositionUi(nextPosition);
            _lastSliderTime = DateTime.Now;
        }
    }
}
