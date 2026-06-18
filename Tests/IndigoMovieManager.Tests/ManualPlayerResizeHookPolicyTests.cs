using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ManualPlayerResizeHookPolicyTests
{
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
    public void PlayerVolume_保存値100は既定音量として50へ戻す()
    {
        string playerSource = GetMainWindowPlayerSourceText();
        string windowSource = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string settingsSource = GetRepoText("Properties", "Settings.settings");
        string settingsDesignerSource = GetRepoText("Properties", "Settings.Designer.cs");
        string fullscreenWindowSource = GetUpperTabPlayerFullscreenWindowSourceText();

        Assert.That(playerSource, Does.Contain("private const double DefaultPlayerVolume = 0.5d;"));
        Assert.That(playerSource, Does.Contain("private static double ResolveSavedPlayerVolumeSetting(double volume)"));
        Assert.That(playerSource, Does.Contain("return resolvedVolume >= 1d ? DefaultPlayerVolume : resolvedVolume;"));
        Assert.That(playerSource, Does.Contain("private void RestorePlayerVolumeFromSettings()"));
        Assert.That(playerSource, Does.Contain("save: repairSavedVolume"));
        Assert.That(settingsSource, Does.Contain("<Setting Name=\"PlayerVolume\" Type=\"System.Double\" Scope=\"User\">"));
        Assert.That(settingsSource, Does.Contain("<Value Profile=\"(Default)\">0.5</Value>"));
        Assert.That(settingsDesignerSource, Does.Contain("DefaultSettingValueAttribute(\"0.5\")"));
        Assert.That(fullscreenWindowSource, Does.Contain("public double Volume { get; set; } = 0.5d;"));
        Assert.That(
            windowSource,
            Does.Contain("RestorePlayerVolumeFromSettings();")
        );
    }

    [Test]
    public void PlayerVolume_更新入口を中央関数へ集約する()
    {
        string playerSource = GetMainWindowPlayerSourceText();
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();
        string fullscreenWindowSource = GetUpperTabPlayerFullscreenWindowSourceText();

        Assert.That(playerSource, Does.Contain("private double _currentPlayerVolume = DefaultPlayerVolume;"));
        Assert.That(playerSource, Does.Contain("private double GetCurrentPlayerVolumeSetting()"));
        Assert.That(playerSource, Does.Contain("private void ApplyPlayerVolumeSetting("));
        Assert.That(playerSource, Does.Contain("bool updateSlider,"));
        Assert.That(playerSource, Does.Contain("bool save,"));
        Assert.That(playerSource, Does.Contain("bool pushToWebView"));
        Assert.That(playerSource, Does.Contain("private void SetPlayerVolumeFromUser(double volume)"));
        Assert.That(playerSource, Does.Contain("private void PushCurrentPlayerVolumeToWebView()"));
        Assert.That(playerSource, Does.Contain("private void SetPlayerVolumeFromWebView(double volume)"));
        Assert.That(playerSource, Does.Contain("SetPlayerVolumeFromUser(uxVolumeSlider.Value);"));
        Assert.That(upperTabPlayerSource, Does.Contain("double restoreVolume = GetCurrentPlayerVolumeSetting();"));
        Assert.That(upperTabPlayerSource, Does.Contain("string volume = GetCurrentPlayerVolumeSetting().ToString("));
        Assert.That(upperTabPlayerSource, Does.Contain("const string playerReadyMessage = \"player-ready\";"));
        Assert.That(upperTabPlayerSource, Does.Contain("PushCurrentPlayerVolumeToWebView();"));
        Assert.That(fullscreenWindowSource, Does.Contain("SetPlayerVolumeFromWebView(snapshot.Volume);"));
        Assert.That(fullscreenWindowSource, Does.Contain("snapshot.Volume = GetCurrentPlayerVolumeSetting();"));
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
    public void WebViewPlayer_ホスト音量適用前の既定音量通知を抑止する()
    {
        string mainWindowPlayerSource = GetMainWindowPlayerSourceText();
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();
        string fullscreenWindowSource = GetUpperTabPlayerFullscreenWindowSourceText();

        Assert.That(mainWindowPlayerSource, Does.Contain("indigoPlayerHostVolumeApplied = '1'"));
        Assert.That(upperTabPlayerSource, Does.Contain("indigoPlayerHostVolumeApplied = '1'"));
        Assert.That(fullscreenWindowSource, Does.Contain("indigoPlayerHostVolumeApplied = '1'"));
        Assert.That(mainWindowPlayerSource, Does.Contain("indigoPlayerHostVolumeApplying = '1'"));
        Assert.That(upperTabPlayerSource, Does.Contain("indigoPlayerHostVolumeApplying = '1'"));
        Assert.That(mainWindowPlayerSource, Does.Contain("}, 250);"));
        Assert.That(upperTabPlayerSource, Does.Contain("}, 250);"));
        Assert.That(upperTabPlayerSource, Does.Contain("chrome.webview.postMessage('player-ready');"));
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("player.dataset.indigoPlayerHostVolumeApplied !== '1'")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("player.dataset.indigoPlayerHostVolumeApplying === '1'")
        );
        Assert.That(upperTabPlayerSource, Does.Not.Contain("notifyVolume();"));
    }

    [Test]
    public void WebViewPlayer_動画切り替え時の100パーセント通知は保存しない()
    {
        string playerSource = GetMainWindowPlayerSourceText();

        Assert.That(playerSource, Does.Contain("if (resolvedVolume >= 1d)"));
        Assert.That(playerSource, Does.Contain("double fallbackVolume = currentVolume >= 1d ? DefaultPlayerVolume : currentVolume;"));
        Assert.That(playerSource, Does.Contain("player webview default volume ignored"));
        Assert.That(playerSource, Does.Contain("ApplyPlayerVolumeSetting("));
        Assert.That(playerSource, Does.Contain("fallbackVolume,"));
        Assert.That(playerSource, Does.Contain("pushToWebView: true"));
        Assert.That(playerSource, Does.Contain("return;"));
    }

    [Test]
    public void WebViewPlayer_100パーセント通知分岐は既定50パーセントへ戻してから保存する()
    {
        string playerSource = GetMainWindowPlayerSourceText();
        string method = GetMethodBlock(
            playerSource,
            "private void SetPlayerVolumeFromWebView(double volume)"
        );
        int branchStart = method.IndexOf("if (resolvedVolume >= 1d)", StringComparison.Ordinal);
        Assert.That(branchStart, Is.GreaterThanOrEqualTo(0));

        int branchReturn = method.IndexOf("return;", branchStart, StringComparison.Ordinal);
        Assert.That(branchReturn, Is.GreaterThan(branchStart));
        string defaultVolumeBranch = method.Substring(branchStart, branchReturn - branchStart);

        Assert.Multiple(() =>
        {
            Assert.That(
                defaultVolumeBranch,
                Does.Contain("currentVolume >= 1d ? DefaultPlayerVolume : currentVolume")
            );
            Assert.That(defaultVolumeBranch, Does.Contain("fallbackVolume,"));
            Assert.That(defaultVolumeBranch, Does.Contain("save: true"));
            Assert.That(defaultVolumeBranch, Does.Contain("pushToWebView: true"));
            Assert.That(defaultVolumeBranch, Does.Not.Contain("resolvedVolume,"));
        });
    }

    [Test]
    public void PlayerThumbnailClick_選択同期でスクロール位置を動かさない()
    {
        string selectionSource = GetMainWindowSelectionSourceText();

        Assert.That(selectionSource, Does.Contain("SelectPlayerThumbnailRecordWithoutScroll(label, record);"));
        Assert.That(selectionSource, Does.Contain("syncPlayerSelection: false"));
        Assert.That(selectionSource, Does.Contain("return;"));
        Assert.That(
            selectionSource,
            Does.Contain("private void SelectPlayerThumbnailRecordWithoutScroll(Label label, MovieRecords record)")
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
    public void PlayerThumbnailRail_Grid風1系統で切替と二重同期を持たない()
    {
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();
        string mainWindowXaml = GetRepoText("Views", "Main", "MainWindow.xaml");
        string viewportSource = GetRepoText("UpperTabs", "Common", "MainWindow.UpperTabs.Viewport.cs");

        Assert.That(mainWindowXaml, Does.Contain("x:Name=\"PlayerThumbnailList\""));
        Assert.That(mainWindowXaml, Does.Contain("<vwp:VirtualizingWrapPanel"));
        Assert.That(mainWindowXaml, Does.Contain("右レールはGrid風の固定幅だけにして"));
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

        string[] surfaceMethods =
        [
            GetMethodBlock(upperTabPlayerSource, "private void ShowPlayerSurface()"),
            GetMethodBlock(upperTabPlayerSource, "private void ShowPlayerEmptyState()"),
            GetMethodBlock(upperTabPlayerSource, "private void ResetWebViewPlayerSurface()"),
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
        Assert.That(saveVolumeMethod, Does.Contain("PersistenceWriteResult.FromFailure("));
        Assert.That(playbackStatsPersistMethod, Does.Contain("Task.Run("));
        Assert.That(playbackStatsPersistMethod, Does.Contain("PersistenceWriteRequest.Create("));
        Assert.That(playbackStatsPersistMethod, Does.Contain("PersistenceWriteResult.FromFailure("));
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
    public void PlayerWebViewEnvironmentWarm_起動light_services後に環境だけ先行作成する()
    {
        string startupSource = GetMainWindowStartupSourceText();
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();
        string warmMethod = GetMethodBlock(
            upperTabPlayerSource,
            "private async Task WarmPlayerWebViewEnvironmentAfterIdleAsync()"
        );

        Assert.That(startupSource, Does.Contain("QueuePlayerWebViewEnvironmentWarm();"));
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("private bool _playerWebViewEnvironmentWarmQueued;")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("private void QueuePlayerWebViewEnvironmentWarm()")
        );
        Assert.That(warmMethod, Does.Contain("await WaitForPlayerDispatcherContextIdleOrDelayAsync();"));
        Assert.That(warmMethod, Does.Contain("await GetOrCreatePlayerWebViewEnvironmentAsync();"));
        Assert.That(warmMethod, Does.Not.Contain("EnsureCoreWebView2Async"));
        Assert.That(warmMethod, Does.Not.Contain("BeginUserPriorityWork(\"player\")"));
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
        Assert.That(upperTabPlayerSource, Does.Contain("MarkPlayerUserPriorityReleasePending();"));
        Assert.That(upperTabPlayerSource, Does.Contain("ReleasePendingPlayerUserPriorityWork();"));
        Assert.That(upperTabPlayerSource, Does.Contain("if (!e.IsSuccess)"));
        Assert.That(mainWindowXaml, Does.Contain("MediaEnded=\"UxVideoPlayer_MediaEnded\""));
        Assert.That(upperTabPlayerSource, Does.Contain("_hasPendingWebViewPlaybackRequest = false;"));
        Assert.That(mainWindowXaml, Does.Contain("NavigationStarting=\"UxWebVideoPlayer_NavigationStarting\""));
        Assert.That(upperTabPlayerSource, Does.Contain("e.NavigationId != _pendingWebViewNavigationId"));
        Assert.That(mainWindowXaml, Does.Contain("MediaFailed=\"UxVideoPlayer_MediaFailed\""));
        Assert.That(mainWindowPlayerSource, Does.Contain("private void UxVideoPlayer_MediaFailed("));
        Assert.That(mainWindowPlayerSource, Does.Contain("private void UxVideoPlayer_MediaEnded("));
        Assert.That(mainWindowPlayerSource, Does.Contain("SetPlayerPlaybackActive(false);"));
        Assert.That(mainWindowPlayerSource, Does.Contain("_hasPendingPlayerPlaybackRequest = false;"));
        Assert.That(upperTabPlayerSource, Does.Contain("try"));
        Assert.That(upperTabPlayerSource, Does.Contain("finally"));
        Assert.That(upperTabPlayerSource, Does.Contain("EndUserPriorityWork(\"player\");"));
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
    public void ResetWebViewPlayerSurface_WebView停止時もpending_user_priorityを解除する()
    {
        // WebView の NavigationCompleted が後着しても、reset 側で優先区間を確実に畳む。
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();
        string resetMethod = GetMethodBlock(
            upperTabPlayerSource,
            "private void ResetWebViewPlayerSurface()"
        );
        string openMovieMethod = GetMethodBlock(
            upperTabPlayerSource,
            "private async Task OpenMovieInPlayerTabAsync("
        );

        int webViewActiveCheckIndex = openMovieMethod.IndexOf(
            "if (_isWebViewPlayerActive)",
            StringComparison.Ordinal
        );
        int resetCallIndex = openMovieMethod.IndexOf(
            "ResetWebViewPlayerSurface();",
            StringComparison.Ordinal
        );

        Assert.That(webViewActiveCheckIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(resetCallIndex, Is.GreaterThan(webViewActiveCheckIndex));
        Assert.That(resetMethod, Does.Contain("_hasPendingWebViewPlaybackRequest = false;"));
        Assert.That(resetMethod, Does.Contain("_isWebViewPlayerActive = false;"));
        Assert.That(resetMethod, Does.Contain("_pendingWebViewNavigationId = 0;"));
        Assert.That(resetMethod, Does.Contain("ReleasePendingPlayerUserPriorityWork();"));
        Assert.That(
            resetMethod.IndexOf("ReleasePendingPlayerUserPriorityWork();", StringComparison.Ordinal),
            Is.LessThan(resetMethod.IndexOf("if (uxWebVideoPlayer == null)", StringComparison.Ordinal))
        );
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

    private static string GetMainWindowStartupSourceText()
    {
        return GetRepoText("Views", "Main", "MainWindow.Startup.cs");
    }

    private static string GetRepoText(params string[] relativePathParts)
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current != null)
        {
            string candidate = Path.Combine([current.FullName, .. relativePathParts]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        Assert.Fail($"{Path.Combine(relativePathParts)} の位置を repo root から解決できませんでした。");
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
