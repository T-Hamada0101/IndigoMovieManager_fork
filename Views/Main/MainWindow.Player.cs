using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
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
        private const double ManualPlayerFallbackControllerHeight = 72d;
        private const double DefaultPlayerVolume = 0.5d;
        private bool _isTimeSliderSyncingFromPlayer;
        private bool _isTimeSliderDragging;
        private bool _isManualPlayerResizeTrackingHooked;
        private bool _isPlayerVolumeApplyingToUi;
        private double _currentPlayerVolume = DefaultPlayerVolume;
        private DispatcherTimer _playerVolumeSaveDebounceTimer;

        // 保存済み設定が壊れていても、音量は常に 0.0 から 1.0 の安全域へ戻す。
        private static double ClampPlayerVolumeSetting(double volume)
        {
            if (double.IsNaN(volume) || double.IsInfinity(volume))
            {
                return DefaultPlayerVolume;
            }

            return Math.Max(0d, Math.Min(1d, volume));
        }

        // WebView2 の既定値 100% が保存に混ざった時は、次回起動で既定の 50% へ戻す。
        private static double ResolveSavedPlayerVolumeSetting(double volume)
        {
            double resolvedVolume = ClampPlayerVolumeSetting(volume);
            return resolvedVolume >= 1d ? DefaultPlayerVolume : resolvedVolume;
        }

        // 起動時の復元も中央入口へ通し、保存値・UI・プレイヤーの正本を分散させない。
        private void RestorePlayerVolumeFromSettings()
        {
            double rawSavedVolume = Properties.Settings.Default.PlayerVolume;
            double savedPlayerVolume = ResolveSavedPlayerVolumeSetting(rawSavedVolume);
            bool repairSavedVolume =
                Math.Abs(ClampPlayerVolumeSetting(rawSavedVolume) - savedPlayerVolume) > 0.0001d;

            ApplyPlayerVolumeSetting(
                savedPlayerVolume,
                updateSlider: true,
                save: repairSavedVolume,
                pushToWebView: false
            );
        }

        // 他ファイルから音量を参照する時も、スライダー値ではなくこの正本を使う。
        private double GetCurrentPlayerVolumeSetting()
        {
            return ClampPlayerVolumeSetting(_currentPlayerVolume);
        }

        // 画面表示・保存・WebView2反映をここへ集約し、動画切り替えごとの音量ぶれを防ぐ。
        private void ApplyPlayerVolumeSetting(
            double volume,
            bool updateSlider,
            bool save,
            bool pushToWebView
        )
        {
            double resolvedVolume = ClampPlayerVolumeSetting(volume);
            _currentPlayerVolume = resolvedVolume;

            if (uxVideoPlayer != null)
            {
                uxVideoPlayer.Volume = resolvedVolume;
            }

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

            // 保存が必要な入口だけここで畳み、設定ファイルへ直接触る場所を増やさない。
            if (save && Math.Abs(Properties.Settings.Default.PlayerVolume - resolvedVolume) > 0.0001d)
            {
                Properties.Settings.Default.PlayerVolume = resolvedVolume;
                QueuePlayerVolumeSettingSave();
            }

            if (!pushToWebView || uxWebVideoPlayer?.CoreWebView2 == null)
            {
                return;
            }

            _ = uxWebVideoPlayer.ExecuteScriptAsync(
                $"const player = document.querySelector('video'); if (player) {{ player.dataset.indigoPlayerHostVolumeApplying = '1'; player.muted = false; player.volume = {resolvedVolume.ToString(System.Globalization.CultureInfo.InvariantCulture)}; player.dataset.indigoPlayerHostVolumeApplied = '1'; setTimeout(() => {{ delete player.dataset.indigoPlayerHostVolumeApplying; }}, 250); }}"
            );
        }

        // ユーザー操作は保存し、現在のWebView2プレイヤーにも即時反映する。
        private void SetPlayerVolumeFromUser(double volume)
        {
            ApplyPlayerVolumeSetting(
                volume,
                updateSlider: true,
                save: true,
                pushToWebView: _isWebViewPlayerActive
            );
        }

        // 新しい video 要素ができた時は、保存せずに現在の正本音量だけをWebView2へ注入する。
        private void PushCurrentPlayerVolumeToWebView()
        {
            ApplyPlayerVolumeSetting(
                GetCurrentPlayerVolumeSetting(),
                updateSlider: false,
                save: false,
                pushToWebView: true
            );
        }

        // WebView2の100%既定値が逆流した時は、正本を守って50%または現在値へ戻す。
        private void SetPlayerVolumeFromWebView(double volume)
        {
            double resolvedVolume = ClampPlayerVolumeSetting(volume);
            if (resolvedVolume >= 1d)
            {
                double currentVolume = GetCurrentPlayerVolumeSetting();
                double fallbackVolume = currentVolume >= 1d ? DefaultPlayerVolume : currentVolume;
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"player webview default volume ignored: incoming={resolvedVolume:0.###} current={currentVolume:0.###} fallback={fallbackVolume:0.###}"
                );

                ApplyPlayerVolumeSetting(
                    fallbackVolume,
                    updateSlider: true,
                    save: true,
                    pushToWebView: true
                );
                return;
            }

            ApplyPlayerVolumeSetting(
                resolvedVolume,
                updateSlider: true,
                save: true,
                pushToWebView: false
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
            Properties.Settings.Default.Save();
        }

        // WebView2 のネイティブ音量変更も設定へ戻し、以後の全動画へ同じ値を配る。
        private void SyncPlayerVolumeFromWebView(double volume)
        {
            SetPlayerVolumeFromWebView(volume);
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
            int secPos = 0; //ここでは渡す為だけに使ってる。
            string moviePath = "";
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

                moviePath = $"\"{mv.Movie_Path}\"";

                if (!Path.Exists(mv.Movie_Path))
                {
                    return;
                }

                if (sender is MenuItem senderObj)
                {
                    if (senderObj.Name == "PlayFromThumb")
                    {
                        msec = GetPlayPosition(GetCurrentThumbnailActionTabIndex(), mv, ref secPos);
                    }
                }
            }

            if (!string.IsNullOrEmpty(playerParam))
            {
                playerParam = playerParam.Replace("<file>", $"{mv.Movie_Path}");
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

            _ = Task.Run(
                () =>
                {
                    try
                    {
                        _mainDbMovieMutationFacade.UpdateScore(dbFullPath, movieId, score);
                        _mainDbMovieMutationFacade.UpdateViewCount(dbFullPath, movieId, viewCount);
                        _mainDbMovieMutationFacade.UpdateLastDate(dbFullPath, movieId, lastDate);
                    }
                    catch (Exception ex)
                    {
                        DebugRuntimeLog.Write(
                            "player",
                            $"playback stats persist failed: db='{dbFullPath}' movie_id={movieId} err='{ex.GetType().Name}'"
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
                        UpdateBookmarkViewCount(dbFullPath, movieId);
                    }
                    catch (Exception ex)
                    {
                        DebugRuntimeLog.Write(
                            "player",
                            $"bookmark view count persist failed: db='{dbFullPath}' movie_id={movieId} err='{ex.GetType().Name}'"
                        );
                    }
                }
            );
        }

        private bool IsPlaying = false;

        // 背後の poll loop からも読むため、再生中フラグは必ずこの入口で扱う。
        private bool IsPlayerPlaybackActive()
        {
            return Volatile.Read(ref IsPlaying);
        }

        private void SetPlayerPlaybackActive(bool isActive)
        {
            Volatile.Write(ref IsPlaying, isActive);
        }

        /// <summary>
        /// 動画再生の号砲！プレイヤーを呼び覚まし、熱い映像体験をスタートさせるぜ！▶️✨
        /// （ありがとう先人の知恵：https://resanaplaza.com/2023/06/24/%e3%80%90...MediaElement）
        /// </summary>
        private void Start_Click(object sender, RoutedEventArgs e)
        {
            if (_isWebViewPlayerActive)
            {
                SetPlayerPlaybackActive(true);
                _ = uxWebVideoPlayer?.ExecuteScriptAsync(
                    "document.querySelector('video')?.play();"
                );
                return;
            }

            ShowPlayerSurface();
            uxVideoPlayer.Play();
            SetPlayerPlaybackActive(true);
            uxTimeSlider.Value = uxVideoPlayer.Position.TotalMilliseconds;
            TryStartDispatcherTimer(timer, nameof(timer));
        }

        /// <summary>
        /// ちょい待ち！一時停止ボタンで時を止めるぜ！⏸️
        /// </summary>
        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            if (_isWebViewPlayerActive)
            {
                _ = PauseWebViewPlayerAsync();
                return;
            }

            uxVideoPlayer.Pause();
            SetPlayerPlaybackActive(false);
        }

        private void UxVideoPlayer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (IsPlayerPlaybackActive())
            {
                uxVideoPlayer.Pause();
                SetPlayerPlaybackActive(false);
            }
            else
            {
                uxVideoPlayer.Play();
                SetPlayerPlaybackActive(true);
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

            DateTime now = DateTime.Now;
            TimeSpan timeSinceLastUpdate = now - _lastSliderTime;

            if (timeSinceLastUpdate >= _timeSliderInterval)
            {
                uxVideoPlayer.Position = TimeSpan.FromMilliseconds(uxTimeSlider.Value);
                _lastSliderTime = now;
                UpdatePlayerPositionUi(uxVideoPlayer.Position);
            }
        }

        /// <summary>
        /// 動画ファイルのロード完了！再生時間の最大値をスライダーにガツンとセットするぜ！🎞️
        /// </summary>
        private void UxVideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            // duration 未確定の動画でも落とさず、既知の最大値だけを安全に反映する。
            uxTimeSlider.Maximum = ResolveMediaDurationMaximumMilliseconds(
                uxVideoPlayer.NaturalDuration,
                uxTimeSlider.Maximum
            );
            ShowPlayerSurface();
            UpdateManualPlayerViewport();
            _ = ApplyPendingPlayerPlaybackRequestAsync();
        }

        private void UxVideoPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            // ロード失敗時も user-priority を解放し、背後監視を永久停止させない。
            SetPlayerPlaybackActive(false);
            _hasPendingPlayerPlaybackRequest = false;
            ReleasePendingPlayerUserPriorityWork();
            StopDispatcherTimerSafely(timer, nameof(timer));
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"player media load failed: {e?.ErrorException?.Message ?? "unknown"}"
            );
        }

        private void UxVideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            // 再生終了後は poll の再生中扱いを解除し、通常の監視間隔へ戻す。
            SetPlayerPlaybackActive(false);
            StopDispatcherTimerSafely(timer, nameof(timer));
            UpdatePlayerPositionUi(uxVideoPlayer.Position);
        }

        internal static double ResolveMediaDurationMaximumMilliseconds(
            Duration naturalDuration,
            double fallbackMaximum
        )
        {
            if (naturalDuration.HasTimeSpan)
            {
                return Math.Max(0d, naturalDuration.TimeSpan.TotalMilliseconds);
            }

            if (double.IsNaN(fallbackMaximum) || double.IsInfinity(fallbackMaximum))
            {
                return 0d;
            }

            return Math.Max(0d, fallbackMaximum);
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
            if (uxVideoPlayer == null || PlayerArea == null || PlayerController == null)
            {
                return;
            }

            if (_isDetachedPlayerFullscreenActive)
            {
                if (uxWebVideoPlayer != null)
                {
                    // 全画面中は専用Window側で全面ストレッチさせ、MainWindow側サイズは持ち込まない。
                    SetPlayerElementSizeIfChanged(uxWebVideoPlayer, double.NaN, double.NaN);
                }

                return;
            }

            double controllerHeight = PlayerController.Visibility == Visibility.Visible
                ? (
                    PlayerController.ActualHeight > 1d
                        ? PlayerController.ActualHeight
                        : ManualPlayerFallbackControllerHeight
                )
                : 0d;
            double availableWidth;
            double availableHeight;
            double preferredLandscapeWidth = ManualPlayerPreferredLandscapeWidth;
            if (!TryGetPlayerTabViewportSize(out availableWidth, out availableHeight))
            {
                availableWidth = Math.Max(0d, ActualWidth - ManualPlayerHorizontalPadding);
                availableHeight = Math.Max(
                    0d,
                    ActualHeight - ManualPlayerVerticalPadding - controllerHeight
                );
            }
            else
            {
                availableHeight = Math.Max(0d, availableHeight - controllerHeight);
                preferredLandscapeWidth = 0d;
            }

            if (_isWebViewPlayerActive)
            {
                // WebView2 はプレイヤー枠いっぱいに広げ、内側余白だけを残して使い切る。
                SetPlayerElementSizeIfChanged(uxWebVideoPlayer, availableWidth, availableHeight);
                SetPlayerElementSizeIfChanged(
                    PlayerArea,
                    availableWidth,
                    availableHeight + controllerHeight
                );
                SetPlayerElementWidthIfChanged(PlayerController, availableWidth);
                return;
            }

            Size viewportSize = ResolveManualPlayerViewportSize(
                availableWidth,
                availableHeight,
                uxVideoPlayer.NaturalVideoWidth,
                uxVideoPlayer.NaturalVideoHeight,
                preferredLandscapeWidth
            );
            if (viewportSize.Width <= 0d || viewportSize.Height <= 0d)
            {
                return;
            }

            // 動画面と操作バーの横幅を揃え、縦動画でも画面内へ収める。
            SetPlayerElementSizeIfChanged(uxVideoPlayer, viewportSize.Width, viewportSize.Height);
            if (uxWebVideoPlayer != null)
            {
                SetPlayerElementSizeIfChanged(
                    uxWebVideoPlayer,
                    viewportSize.Width,
                    viewportSize.Height
                );
            }
            SetPlayerElementSizeIfChanged(
                PlayerArea,
                viewportSize.Width,
                viewportSize.Height + controllerHeight
            );
            SetPlayerElementWidthIfChanged(PlayerController, viewportSize.Width);
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

        private void CloseManualPlayerOverlay()
        {
            _ = ForceCloseMainWindowPlayerFullscreenAsync();
            PlayerArea.Visibility = Visibility.Collapsed;
            PlayerController.Visibility = Visibility.Collapsed;
            uxVideoPlayer.Visibility = Visibility.Collapsed;
            uxVideoPlayer.Stop();
            uxVideoPlayer.Source = null;
            _hasPendingPlayerPlaybackRequest = false;
            _currentPlayerMoviePath = "";
            ResetWebViewPlayerSurface();
            SetPlayerPlaybackActive(false);
            uxTimeSlider.Value = 0;
            uxTimeSlider.Maximum = 0;
            uxTime.Text = "00:00:00";
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
            if (_isPlayerVolumeApplyingToUi)
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
            uxVideoPlayer.Pause();

            QueueObj queueObj = new()
            {
                MovieId = mv.Movie_Id,
                MovieFullPath = mv.Movie_Path,
                Hash = mv.Hash,
                Tabindex = GetCurrentThumbnailActionTabIndex(),
                ThumbPanelPos = manualPos,
                ThumbTimePos = (int)uxVideoPlayer.Position.TotalSeconds,
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
                    msec = GetPlayPosition(GetCurrentThumbnailActionTabIndex(), mv, ref manualPos);
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
            uxVideoPlayer.Position = new TimeSpan(0, 0, 0, 0, tempSlider);
            UpdatePlayerPositionUi(uxVideoPlayer.Position);
        }

        /// <summary>
        /// ユーザーがスライダーを掴んでいない時は、動画の再生位置に合わせてスライダーを自動で追従させる滑らか処理！🏄‍♂️
        /// </summary>
        private void Timer_Tick(object sender, EventArgs e)
        {
            if (_isWebViewPlayerActive)
            {
                return;
            }

            if (!_isTimeSliderDragging)
            {
                _isTimeSliderSyncingFromPlayer = true;
                try
                {
                    uxTimeSlider.Value = uxVideoPlayer.Position.TotalMilliseconds;
                    UpdatePlayerPositionUi(uxVideoPlayer.Position);
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

            if (_isWebViewPlayerActive)
            {
                _lastSliderTime = DateTime.Now;
                return;
            }

            TimeSpan nextPosition = TimeSpan.FromMilliseconds(uxTimeSlider.Value);
            uxVideoPlayer.Position = nextPosition;
            UpdatePlayerPositionUi(nextPosition);
            _lastSliderTime = DateTime.Now;
        }
    }
}
