using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ManualPlayerResizeHookPolicyTests
{
    [Test]
    public void 再生中動画は削除リストへ保留し別動画選択後にPlayer解放して削除する()
    {
        string playerTabSource = GetRepoText(
            "UpperTabs",
            "Player",
            "MainWindow.UpperTabs.PlayerTab.cs"
        );
        string libVlcSource = GetRepoText(
            "UpperTabs",
            "Player",
            "MainWindow.UpperTabs.LibVlcPlayer.cs"
        );
        string deleteSource = GetRepoText("Views", "Main", "MainWindow.MenuActions.cs");
        string deferMethod = GetMethodBlock(
            deleteSource,
            "private bool TryDeferCurrentPlayerMovieDeletesUntilNextSelection("
        );
        string pendingQueueMethod = GetMethodBlock(
            deleteSource,
            "private void QueuePendingPlayerMovieDeletesAfterPlayerRelease("
        );
        string switchMethod = GetMethodBlock(
            playerTabSource,
            "private bool TryReleasePendingPlayerMovieDeletesForMovieSwitch("
        );
        string openMethod = GetMethodBlock(playerTabSource, "private async Task OpenMovieInPlayerTabAsync(");
        string releaseMethod = GetMethodBlock(
            libVlcSource,
            "private bool TryReleaseCurrentLibVlcMediaForDelete("
        );
        string deleteMethod = GetMethodBlock(
            deleteSource,
            "private void ExecuteDeleteAction("
        );

        Assert.Multiple(() =>
        {
            Assert.That(deferMethod, Does.Contain("PartitionCurrentPlayerMovieDeleteRecords("));
            Assert.That(deferMethod, Does.Contain("_pendingPlayerMovieDeleteRequests.Add(request);"));
            Assert.That(deferMethod, Does.Contain("ShowPendingPlayerMovieDeleteToast("));
            Assert.That(switchMethod, Does.Contain("CloseManualPlayerOverlay();"));
            Assert.That(switchMethod, Does.Contain("TryReleaseCurrentLibVlcMediaForDelete("));
            Assert.That(
                switchMethod,
                Does.Contain("QueuePendingPlayerMovieDeletesAfterPlayerRelease();")
            );
            Assert.That(pendingQueueMethod, Does.Contain("QueueConfirmedMovieDeleteSnapshots("));
            Assert.That(releaseMethod, Does.Contain("_playerMediaPlayer.Media = null;"));
            Assert.That(
                deleteMethod,
                Does.Contain("TryDeferCurrentPlayerMovieDeletesUntilNextSelection(")
            );
            Assert.That(
                openMethod.IndexOf(
                    "TryReleasePendingPlayerMovieDeletesForMovieSwitch(",
                    StringComparison.Ordinal
                ),
                Is.LessThan(openMethod.IndexOf("BeginUserPriorityWork(\"player\")", StringComparison.Ordinal))
            );
        });
    }

    [Test]
    public void EnsureManualPlayerResizeTrackingHooked_未登録時だけフックする()
    {
        // 変更後の実装方針は、二重登録を防ぎつつ1回だけ SizeChanged を拾うこと。
        string source = GetMainWindowPlayerSourceText();

        Assert.That(source, Does.Contain("private void EnsureManualPlayerResizeTrackingHooked()"));
        Assert.That(source, Does.Contain("if (_isManualPlayerResizeTrackingHooked)"));
        Assert.That(source, Does.Contain("SizeChanged += ManualPlayerHost_SizeChanged;"));
    }

    [Test]
    public void ManualPlayerHost_SizeChanged_表示中のみviewport更新する()
    {
        // 再生オーバーレイが隠れている時は不要な再計算を避ける契約を保持する。
        string source = GetMainWindowPlayerSourceText();

        Assert.That(
            source,
            Does.Contain("if (PlayerArea?.Visibility != Visibility.Visible)")
        );
        Assert.That(source, Does.Contain("UpdateManualPlayerViewport();"));
    }

    [Test]
    public void ApplyPlayerVolumeSetting_保存はdebounce経由で畳む()
    {
        string source = GetMainWindowPlayerSourceText();

        Assert.That(source, Does.Contain("private DispatcherTimer _playerVolumeSaveDebounceTimer;"));
        Assert.That(source, Does.Contain("private Task _playerVolumeSettingsSaveTask = Task.CompletedTask;"));
        Assert.That(source, Does.Contain("private void QueuePlayerVolumeSettingSave()"));
        Assert.That(source, Does.Contain("private void PlayerVolumeSaveDebounceTimer_Tick("));
        Assert.That(source, Does.Contain("QueuePlayerVolumeSettingSaveInBackground();"));
        Assert.That(source, Does.Contain("WaitForPlayerVolumeSettingSaveForShutdown("));
        Assert.That(source, Does.Contain("TaskScheduler.Default"));
        Assert.That(
            source,
            Does.Contain("Math.Abs(Properties.Settings.Default.PlayerVolume - resolvedVolume) > 0.0001d")
        );
        Assert.That(source, Does.Contain("QueuePlayerVolumeSettingSave();"));
    }

    [Test]
    public void PlayerVolume_初期値25パーセントと保存値100パーセントを両立する()
    {
        string playerSource = GetMainWindowPlayerSourceText();
        string windowSource = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string windowXamlSource = GetRepoText("Views", "Main", "MainWindow.xaml");
        string policySource = GetRepoText("Views", "Main", "PlayerVolumePolicy.cs");
        string settingsSource = GetRepoText("Properties", "Settings.settings");
        string settingsDesignerSource = GetRepoText("Properties", "Settings.Designer.cs");
        string libVlcSource = GetLibVlcPlayerSourceText();

        Assert.That(policySource, Does.Contain("internal const double DefaultVolume = 0.25d;"));
        Assert.That(playerSource, Does.Contain("PlayerVolumePolicy.Normalize(rawSavedVolume)"));
        Assert.That(playerSource, Does.Contain("PlayerVolumePolicy.RequiresRepair(rawSavedVolume)"));
        Assert.That(playerSource, Does.Not.Contain("ResolveSavedPlayerVolumeSetting"));
        Assert.That(playerSource, Does.Contain("private void RestorePlayerVolumeFromSettings()"));
        Assert.That(playerSource, Does.Contain("save: repairSavedVolume"));
        Assert.That(windowXamlSource, Does.Contain("x:Name=\"uxVolumeSlider\""));
        Assert.That(windowXamlSource, Does.Contain("Value=\"0.25\""));
        Assert.That(settingsSource, Does.Contain("<Setting Name=\"PlayerVolume\" Type=\"System.Double\" Scope=\"User\">"));
        Assert.That(settingsSource, Does.Contain("<Value Profile=\"(Default)\">0.25</Value>"));
        Assert.That(settingsDesignerSource, Does.Contain("DefaultSettingValueAttribute(\"0.25\")"));
        Assert.That(libVlcSource, Does.Contain("ApplyCurrentPlayerVolumeToEngine();"));
        Assert.That(libVlcSource, Does.Contain("_playerMediaPlayer.Volume ="));
        Assert.That(
            windowSource,
            Does.Contain("RestorePlayerVolumeFromSettings();")
        );
    }

    [Test]
    public void PlayerVolume_保存値復元前のXaml初期イベントを無視する()
    {
        string playerSource = GetMainWindowPlayerSourceText();
        string restoreMethod = GetMethodBlock(
            playerSource,
            "private void RestorePlayerVolumeFromSettings()"
        );
        string sliderMethod = GetMethodBlock(
            playerSource,
            "private void UxVolumeSlider_ValueChanged("
        );

        Assert.Multiple(() =>
        {
            Assert.That(playerSource, Does.Contain("private bool _isPlayerVolumeInitialized;"));
            Assert.That(restoreMethod, Does.Contain("_isPlayerVolumeInitialized = true;"));
            Assert.That(
                sliderMethod,
                Does.Contain("if (!_isPlayerVolumeInitialized || _isPlayerVolumeApplyingToUi)")
            );
            Assert.That(sliderMethod, Does.Contain("SetPlayerVolumeFromUser(uxVolumeSlider.Value);"));
        });
    }

    [TestCase(0d, 0d, "0:00 / 0:00")]
    [TestCase(65000d, 12703000d, "1:05 / 3:31:43")]
    [TestCase(90061000d, 90061000d, "25:01:01 / 25:01:01")]
    public void BuildPlayerTimeLabel_HTML5プレイヤーと同じ現在総時間表示にする(
        double positionMilliseconds,
        double durationMilliseconds,
        string expected
    )
    {
        string actual = MainWindow.BuildPlayerTimeLabel(
            TimeSpan.FromMilliseconds(positionMilliseconds),
            durationMilliseconds
        );

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void LibVLC操作面は既存オーバーレイと同じVideoViewを全画面でも使う()
    {
        string windowXaml = GetRepoText("Views", "Main", "MainWindow.xaml");
        string windowSource = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string playerSource = GetMainWindowPlayerSourceText();
        string fullscreenSource = GetUpperTabPlayerFullscreenWindowSourceText();

        Assert.Multiple(() =>
        {
            Assert.That(windowXaml, Does.Contain("x:Name=\"PlayerController\""));
            Assert.That(windowXaml, Does.Contain("VerticalAlignment=\"Bottom\""));
            Assert.That(windowXaml, Does.Contain("Background=\"#CC000000\""));
            Assert.That(windowXaml, Does.Contain("Click=\"PlayPause_Click\""));
            Assert.That(windowXaml, Does.Contain("IsMoveToPointEnabled=\"True\""));
            Assert.That(windowXaml, Does.Contain("<vlc:VideoView"));
            Assert.That(windowXaml, Does.Contain("Click=\"PlayerMuteButton_Click\""));
            Assert.That(windowXaml, Does.Contain("Click=\"PlayerFullscreen_Click\""));
            Assert.That(windowXaml, Does.Contain("AutomationProperties.Name=\"全画面\""));
            Assert.That(windowXaml, Does.Contain("x:Name=\"PlayerMoreButton\""));
            Assert.That(windowXaml, Does.Contain("Click=\"Capture_Click\""));
            Assert.That(windowXaml, Does.Contain("Click=\"AddBookmark_Click\""));
            Assert.That(
                windowSource,
                Does.Contain("new DispatcherTimer(DispatcherPriority.Background)")
            );
            Assert.That(windowSource, Does.Contain("TimeSpan.FromMilliseconds(250)"));
            Assert.That(playerSource, Does.Contain("private void PlayPause_Click("));
            Assert.That(playerSource, Does.Contain("UpdatePlayerTimeLabel(requestedPosition);"));
            Assert.That(
                fullscreenSource,
                Does.Contain("public void ShowPlayer(VideoView player)")
            );
            Assert.That(
                fullscreenSource,
                Does.Contain("private Task OpenMainWindowPlayerFullscreenAsync()")
            );
            Assert.That(
                fullscreenSource,
                Does.Contain("window.ShowPlayer(uxVideoPlayer);")
            );
            Assert.That(windowXaml, Does.Not.Contain("<MediaElement"));
            Assert.That(windowXaml, Does.Not.Contain("<wv2:WebView2"));
        });
    }

    [Test]
    public void PlayerVolume_更新入口を中央関数へ集約する()
    {
        string playerSource = GetMainWindowPlayerSourceText();
        string libVlcSource = GetLibVlcPlayerSourceText();

        Assert.That(
            playerSource,
            Does.Contain(
                "private double _currentPlayerVolume = PlayerVolumePolicy.DefaultVolume;"
            )
        );
        Assert.That(playerSource, Does.Contain("private double GetCurrentPlayerVolumeSetting()"));
        Assert.That(playerSource, Does.Contain("private void ApplyPlayerVolumeSetting("));
        Assert.That(playerSource, Does.Contain("bool updateSlider,"));
        Assert.That(playerSource, Does.Contain("bool save,"));
        Assert.That(playerSource, Does.Contain("bool applyToEngine"));
        Assert.That(playerSource, Does.Contain("private void SetPlayerVolumeFromUser(double volume)"));
        Assert.That(playerSource, Does.Contain("ApplyCurrentPlayerVolumeToEngine();"));
        Assert.That(playerSource, Does.Contain("SetPlayerVolumeFromUser(uxVolumeSlider.Value);"));
        Assert.That(libVlcSource, Does.Contain("private void ApplyCurrentPlayerVolumeToEngine()"));
        Assert.That(libVlcSource, Does.Contain("GetCurrentPlayerVolumeSetting() * 100d"));
        Assert.That(libVlcSource, Does.Contain("private void SetPlayerEngineMuted(bool muted)"));
    }

    [Test]
    public void PlayMovie_Click_再生統計DB更新は背景へ逃がす()
    {
        string source = GetMainWindowPlayerSourceText();
        string playMethod = GetMethodBlock(source, "public async void PlayMovie_Click(");
        string movieStatsMethod = GetMethodBlock(
            source,
            "private void QueueMoviePlaybackStatsPersist("
        );
        string bookmarkStatsMethod = GetMethodBlock(
            source,
            "private static void QueueBookmarkViewCountUpdate("
        );

        Assert.That(playMethod, Does.Contain("QueueMoviePlaybackStatsPersist("));
        Assert.That(playMethod, Does.Contain("QueueBookmarkViewCountUpdate("));
        Assert.That(playMethod, Does.Not.Contain("_mainDbMovieMutationFacade.UpdateScore("));
        Assert.That(playMethod, Does.Not.Contain("UpdateBookmarkViewCount("));
        Assert.That(movieStatsMethod, Does.Contain("Task.Run("));
        Assert.That(movieStatsMethod, Does.Contain("_mainDbMovieMutationFacade.UpdateScore("));
        Assert.That(movieStatsMethod, Does.Contain("_mainDbMovieMutationFacade.UpdateViewCount("));
        Assert.That(movieStatsMethod, Does.Contain("_mainDbMovieMutationFacade.UpdateLastDate("));
        Assert.That(bookmarkStatsMethod, Does.Contain("Task.Run("));
        Assert.That(bookmarkStatsMethod, Does.Contain("TryUpdateBookmarkViewCount("));
        Assert.That(bookmarkStatsMethod, Does.Contain("BuildBookmarkPersistenceFailureState("));
        Assert.That(bookmarkStatsMethod, Does.Contain("BuildBookmarkPersistenceFailureLog("));
    }

    [Test]
    public void PlayMovie_Click_Bookmark再生位置のMovieInfo取得は背景へ逃がす()
    {
        string source = GetMainWindowPlayerSourceText();
        string playMethod = GetMethodBlock(source, "public async void PlayMovie_Click(");
        string resolveAsyncMethod = GetMethodBlock(
            source,
            "private static Task<int> ResolveBookmarkPlaybackMillisecondsAsync("
        );
        string resolveMethod = GetMethodBlock(
            source,
            "private static int ResolveBookmarkPlaybackMilliseconds("
        );

        Assert.That(playMethod, Does.Contain("await ResolveBookmarkPlaybackMillisecondsAsync("));
        Assert.That(playMethod, Does.Not.Contain("new MovieInfo(BookMarkedFilePath"));
        Assert.That(resolveAsyncMethod, Does.Contain("Task.Run("));
        Assert.That(resolveMethod, Does.Contain("MovieInfo movieInfo = new("));
        Assert.That(resolveMethod, Does.Contain("Math.Max(1, (int)movieInfo.FPS)"));
    }

    [Test]
    public void PlayMovie_Click_通常再生の存在確認は背景へ逃がす()
    {
        string source = GetMainWindowPlayerSourceText();
        string playMethod = GetMethodBlock(source, "public async void PlayMovie_Click(");
        string existsAsyncMethod = GetMethodBlock(
            source,
            "private static Task<bool> MoviePathExistsForPlaybackAsync("
        );

        Assert.That(playMethod, Does.Contain("string selectedMoviePath = mv.Movie_Path;"));
        Assert.That(playMethod, Does.Contain("playbackMoviePath = selectedMoviePath;"));
        Assert.That(playMethod, Does.Contain("playerParam.Replace(\"<file>\", $\"{playbackMoviePath}\")"));
        Assert.That(playMethod, Does.Contain("await MoviePathExistsForPlaybackAsync(selectedMoviePath)"));
        Assert.That(playMethod, Does.Not.Contain("Path.Exists(mv.Movie_Path)"));
        Assert.That(existsAsyncMethod, Does.Contain("Task.Run(() => Path.Exists(movieFullPath))"));
        Assert.That(existsAsyncMethod, Does.Contain("string.IsNullOrWhiteSpace(movieFullPath)"));
        Assert.That(existsAsyncMethod, Does.Contain("Task.FromResult(false)"));
    }

    [Test]
    public void PlayMovie_Click_サムネイル再生位置の解析は背景へ逃がす()
    {
        string playerSource = GetMainWindowPlayerSourceText();
        string xamlSource = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string playMethod = GetMethodBlock(playerSource, "public async void PlayMovie_Click(");
        string manualMethod = GetMethodBlock(
            playerSource,
            "private async void ManualThumbnail_Click("
        );
        string resolverMethod = GetMethodBlock(
            playerSource,
            "private Task<(int Milliseconds, int Position, bool HasPosition)> ResolveSelectedThumbnailPlaybackPositionAsync("
        );

        Assert.That(playMethod, Does.Contain("await ResolveSelectedThumbnailPlaybackPositionAsync("));
        Assert.That(manualMethod, Does.Contain("await ResolveSelectedThumbnailPlaybackPositionAsync("));
        Assert.That(playMethod, Does.Not.Contain("GetPlayPosition("));
        Assert.That(manualMethod, Does.Not.Contain("GetPlayPosition("));
        Assert.That(resolverMethod, Does.Contain("Task.Run(() =>"));
        Assert.That(xamlSource, Does.Contain("private static bool TryResolveThumbnailPlaybackPosition("));
        Assert.That(xamlSource, Does.Contain("thumbInfo.GetThumbInfo(thumbPath);"));
    }

    [Test]
    public void LibVLCPlayer_管理コードとWindowsネイティブ本体を同じバージョン系列で配布する()
    {
        string project = GetRepoText("IndigoMovieManager.csproj");

        Assert.That(project, Does.Contain("<PackageReference Include=\"LibVLCSharp.WPF\" Version=\"3.10.1\" />"));
        Assert.That(project, Does.Contain("<PackageReference Include=\"VideoLAN.LibVLC.Windows\" Version=\"3.0.23.1\" />"));
    }

    [Test]
    public void LibVLCPlayer_1実体を遅延初期化し動画切替で再利用する()
    {
        string source = GetLibVlcPlayerSourceText();
        string ensureMethod = GetMethodBlock(source, "private async Task<bool> EnsureLibVlcPlayerReadyAsync()");
        string openMethod = GetMethodBlock(source, "private async Task<bool> TryOpenLibVlcMovieAsync(string moviePath)");

        Assert.That(ensureMethod, Does.Contain("if (_isLibVlcPlayerInitialized && _playerMediaPlayer != null)"));
        Assert.That(source, Does.Contain("Core.Initialize(nativeDirectory);"));
        Assert.That(ensureMethod, Does.Contain("await initializationTask;"));
        Assert.That(ensureMethod, Does.Contain("_playerMediaPlayer = resources.MediaPlayer;"));
        Assert.That(ensureMethod, Does.Contain("uxVideoPlayer.MediaPlayer = _playerMediaPlayer;"));
        Assert.That(openMethod, Does.Contain("new(_playerLibVlc, moviePath, FromType.FromPath)"));
        Assert.That(openMethod, Does.Contain("_playerMediaPlayer.Media = media;"));
        Assert.That(openMethod, Does.Not.Contain("new MediaPlayer("));
    }

    [Test]
    public void LibVLCPlayer_イベントをUIスレッドへ戻し終了時に確実に破棄する()
    {
        string source = GetLibVlcPlayerSourceText();
        string lifecycle = GetRepoText("Views", "Main", "MainWindow.Lifecycle.cs");

        Assert.That(source, Does.Contain("mediaPlayer.Playing += PlayerMediaPlayer_Playing;"));
        Assert.That(source, Does.Contain("Dispatcher.BeginInvoke(DispatcherPriority.Background, action)"));
        Assert.That(source, Does.Contain("mediaPlayer.Playing -= PlayerMediaPlayer_Playing;"));
        Assert.That(source, Does.Contain("mediaPlayer.Dispose();"));
        Assert.That(source, Does.Contain("libVlc?.Dispose();"));
        Assert.That(lifecycle, Does.Contain("DisposeLibVlcPlayer();"));
    }

    [Test]
    public void LibVLCPlayer_遅れて届く状態通知を現在状態で無効化する()
    {
        string source = GetLibVlcPlayerSourceText();
        string playingMethod = GetMethodBlock(
            source,
            "private async void HandleLibVlcPlayerPlayingOnUi()"
        );
        string pausedMethod = GetMethodBlock(
            source,
            "private void PlayerMediaPlayer_Paused(object sender, EventArgs e)"
        );
        string stoppedMethod = GetMethodBlock(
            source,
            "private void PlayerMediaPlayer_Stopped(object sender, EventArgs e)"
        );
        string endedMethod = GetMethodBlock(
            source,
            "private void PlayerMediaPlayer_EndReached(object sender, EventArgs e)"
        );
        string errorMethod = GetMethodBlock(
            source,
            "private void PlayerMediaPlayer_EncounteredError(object sender, EventArgs e)"
        );

        Assert.Multiple(() =>
        {
            Assert.That(playingMethod, Does.Contain("_playerMediaPlayer?.State != VLCState.Playing"));
            Assert.That(pausedMethod, Does.Contain("_playerMediaPlayer?.State != VLCState.Paused"));
            Assert.That(stoppedMethod, Does.Contain("_playerMediaPlayer?.State != VLCState.Stopped"));
            Assert.That(endedMethod, Does.Contain("_playerMediaPlayer?.State != VLCState.Ended"));
            Assert.That(errorMethod, Does.Contain("_playerMediaPlayer?.State != VLCState.Error"));
        });
    }

    [Test]
    public void PlayerThumbnailClick_選択同期でスクロール位置を動かさない()
    {
        string selectionSource = GetMainWindowSelectionSourceText();

        Assert.That(
            selectionSource,
            Does.Contain("sender is FrameworkElement clickedElement")
        );
        Assert.That(
            selectionSource,
            Does.Contain("SelectPlayerThumbnailRecordWithoutScroll(clickedElement, record);")
        );
        Assert.That(selectionSource, Does.Contain("syncPlayerSelection: false"));
        Assert.That(selectionSource, Does.Contain("return;"));
        Assert.That(
            selectionSource,
            Does.Contain("FrameworkElement clickedElement,")
        );
        Assert.That(selectionSource, Does.Contain("_suppressPlayerThumbnailSelectionChanged = true;"));
        Assert.That(selectionSource, Does.Contain("SyncPlayerThumbnailSelectionAcrossViews(sourceList, record);"));
        Assert.That(
            selectionSource,
            Does.Contain("if (!ReferenceEquals(sourceList.SelectedItem, record))")
        );
        Assert.That(selectionSource, Does.Contain("ShowExtensionDetail(record);"));
        Assert.That(selectionSource, Does.Contain("ShowTagEditor(record);"));

        string upperTabPlayerSource = GetUpperTabPlayerSourceText();

        Assert.That(upperTabPlayerSource, Does.Contain("bool syncPlayerSelection = true"));
        Assert.That(upperTabPlayerSource, Does.Contain("if (syncPlayerSelection)"));
    }

    [Test]
    public void PlayerThumbnailRail_標準縦リスト1系統で切替と二重同期を持たない()
    {
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();
        string mainWindowXaml = GetRepoText("Views", "Main", "MainWindow.xaml");
        string viewportSource = GetRepoText("UpperTabs", "Common", "MainWindow.UpperTabs.Viewport.cs");

        Assert.That(mainWindowXaml, Does.Contain("x:Name=\"PlayerThumbnailList\""));
        Assert.That(mainWindowXaml, Does.Contain("<VirtualizingStackPanel"));
        Assert.That(mainWindowXaml, Does.Contain("右レールは固定高の縦1行に限定し"));
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("return PlayerThumbnailList;")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("プレイヤータブは右レール固定に寄せ、Grid風サムネを1系統だけ流す。")
        );
        Assert.That(mainWindowXaml, Does.Not.Contain("PlayerThumbnailCompactList"));
        Assert.That(mainWindowXaml, Does.Not.Contain("PlayerThumbnailSingleColumnButton"));
        Assert.That(mainWindowXaml, Does.Not.Contain("PlayerThumbnailCompactGridButton"));
        Assert.That(upperTabPlayerSource, Does.Not.Contain("_isPlayerThumbnailCompactViewEnabled"));
        Assert.That(upperTabPlayerSource, Does.Not.Contain("SetPlayerThumbnailCompactViewMode"));
        Assert.That(upperTabPlayerSource, Does.Not.Contain("PlayerTabBottomLayoutWidthThreshold"));
        Assert.That(viewportSource, Does.Not.Contain("AttachUpperTabScrollViewer(PlayerThumbnailCompactList);"));
    }

    [Test]
    public void PlayerTabLayout_サムネを右端固定し空状態でもPlayer幅を確保する()
    {
        string mainWindowXaml = GetRepoText("Views", "Main", "MainWindow.xaml");
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();
        int playerTabStart = mainWindowXaml.IndexOf(
            "x:Name=\"TabPlayer\"",
            StringComparison.Ordinal
        );
        int playerTabTagEnd = mainWindowXaml.IndexOf('>', playerTabStart);
        int playerRootStart = mainWindowXaml.IndexOf(
            "x:Name=\"PlayerTabLayoutRoot\"",
            StringComparison.Ordinal
        );
        int playerRootTagEnd = mainWindowXaml.IndexOf('>', playerRootStart);

        Assert.That(playerTabStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(playerTabTagEnd, Is.GreaterThan(playerTabStart));
        Assert.That(playerRootStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(playerRootTagEnd, Is.GreaterThan(playerRootStart));

        string playerTabStartTag = mainWindowXaml.Substring(
            playerTabStart,
            playerTabTagEnd - playerTabStart
        );
        string playerRootStartTag = mainWindowXaml.Substring(
            playerRootStart,
            playerRootTagEnd - playerRootStart
        );

        Assert.Multiple(() =>
        {
            Assert.That(
                playerTabStartTag,
                Does.Contain("HorizontalContentAlignment=\"Stretch\"")
            );
            Assert.That(playerTabStartTag, Does.Contain("VerticalContentAlignment=\"Stretch\""));
            Assert.That(playerRootStartTag, Does.Contain("HorizontalAlignment=\"Stretch\""));
            Assert.That(playerRootStartTag, Does.Contain("VerticalAlignment=\"Stretch\""));
            Assert.That(
                mainWindowXaml,
                Does.Contain("x:Name=\"PlayerTabMainColumn\" MinWidth=\"480\" Width=\"*\"")
            );
            Assert.That(
                mainWindowXaml,
                Does.Contain("x:Name=\"PlayerTabSideColumn\" Width=\"264\"")
            );
            Assert.That(upperTabPlayerSource, Does.Contain("Grid.SetColumn(PlayerThumbnailHost, 2);"));
            Assert.That(
                upperTabPlayerSource,
                Does.Contain("PlayerTabMainColumn.Width = new GridLength(1d, GridUnitType.Star);")
            );
            Assert.That(
                upperTabPlayerSource,
                Does.Contain("PlayerTabSideColumn.Width = new GridLength(264d);")
            );
        });
    }

    [Test]
    public void PlayerThumbnailRail_画像BindingへMoviePathを渡してviewport近傍ゲートを効かせる()
    {
        string mainWindowXaml = GetRepoText("Views", "Main", "MainWindow.xaml");
        int listStart = mainWindowXaml.IndexOf(
            "x:Name=\"PlayerThumbnailList\"",
            StringComparison.Ordinal
        );
        Assert.That(listStart, Is.GreaterThanOrEqualTo(0));

        int converterIndex = mainWindowXaml.IndexOf(
            "Converter=\"{StaticResource playerRightRailImageSourceConverter}\"",
            listStart,
            StringComparison.Ordinal
        );
        Assert.That(converterIndex, Is.GreaterThan(listStart));

        int bindingEnd = mainWindowXaml.IndexOf("</MultiBinding>", converterIndex, StringComparison.Ordinal);
        Assert.That(bindingEnd, Is.GreaterThan(converterIndex));
        string playerThumbnailImageBinding = mainWindowXaml.Substring(
            converterIndex,
            bindingEnd - converterIndex
        );

        // converter の4番目の値へ動画パスを渡し、右レールでも viewport 近傍だけ decode する。
        Assert.That(playerThumbnailImageBinding, Does.Contain("<Binding Path=\"ThumbPathGrid\" />"));
        Assert.That(playerThumbnailImageBinding, Does.Contain("<Binding Path=\"IsExists\" />"));
        Assert.That(
            playerThumbnailImageBinding,
            Does.Contain("<Binding Source=\"{x:Reference PlayerThumbnailList}\" Path=\"IsVisible\" />")
        );
        Assert.That(playerThumbnailImageBinding, Does.Contain("<Binding Path=\"Movie_Path\" />"));
    }

    [Test]
    public void PlayerSurface_同一表示状態とサイズは再代入しない()
    {
        string mainWindowPlayerSource = GetMainWindowPlayerSourceText();
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();

        Assert.That(
            upperTabPlayerSource,
            Does.Contain("private static void SetPlayerVisibilityIfChanged(UIElement element, Visibility visibility)")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("element == null || element.Visibility == visibility")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("SetPlayerVisibilityIfChanged(PlayerArea, Visibility.Visible);")
        );
        Assert.That(
            mainWindowPlayerSource,
            Does.Contain("private static void SetPlayerElementSizeIfChanged(")
        );
        Assert.That(
            mainWindowPlayerSource,
            Does.Contain("ArePlayerLayoutLengthsEqual(element.Width, width)")
        );
        Assert.That(
            mainWindowPlayerSource,
            Does.Contain("ArePlayerLayoutLengthsEqual(element.Height, height)")
        );
        Assert.That(
            mainWindowPlayerSource,
            Does.Contain("double.IsNaN(current) && double.IsNaN(next)")
        );
    }

    [Test]
    public void PlayerSurface操作は保存処理とDB書込を直接持たない()
    {
        // surface操作は見た目の反映だけに閉じ、保存は既存のqueue/background経路へ任せる。
        string mainWindowPlayerSource = GetMainWindowPlayerSourceText();
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();
        string libVlcSource = GetLibVlcPlayerSourceText();

        string[] surfaceMethods =
        [
            GetMethodBlock(upperTabPlayerSource, "private void ShowPlayerSurface()"),
            GetMethodBlock(upperTabPlayerSource, "private void ShowPlayerEmptyState()"),
            GetMethodBlock(libVlcSource, "private void ReleaseLibVlcPlayerResources()"),
            GetMethodBlock(mainWindowPlayerSource, "private void UpdateManualPlayerViewport()"),
            GetMethodBlock(mainWindowPlayerSource, "private static void SetPlayerElementSizeIfChanged("),
            GetMethodBlock(mainWindowPlayerSource, "private static void SetPlayerElementWidthIfChanged("),
            GetMethodBlock(mainWindowPlayerSource, "private static void SetPlayerElementHeightIfChanged("),
            GetMethodBlock(mainWindowPlayerSource, "private void CloseManualPlayerOverlay()"),
        ];

        foreach (string surfaceMethod in surfaceMethods)
        {
            AssertPlayerSurfaceMethodDoesNotPersist(surfaceMethod);
        }
    }

    [Test]
    public void Player保存経路はsurface操作から分離した既存queueとbackgroundへ寄せる()
    {
        string mainWindowPlayerSource = GetMainWindowPlayerSourceText();

        string applyVolumeMethod = GetMethodBlock(
            mainWindowPlayerSource,
            "private void ApplyPlayerVolumeSetting("
        );
        string queueVolumeSaveMethod = GetMethodBlock(
            mainWindowPlayerSource,
            "private void QueuePlayerVolumeSettingSave()"
        );
        string backgroundVolumeSaveMethod = GetMethodBlock(
            mainWindowPlayerSource,
            "private void QueuePlayerVolumeSettingSaveInBackground()"
        );
        string saveVolumeMethod = GetMethodBlock(
            mainWindowPlayerSource,
            "private void SavePlayerVolumeSettingInBackground(PersistenceWriteRequest writeRequest)"
        );
        string playbackStatsPersistMethod = GetMethodBlock(
            mainWindowPlayerSource,
            "private void QueueMoviePlaybackStatsPersist("
        );

        Assert.That(applyVolumeMethod, Does.Contain("QueuePlayerVolumeSettingSave();"));
        Assert.That(queueVolumeSaveMethod, Does.Contain("DispatcherTimer"));
        Assert.That(backgroundVolumeSaveMethod, Does.Contain("TaskScheduler.Default"));
        Assert.That(backgroundVolumeSaveMethod, Does.Contain("BuildPlayerVolumeSettingsWriteRequest()"));
        Assert.That(saveVolumeMethod, Does.Contain("Properties.Settings.Default.Save();"));
        Assert.That(saveVolumeMethod, Does.Contain("PersistenceWriteResult.FromSuccess("));
        Assert.That(saveVolumeMethod, Does.Contain("PersistenceWriteResult.FromFailure("));
        Assert.That(
            saveVolumeMethod,
            Does.Contain("player volume settings save succeeded: {result.LogFields}")
        );
        Assert.That(playbackStatsPersistMethod, Does.Contain("Task.Run("));
        Assert.That(playbackStatsPersistMethod, Does.Contain("PersistenceWriteRequest.Create("));
        Assert.That(playbackStatsPersistMethod, Does.Contain("PersistenceWriteResult.FromSuccess("));
        Assert.That(playbackStatsPersistMethod, Does.Contain("PersistenceWriteResult.FromFailure("));
        Assert.That(
            playbackStatsPersistMethod,
            Does.Contain(
                "playback stats persist succeeded: db='{dbFullPath}' movie_id={movieId} {result.LogFields}"
            )
        );
        Assert.That(
            playbackStatsPersistMethod,
            Does.Contain("playback stats persist failed: db='{dbFullPath}' movie_id={movieId} {result.LogFields}")
        );
    }

    [Test]
    public void PlayerThumbnailSelectionSync_同一選択では再スクロールとvisible更新を積まない()
    {
        // プレイヤータブ内の同一動画再生では、選択同期だけで重い visible refresh を再投入しない。
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();

        Assert.That(
            upperTabPlayerSource,
            Does.Contain("private bool SelectUpperTabPlayerMovieRecord(MovieRecords record)")
        );
        Assert.That(upperTabPlayerSource, Does.Contain("bool selectionChanged = false;"));
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("if (ReferenceEquals(list.SelectedItem, record))")
        );
        Assert.That(upperTabPlayerSource, Does.Contain("bool activeListSelectionChanged = false;"));
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("if (activeListSelectionChanged)")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("if (!ReferenceEquals(list.SelectedItem, selectedMovie))")
        );
        Assert.That(upperTabPlayerSource, Does.Contain("return selectionChanged;"));
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("selectionChanged = SelectUpperTabPlayerMovieRecord(record);")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("if (selectionChanged)")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("RequestUpperTabVisibleRangeRefresh(immediate: true, reason: \"player-selection\");")
        );
    }

    [Test]
    public void PlayerThumbnailSelectionChanged_抑止中と非表示中は詳細更新を走らせない()
    {
        // Player 右レールの選択同期では、裏側の SelectionChanged から詳細/タグ更新を二重起動しない。
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();
        string handler = GetMethodBlock(
            upperTabPlayerSource,
            "private async void PlayerThumbnailList_SelectionChanged("
        );
        string syncMethod = GetMethodBlock(
            upperTabPlayerSource,
            "private void SyncUpperTabPlayerSelection("
        );

        int guardIndex = handler.IndexOf(
            "if (_suppressPlayerThumbnailSelectionChanged || TabPlayer?.IsSelected != true)",
            StringComparison.Ordinal
        );
        int listSelectionIndex = handler.IndexOf("List_SelectionChanged(sender, e);", StringComparison.Ordinal);
        int nullSelectionIndex = handler.IndexOf("if (selectedMovie == null)", StringComparison.Ordinal);
        int nullSelectionListIndex = handler.IndexOf(
            "List_SelectionChanged(sender, e);",
            nullSelectionIndex,
            StringComparison.Ordinal
        );

        Assert.That(guardIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(listSelectionIndex, Is.GreaterThan(guardIndex));
        Assert.That(nullSelectionIndex, Is.GreaterThan(guardIndex));
        Assert.That(nullSelectionListIndex, Is.GreaterThan(nullSelectionIndex));
        Assert.That(syncMethod, Does.Contain("ShowExtensionDetail(record);"));
        Assert.That(syncMethod, Does.Contain("ShowTagEditor(record);"));
    }

    [Test]
    public void PlayerDispatcherBackgroundWait_同時待機は1本へ畳む()
    {
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();

        Assert.That(
            upperTabPlayerSource,
            Does.Contain("private DispatcherOperation _playerBackgroundYieldOperation;")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("private async Task WaitForPlayerDispatcherBackgroundAsync()")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("pendingOperation = Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("_playerBackgroundYieldOperation = pendingOperation;")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("await WaitForPlayerDispatcherBackgroundAsync();")
        );
    }

    [Test]
    public void PlayerTabActivationAutoOpen_タブ表示後のContextIdleへ遅延する()
    {
        // タブ切替は先に完了させ、動画初期化だけを後段へ送って初動の重さを抑える。
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();

        Assert.That(
            upperTabPlayerSource,
            Does.Contain("private int _playerTabActivationAutoOpenRevision;")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("QueuePlayerTabActivationAutoOpen(selectedMovie);")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("private async Task RunPlayerTabActivationAutoOpenAsync(")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("await WaitForPlayerDispatcherContextIdleOrDelayAsync();")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("DispatcherPriority.ContextIdle")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("private const int PlayerTabActivationAutoOpenMaxDelayMs = 250;")
        );
        Assert.That(upperTabPlayerSource, Does.Contain("Task.Delay(PlayerTabActivationAutoOpenMaxDelayMs)"));
        Assert.That(upperTabPlayerSource, Does.Contain("Task.WhenAny(idleTask, delayTask)"));
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("revision != _playerTabActivationAutoOpenRevision")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("TabPlayer?.IsSelected != true")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("!IsSamePlayerTabAutoOpenMovie(")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("currentMovie.Movie_Id == requestedMovie.Movie_Id")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("System.StringComparison.OrdinalIgnoreCase")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("player activation auto-open failed")
        );
    }

    [Test]
    public void LibVLCPlayer_起動時warmを持たず初回再生時に遅延初期化する()
    {
        string startupSource = GetMainWindowStartupSourceText();
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();
        string libVlcSource = GetLibVlcPlayerSourceText();
        string openMethod = GetMethodBlock(
            libVlcSource,
            "private async Task<bool> TryOpenLibVlcMovieAsync(string moviePath)"
        );

        Assert.That(startupSource, Does.Not.Contain("QueuePlayerWebViewEnvironmentWarm();"));
        Assert.That(upperTabPlayerSource, Does.Not.Contain("WarmPlayerWebViewEnvironmentAfterIdleAsync"));
        Assert.That(openMethod, Does.Contain("EnsureLibVlcPlayerReadyAsync()"));
        Assert.That(startupSource, Does.Not.Contain("EnsureLibVlcPlayerReadyAsync()"));
    }

    [Test]
    public void HandleUpperTabPlayerSelectionChanged_自動再生を直接awaitしない()
    {
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();
        string handler = GetMethodBlock(
            upperTabPlayerSource,
            "private void HandleUpperTabPlayerSelectionChanged("
        );

        Assert.That(handler, Does.Contain("QueuePlayerTabActivationAutoOpen(selectedMovie);"));
        Assert.That(handler, Does.Not.Contain("await OpenMovieInPlayerTabAsync("));
    }

    [Test]
    public void PlayerTabActivationAutoOpen_先頭選択のSelectionChanged即再生を抑止する()
    {
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();
        string defaultViewMethod = GetMethodBlock(
            upperTabPlayerSource,
            "private void SelectUpperTabPlayerAsDefaultView()"
        );
        string handler = GetMethodBlock(
            upperTabPlayerSource,
            "private void HandleUpperTabPlayerSelectionChanged("
        );

        Assert.That(defaultViewMethod, Does.Contain("_suppressPlayerThumbnailSelectionChanged = true;"));
        Assert.That(defaultViewMethod, Does.Contain("SelectFirstUpperTabPlayerItemIfAvailable();"));
        Assert.That(defaultViewMethod, Does.Contain("_suppressPlayerThumbnailSelectionChanged = false;"));
        Assert.That(handler, Does.Contain("_suppressPlayerThumbnailSelectionChanged = true;"));
        Assert.That(
            handler,
            Does.Contain("RefreshUpperTabExtensionDetailFromCurrentSelection(")
        );
        Assert.That(handler, Does.Contain("_suppressPlayerThumbnailSelectionChanged = false;"));
    }

    [Test]
    public void PausePlayerTabPlaybackForBackground_予約自動再生を無効化する()
    {
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();
        string pauseMethod = GetMethodBlock(
            upperTabPlayerSource,
            "private void PausePlayerTabPlaybackForBackground()"
        );

        Assert.That(pauseMethod, Does.Contain("++_playerTabActivationAutoOpenRevision;"));
    }

    [Test]
    public void OpenMovieInPlayerTabAsync_Player操作中はuser_priority_scopeで囲む()
    {
        // Player 再生開始中は watch/poll を後ろへ逃がし、早期 return でも必ず解除する。
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();
        string mainWindowPlayerSource = GetMainWindowPlayerSourceText();
        string mainWindowXaml = GetRepoText("Views", "Main", "MainWindow.xaml");

        Assert.That(upperTabPlayerSource, Does.Contain("BeginUserPriorityWork(\"player\");"));
        Assert.That(upperTabPlayerSource, Does.Contain("MarkPlayerUserPriorityReleasePending("));
        Assert.That(upperTabPlayerSource, Does.Contain("ReleasePendingPlayerUserPriorityWork();"));
        Assert.That(upperTabPlayerSource, Does.Contain("PlayerUserPriorityMaxDurationMs = 250"));
        Assert.That(upperTabPlayerSource, Does.Contain("revision != _playerUserPriorityRevision"));
        Assert.That(upperTabPlayerSource, Does.Contain("expectedRevision != _pendingPlayerUserPriorityRevision"));
        Assert.That(upperTabPlayerSource, Does.Contain("PlayerUserPriorityReleaseTimer_Tick"));
        Assert.That(upperTabPlayerSource, Does.Contain("\"timeout\""));
        Assert.That(upperTabPlayerSource, Does.Contain("await TryOpenLibVlcMovieAsync(moviePath)"));
        Assert.That(mainWindowXaml, Does.Contain("<vlc:VideoView"));
        Assert.That(mainWindowXaml, Does.Not.Contain("MediaEnded="));
        Assert.That(mainWindowXaml, Does.Not.Contain("NavigationStarting="));
        Assert.That(mainWindowPlayerSource, Does.Contain("SetPlayerPlaybackActive(false,"));
        Assert.That(upperTabPlayerSource, Does.Contain("try"));
        Assert.That(upperTabPlayerSource, Does.Contain("finally"));
        Assert.That(upperTabPlayerSource, Does.Contain("EndUserPriorityWork(\"player\");"));
    }

    [Test]
    public void PlayerUserPriority_LibVLCのPlayingまたはErrorで解除する()
    {
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();
        string libVlcSource = GetLibVlcPlayerSourceText();
        string openMethod = GetMethodBlock(
            upperTabPlayerSource,
            "private async Task OpenMovieInPlayerTabAsync("
        );
        string applyMethod = GetMethodBlock(
            upperTabPlayerSource,
            "private async Task ApplyPendingPlayerPlaybackRequestAsync()"
        );
        string playingMethod = GetMethodBlock(
            libVlcSource,
            "private async void HandleLibVlcPlayerPlayingOnUi()"
        );
        string errorMethod = GetMethodBlock(
            libVlcSource,
            "private void PlayerMediaPlayer_EncounteredError(object sender, EventArgs e)"
        );

        Assert.Multiple(() =>
        {
            Assert.That(openMethod, Does.Contain("await TryOpenLibVlcMovieAsync(moviePath)"));
            Assert.That(openMethod, Does.Contain("MarkPlayerUserPriorityReleasePending(playerUserPriorityRevision);"));
            Assert.That(openMethod, Does.Contain("releaseUserPriorityOnExit = false;"));
            Assert.That(playingMethod, Does.Contain("await ApplyPendingPlayerPlaybackRequestAsync();"));
            Assert.That(errorMethod, Does.Contain("ReleasePendingPlayerUserPriorityWork("));
            Assert.That(applyMethod, Does.Contain("finally"));
            Assert.That(applyMethod, Does.Contain("ReleasePendingPlayerUserPriorityWork();"));
        });
    }

    [Test]
    public void PlayerUserPriority_連続選択は前要求を解除してから次要求をBeginする()
    {
        string source = GetUpperTabPlayerSourceText();
        string openMethod = GetMethodBlock(source, "private async Task OpenMovieInPlayerTabAsync(");

        int releaseIndex = openMethod.IndexOf(
            "ReleasePendingPlayerUserPriorityWork(\"superseded\");",
            StringComparison.Ordinal
        );
        int beginIndex = openMethod.IndexOf("BeginUserPriorityWork(\"player\");", StringComparison.Ordinal);

        Assert.That(releaseIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(beginIndex, Is.GreaterThan(releaseIndex));
    }

    [Test]
    public void PlayerUserPriority_shutdownはpendingを解除してBeginしない()
    {
        string source = GetUpperTabPlayerSourceText();
        string lifecycleSource = GetRepoText("Views", "Main", "MainWindow.Lifecycle.cs");
        string openMethod = GetMethodBlock(source, "private async Task OpenMovieInPlayerTabAsync(");
        string closingMethod = GetMethodBlock(
            lifecycleSource,
            "private void MainWindow_Closing(object sender, CancelEventArgs e)"
        );
        int shutdownGuardIndex = openMethod.IndexOf(
            "Dispatcher?.HasShutdownStarted == true",
            StringComparison.Ordinal
        );
        int beginIndex = openMethod.IndexOf("BeginUserPriorityWork(\"player\");", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(shutdownGuardIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(beginIndex, Is.GreaterThan(shutdownGuardIndex));
            Assert.That(closingMethod, Does.Contain("ReleasePendingPlayerUserPriorityWork(\"shutdown\");"));
        });
    }

    [Test]
    public void PlayerUserPriority_timeoutは再生を止めずpriorityだけを解除する()
    {
        string source = GetUpperTabPlayerSourceText();
        string timeoutMethod = GetMethodBlock(
            source,
            "private void PlayerUserPriorityReleaseTimer_Tick(object sender, System.EventArgs e)"
        );

        Assert.Multiple(() =>
        {
            Assert.That(timeoutMethod, Does.Contain("ReleasePendingPlayerUserPriorityWork("));
            Assert.That(timeoutMethod, Does.Contain("timeout"));
            Assert.That(timeoutMethod, Does.Not.Contain("uxVideoPlayer.Stop("));
            Assert.That(timeoutMethod, Does.Not.Contain("uxVideoPlayer.Pause("));
            Assert.That(timeoutMethod, Does.Not.Contain("ResetWebViewPlayerSurface("));
            Assert.That(timeoutMethod, Does.Not.Contain("SetPlayerPlaybackActive(false"));
        });
    }

    [Test]
    public void PlayerUserPriority_revisionGuardはstale解除を無視して現在要求だけを解除する()
    {
        MainWindow window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        SetPrivateField(window, "_isPlayerUserPriorityReleasePending", true);
        SetPrivateField(window, "_pendingPlayerUserPriorityRevision", 12);

        InvokePrivateMethod(window, "ReleasePendingPlayerUserPriorityWork", "completed", 11);

        Assert.Multiple(() =>
        {
            Assert.That(GetPrivateField<bool>(window, "_isPlayerUserPriorityReleasePending"), Is.True);
            Assert.That(GetPrivateField<int>(window, "_pendingPlayerUserPriorityRevision"), Is.EqualTo(12));
        });

        InvokePrivateMethod(window, "ReleasePendingPlayerUserPriorityWork", "timeout", 12);

        Assert.Multiple(() =>
        {
            Assert.That(GetPrivateField<bool>(window, "_isPlayerUserPriorityReleasePending"), Is.False);
            Assert.That(GetPrivateField<int>(window, "_pendingPlayerUserPriorityRevision"), Is.Zero);
        });
    }

    [Test]
    public void PlayerPlaybackState_実遷移だけplayer_playback理由でログに残す()
    {
        // 再生中フラグは Everything poll の入力なので、状態遷移だけを操作優先語彙で観測する。
        string mainWindowPlayerSource = GetMainWindowPlayerSourceText();
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();
        string fullscreenWindowSource = GetUpperTabPlayerFullscreenWindowSourceText();
        string bookmarkSource = GetRepoText(
            "BottomTabs",
            "Bookmark",
            "MainWindow.BottomTab.Bookmark.cs"
        );
        string stateMethod = GetMethodBlock(
            mainWindowPlayerSource,
            "private void SetPlayerPlaybackActive(bool isActive, string reason = \"\")"
        );
        string transitionMethod = GetMethodBlock(
            mainWindowPlayerSource,
            "private static string ResolvePlayerPlaybackTransitionLogValue(bool isActive)"
        );

        Assert.Multiple(() =>
        {
            Assert.That(mainWindowPlayerSource, Does.Contain("private int _isPlayerPlaybackActiveFlag;"));
            Assert.That(stateMethod, Does.Contain("Interlocked.Exchange("));
            Assert.That(stateMethod, Does.Contain("if (previousValue == nextValue)"));
            Assert.That(
                stateMethod,
                Does.Contain(
                    "player playback state changed: {BuildPlayerPlaybackCoreRouteLogFields(isActive, reason)}"
                )
            );
            Assert.That(mainWindowPlayerSource, Does.Contain("BuildPlayerPlaybackCoreRouteLogFields("));
            Assert.That(mainWindowPlayerSource, Does.Contain("core_route=player-playback"));
            Assert.That(
                mainWindowPlayerSource,
                Does.Contain("operation_reason={UiOperationPriorityPolicy.OperationReasonPlayerPlayback}")
            );
            Assert.That(
                mainWindowPlayerSource,
                Does.Contain("player_surface={ResolvePlayerPlaybackSurfaceLogValue()}")
            );
            Assert.That(
                mainWindowPlayerSource,
                Does.Contain("player_surface_ready={FormatLogBool(IsPlayerPlaybackSurfaceReady())}")
            );
            Assert.That(mainWindowPlayerSource, Does.Contain("active={FormatLogBool(isActive)}"));
            Assert.That(
                mainWindowPlayerSource,
                Does.Contain("player_transition={ResolvePlayerPlaybackTransitionLogValue(isActive)}")
            );
            Assert.That(transitionMethod, Does.Contain("return isActive ? \"start\" : \"stop\";"));
            Assert.That(mainWindowPlayerSource, Does.Contain("reason={reason ?? \"\"}"));
            Assert.That(mainWindowPlayerSource, Does.Contain("return \"libvlc\";"));
            Assert.That(
                mainWindowPlayerSource,
                Does.Contain("return _isLibVlcPlayerInitialized && _playerMediaPlayer != null;")
            );
            Assert.That(mainWindowPlayerSource, Does.Not.Contain("IsPlaying ="));
            Assert.That(upperTabPlayerSource, Does.Not.Contain("IsPlaying ="));
            Assert.That(fullscreenWindowSource, Does.Not.Contain("IsPlaying ="));
            Assert.That(bookmarkSource, Does.Not.Contain("IsPlaying ="));
            Assert.That(upperTabPlayerSource, Does.Contain("SetPlayerPlaybackActive(true, \"player-tab-open\")"));
            Assert.That(upperTabPlayerSource, Does.Contain("SetPlayerPlaybackActive(false, \"player-tab-open-paused\")"));
            Assert.That(bookmarkSource, Does.Contain("SetPlayerPlaybackActive(false, \"bookmark-add\")"));
        });
    }

    [Test]
    public void PlayerPlaybackState_実遷移でUiShell入力snapshotをui_priorityへ残す()
    {
        // Player 遷移時点の UI Shell 状態を残し、同状態通知ではログを増やさない。
        string mainWindowPlayerSource = GetMainWindowPlayerSourceText();
        string stateMethod = GetMethodBlock(
            mainWindowPlayerSource,
            "private void SetPlayerPlaybackActive(bool isActive, string reason = \"\")"
        );

        int noTransitionGuardIndex = stateMethod.IndexOf(
            "if (previousValue == nextValue)",
            StringComparison.Ordinal
        );
        int snapshotIndex = stateMethod.IndexOf(
            "UiOperationSnapshot snapshot = CaptureUserPriorityOperationSnapshot(",
            StringComparison.Ordinal
        );
        int uiPriorityLogIndex = stateMethod.IndexOf("\"ui-priority\"", StringComparison.Ordinal);
        int coreRouteLogIndex = stateMethod.IndexOf(
            "BuildPlayerPlaybackCoreRouteLogFields(isActive, reason)",
            StringComparison.Ordinal
        );

        Assert.Multiple(() =>
        {
            Assert.That(noTransitionGuardIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(snapshotIndex, Is.GreaterThan(noTransitionGuardIndex));
            Assert.That(uiPriorityLogIndex, Is.GreaterThan(snapshotIndex));
            Assert.That(coreRouteLogIndex, Is.GreaterThan(uiPriorityLogIndex));
            Assert.That(stateMethod, Does.Contain("CaptureUserPriorityOperationSnapshot("));
            Assert.That(stateMethod, Does.Contain("IsUserPriorityWorkActive()"));
            Assert.That(stateMethod, Does.Contain("isManualMode: false"));
            Assert.That(stateMethod, Does.Contain("BuildUiShellInputLogMessage("));
            Assert.That(
                stateMethod,
                Does.Contain("UiOperationPriorityPolicy.OperationReasonPlayerPlayback,")
            );
            Assert.That(stateMethod, Does.Contain("reason,"));
            Assert.That(stateMethod, Does.Contain("snapshot"));
        });
    }

    [Test]
    public void PlayerPlayback_core_route_helperはPhase7契約fieldsを固定する()
    {
        string mainWindowPlayerSource = GetMainWindowPlayerSourceText();
        string helperMethod = GetMethodBlock(
            mainWindowPlayerSource,
            "private string BuildPlayerPlaybackCoreRouteLogFields("
        );
        string stateMethod = GetMethodBlock(
            mainWindowPlayerSource,
            "private void SetPlayerPlaybackActive(bool isActive, string reason = \"\")"
        );

        Assert.Multiple(() =>
        {
            Assert.That(stateMethod, Does.Contain("BuildPlayerPlaybackCoreRouteLogFields(isActive, reason)"));
            Assert.That(helperMethod, Does.Contain("core_route=player-playback"));
            Assert.That(helperMethod, Does.Contain("operation_reason="));
            Assert.That(
                helperMethod,
                Does.Contain("UiOperationPriorityPolicy.OperationReasonPlayerPlayback")
            );
            Assert.That(helperMethod, Does.Contain("player_surface_ready="));
            Assert.That(helperMethod, Does.Contain("player_transition="));
            Assert.That(helperMethod, Does.Contain("ResolvePlayerPlaybackTransitionLogValue(isActive)"));
            Assert.That(helperMethod, Does.Contain("reason="));
        });
    }

    [Test]
    public void PlayerPlayback_core_route詳細はstate_changedログ行に同居する()
    {
        string mainWindowPlayerSource = GetMainWindowPlayerSourceText();
        string stateMethod = GetMethodBlock(
            mainWindowPlayerSource,
            "private void SetPlayerPlaybackActive(bool isActive, string reason = \"\")"
        );
        string stateChangedLogLine = GetLineContaining(
            stateMethod,
            "player playback state changed:"
        );

        Assert.Multiple(() =>
        {
            Assert.That(
                stateChangedLogLine,
                Does.Contain("BuildPlayerPlaybackCoreRouteLogFields(isActive, reason)")
            );
            Assert.That(stateMethod, Does.Contain("BuildUiShellInputLogMessage("));
            Assert.That(stateMethod, Does.Contain("UiOperationPriorityPolicy.OperationReasonPlayerPlayback"));
        });
    }

    [Test]
    public void PlayerPlayback_core_route_helperはPhase7契約値を返す()
    {
        // Player の実遷移ログに出る core route 値を固定し、再生制御の観測語彙を守る。
        MainWindow window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));

        string startResult = InvokePlayerPlaybackCoreRouteLogFields(
            window,
            isActive: true,
            "start-libvlc"
        );
        string stopResult = InvokePlayerPlaybackCoreRouteLogFields(
            window,
            isActive: false,
            "stop-libvlc"
        );
        string emptyReasonResult = InvokePlayerPlaybackCoreRouteLogFields(
            window,
            isActive: true,
            null!
        );

        Assert.Multiple(() =>
        {
            Assert.That(
                startResult,
                Is.EqualTo(
                    "core_route=player-playback operation_reason=player-playback player_surface=libvlc player_surface_ready=false active=true player_transition=start reason=start-libvlc"
                )
            );
            Assert.That(
                stopResult,
                Is.EqualTo(
                    "core_route=player-playback operation_reason=player-playback player_surface=libvlc player_surface_ready=false active=false player_transition=stop reason=stop-libvlc"
                )
            );
            Assert.That(
                emptyReasonResult,
                Is.EqualTo(
                    "core_route=player-playback operation_reason=player-playback player_surface=libvlc player_surface_ready=false active=true player_transition=start reason="
                )
            );
        });
    }

    [Test]
    public void OpenMovieInPlayerTabAsync_存在確認はuser_priority開始前に背景へ逃がす()
    {
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();
        string openMovieMethod = GetMethodBlock(
            upperTabPlayerSource,
            "private async Task OpenMovieInPlayerTabAsync("
        );
        string existsMethod = GetMethodBlock(
            upperTabPlayerSource,
            "private static Task<bool> PlayerTabMoviePathExistsInBackgroundAsync("
        );
        string stillCurrentMethod = GetMethodBlock(
            upperTabPlayerSource,
            "private static bool IsPlayerTabMoviePathStillCurrent("
        );

        int existsAwaitIndex = openMovieMethod.IndexOf(
            "await PlayerTabMoviePathExistsInBackgroundAsync(moviePath)",
            StringComparison.Ordinal
        );
        int beginPriorityIndex = openMovieMethod.IndexOf(
            "BeginUserPriorityWork(\"player\");",
            StringComparison.Ordinal
        );

        Assert.That(openMovieMethod, Does.Contain("string moviePath = movie?.Movie_Path;"));
        Assert.That(openMovieMethod, Does.Not.Contain("Path.Exists(movie.Movie_Path)"));
        Assert.That(openMovieMethod, Does.Contain("uxVideoPlayer == null"));
        Assert.That(openMovieMethod, Does.Contain("Dispatcher?.HasShutdownStarted == true"));
        Assert.That(openMovieMethod, Does.Contain("IsPlayerTabMoviePathStillCurrent(movie, moviePath)"));
        Assert.That(existsAwaitIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(beginPriorityIndex, Is.GreaterThan(existsAwaitIndex));
        Assert.That(existsMethod, Does.Contain("Task.Run(() =>"));
        Assert.That(existsMethod, Does.Contain("return Path.Exists(moviePath);"));
        Assert.That(existsMethod, Does.Contain("Task.FromResult(false)"));
        Assert.That(stillCurrentMethod, Does.Contain("movie.Movie_Path"));
    }

    [Test]
    public void Player専用WebView2とMediaElement経路を残さずLibVLC終了処理を使う()
    {
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();
        string mainWindowPlayerSource = GetMainWindowPlayerSourceText();
        string fullscreenSource = GetUpperTabPlayerFullscreenWindowSourceText();
        string libVlcSource = GetLibVlcPlayerSourceText();
        string lifecycleSource = GetRepoText("Views", "Main", "MainWindow.Lifecycle.cs");
        string playerSources = upperTabPlayerSource + mainWindowPlayerSource + fullscreenSource;

        Assert.That(playerSources, Does.Not.Contain("uxWebVideoPlayer"));
        Assert.That(playerSources, Does.Not.Contain("_isWebViewPlayerActive"));
        Assert.That(playerSources, Does.Not.Contain("MediaElement"));
        Assert.That(libVlcSource, Does.Contain("private void DisposeLibVlcPlayer()"));
        Assert.That(lifecycleSource, Does.Contain("DisposeLibVlcPlayer();"));
    }

    private static string InvokePlayerPlaybackCoreRouteLogFields(
        MainWindow window,
        bool isActive,
        string reason
    )
    {
        MethodInfo? method = typeof(MainWindow).GetMethod(
            "BuildPlayerPlaybackCoreRouteLogFields",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(method, Is.Not.Null);

        return (string)method!.Invoke(window, [isActive, reason])!;
    }

    private static void InvokePrivateMethod(MainWindow window, string methodName, params object[] args)
    {
        MethodInfo? method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(method, Is.Not.Null, $"{methodName} が見つかりません。");
        method!.Invoke(window, args);
    }

    private static void SetPrivateField<T>(MainWindow window, string fieldName, T value)
    {
        FieldInfo? field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null, $"{fieldName} が見つかりません。");
        field!.SetValue(window, value);
    }

    private static T GetPrivateField<T>(MainWindow window, string fieldName)
    {
        FieldInfo? field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null, $"{fieldName} が見つかりません。");
        return (T)field!.GetValue(window)!;
    }

    private static string GetMainWindowPlayerSourceText()
    {
        return GetRepoText("Views", "Main", "MainWindow.Player.cs");
    }

    private static string GetMainWindowSelectionSourceText()
    {
        return GetRepoText("Views", "Main", "MainWindow.Selection.cs");
    }

    private static string GetUpperTabPlayerSourceText()
    {
        return GetRepoText("UpperTabs", "Player", "MainWindow.UpperTabs.PlayerTab.cs");
    }

    private static string GetUpperTabPlayerFullscreenWindowSourceText()
    {
        return GetRepoText("UpperTabs", "Player", "MainWindow.UpperTabs.PlayerFullscreenWindow.cs");
    }

    private static string GetLibVlcPlayerSourceText()
    {
        return GetRepoText("UpperTabs", "Player", "MainWindow.UpperTabs.LibVlcPlayer.cs");
    }

    private static string GetMainWindowStartupSourceText()
    {
        return GetRepoText("Views", "Main", "MainWindow.Startup.cs");
    }

    private static string GetRepoText(params string[] relativePathParts)
    {
        string candidate = Path.Combine([GetRepositoryRoot(), .. relativePathParts]);
        if (File.Exists(candidate))
        {
            return File.ReadAllText(candidate);
        }

        Assert.Fail($"{Path.Combine(relativePathParts)} の位置を repo root から解決できませんでした。");
        return string.Empty;
    }

    private static string GetRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        // 分離artifactsでも、コンパイル元ソースから正本リポジトリへ戻る。
        DirectoryInfo? current = new(Path.GetDirectoryName(sourceFilePath) ?? "");
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IndigoMovieManager.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        Assert.Fail("リポジトリルートを検出できませんでした。");
        return string.Empty;
    }

    private static string GetMethodBlock(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{signature} が見つかりません。");

        int bodyStart = source.IndexOf('{', start);
        Assert.That(bodyStart, Is.GreaterThanOrEqualTo(0), $"{signature} の本文開始が見つかりません。");

        int depth = 0;
        for (int index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(start, index - start + 1);
                }
            }
        }

        Assert.Fail($"{signature} の本文終了が見つかりません。");
        return string.Empty;
    }

    private static string GetLineContaining(string source, string marker)
    {
        string? line = source.Replace("\r\n", "\n")
            .Split('\n')
            .FirstOrDefault(x => x.Contains(marker, StringComparison.Ordinal));
        Assert.That(line, Is.Not.Null, $"{marker} を含む行が見つかりません。");
        return line!;
    }

    private static void AssertPlayerSurfaceMethodDoesNotPersist(string method)
    {
        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Not.Contain("Properties.Settings.Default.Save("));
            Assert.That(method, Does.Not.Contain("QueueApplicationSettingsSave("));
            Assert.That(method, Does.Not.Contain("QueuePlayerVolumeSettingSave("));
            Assert.That(method, Does.Not.Contain("QueueMoviePlaybackStatsPersist("));
            Assert.That(method, Does.Not.Contain("QueueBookmarkViewCountUpdate("));
            Assert.That(method, Does.Not.Contain("ExecuteNonQuery("));
            Assert.That(method, Does.Not.Contain("_mainDbMovieMutationFacade."));
            Assert.That(method, Does.Not.Contain("UpdateScore("));
            Assert.That(method, Does.Not.Contain("UpdateViewCount("));
            Assert.That(method, Does.Not.Contain("UpdateLastDate("));
            Assert.That(method, Does.Not.Contain("UpdateBookmarkViewCount("));
        });
    }
}
