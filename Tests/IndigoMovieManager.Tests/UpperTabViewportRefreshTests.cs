using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UpperTabViewportRefreshTests
{
    [Test]
    public void 抑止期限内ならfollowup_scroll_refreshを止める()
    {
        long nowUtcTicks = DateTime.UtcNow.Ticks;
        long suppressUntilUtcTicks = nowUtcTicks + TimeSpan.FromMilliseconds(50).Ticks;

        bool actual = IndigoMovieManager.MainWindow.ShouldSuppressUpperTabFollowupScrollRefresh(
            nowUtcTicks,
            suppressUntilUtcTicks
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void 抑止期限切れならfollowup_scroll_refreshを止めない()
    {
        long nowUtcTicks = DateTime.UtcNow.Ticks;
        long suppressUntilUtcTicks = nowUtcTicks - 1;

        bool actual = IndigoMovieManager.MainWindow.ShouldSuppressUpperTabFollowupScrollRefresh(
            nowUtcTicks,
            suppressUntilUtcTicks
        );

        Assert.That(actual, Is.False);
    }

    [TestCase("scroll", true)]
    [TestCase("page-up", true)]
    [TestCase("page-down", true)]
    [TestCase("loaded", false)]
    [TestCase("tab-changed", false)]
    public void recent_viewportはスクロール系操作だけで立てる(string reason, bool expected)
    {
        bool actual = IndigoMovieManager.MainWindow.ShouldMarkRecentViewportInteraction(reason);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void recent_viewportの有効期限内だけactiveになる()
    {
        long nowUtcTicks = DateTime.UtcNow.Ticks;

        Assert.That(
            IndigoMovieManager.MainWindow.IsRecentViewportInteractionActive(
                nowUtcTicks,
                nowUtcTicks + TimeSpan.FromMilliseconds(50).Ticks
            ),
            Is.True
        );
        Assert.That(
            IndigoMovieManager.MainWindow.IsRecentViewportInteractionActive(
                nowUtcTicks,
                nowUtcTicks - 1
            ),
            Is.False
        );
    }

    [Test]
    public void Empty範囲はpreferredキーsnapshotを公開しない()
    {
        bool actual = IndigoMovieManager.MainWindow.ShouldPublishPreferredMoviePathKeysSnapshot(
            IndigoMovieManager.UpperTabs.Common.UpperTabVisibleRange.Empty
        );

        Assert.That(actual, Is.False);
    }

    [Test]
    public void 可視範囲がある場合だけpreferredキーsnapshotを公開する()
    {
        bool actual = IndigoMovieManager.MainWindow.ShouldPublishPreferredMoviePathKeysSnapshot(
            IndigoMovieManager.UpperTabs.Common.UpperTabVisibleRange.Create(
                firstVisibleIndex: 0,
                lastVisibleIndex: 2,
                totalCount: 10,
                overscanItemCount: 1
            )
        );

        Assert.That(actual, Is.True);
    }

    [TestCase(true, 7, 7, 10, 10, true)]
    [TestCase(true, 7, 7, 11, 10, false)]
    [TestCase(true, 7, 3, 10, 10, false)]
    [TestCase(false, 7, 7, 10, 10, false)]
    public void Viewport計測不能時は同一タブ同一sourceだけpreferredキーを保持する(
        bool hasPublishedSnapshot,
        int currentTabIndex,
        int snapshotTabIndex,
        int viewportSourceRevision,
        int snapshotSourceRevision,
        bool expected
    )
    {
        bool actual =
            IndigoMovieManager.MainWindow.ShouldPreservePreferredMoviePathKeysOnUnavailableViewport(
                hasPublishedSnapshot,
                currentTabIndex,
                snapshotTabIndex,
                viewportSourceRevision,
                snapshotSourceRevision
            );

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void Reset_scroll_anchorは標準タブと一意な現行recordだけを復元する()
    {
        string source = GetRepoText("UpperTabs", "Common", "MainWindow.UpperTabs.Viewport.cs");
        string captureMethod = GetMethodBlock(
            source,
            "private MovieViewScrollAnchorContext? CaptureMovieViewScrollAnchor()"
        );
        string restoreMethod = GetMethodBlock(
            source,
            "private void RestoreMovieViewScrollAnchor("
        );

        Assert.That(captureMethod, Does.Contain("TryGetCurrentUpperTabContext("));
        Assert.That(captureMethod, Does.Contain("!isStandardUpperTab"));
        Assert.That(captureMethod, Does.Contain("visibleRange.FirstVisibleIndex"));
        Assert.That(captureMethod, Does.Contain("MovieViewScrollAnchorPolicy.TryCapture("));
        Assert.That(restoreMethod, Does.Contain("currentTabIndex != captured.TabIndex"));
        Assert.That(restoreMethod, Does.Contain("MovieViewScrollAnchorPolicy.ResolveAfterCollectionApply("));
        Assert.That(restoreMethod, Does.Contain("collectionResult.HasChanges"));
        Assert.That(restoreMethod, Does.Contain("SuppressUpperTabFollowupScrollRefreshBriefly();"));
        Assert.That(restoreMethod, Does.Contain("listBox.ScrollIntoView(anchorMovie);"));
        Assert.That(restoreMethod, Does.Contain("dataGrid.ScrollIntoView(anchorMovie);"));
        Assert.That(restoreMethod, Does.Contain("itemsControl.UpdateLayout();"));
        Assert.That(restoreMethod, Does.Contain("MovieViewScrollAnchorPolicy.CalculateRestoredVerticalOffset("));
        Assert.That(restoreMethod, Does.Contain("scrollViewer.ScrollToVerticalOffset(restoredOffset);"));
        Assert.That(restoreMethod, Does.Contain("reason: \"reset-scroll-anchor\""));
        Assert.That(restoreMethod, Does.Contain("catch (InvalidOperationException)"));
    }

    [Test]
    public void Container_top取得はancestor例外と非finiteをfalseへ閉じる()
    {
        string source = GetRepoText("UpperTabs", "Common", "UpperTabViewportTracker.cs");
        string helper = GetMethodBlock(
            source,
            "public static bool TryGetContainerTopRelativeToViewport("
        );

        Assert.That(helper, Does.Contain("TransformToAncestor(scrollViewer)"));
        Assert.That(helper, Does.Contain("if (double.IsFinite(top))"));
        Assert.That(helper, Does.Contain("return false;"));
        Assert.That(helper, Does.Contain("catch (InvalidOperationException)"));
        Assert.That(helper, Does.Contain("top = 0;"));
        Assert.That(helper, Does.Not.Contain("UpdateLayout"));
    }

    [Test]
    public void Preferredキー更新時は画像MultiBinding再評価用revisionを進める()
    {
        string source = GetRepoText("UpperTabs", "Common", "MainWindow.UpperTabs.Viewport.cs");
        string applySnapshotMethod = GetMethodBlock(source, "private void ApplyUpperTabViewportSnapshot(");
        string clearMethod = GetMethodBlock(source, "private void ClearUpperTabVisibleRange()");
        string unavailableMethod = GetMethodBlock(source, "private void HandleUnavailableUpperTabViewport(");
        string refreshSharedMethod = GetMethodBlock(
            source,
            "private void RefreshSharedUpperTabImageRevision()"
        );
        string refreshPlayerMethod = GetMethodBlock(
            source,
            "private void RefreshPlayerRightRailImageRevision()"
        );

        // preferred key は static gate だけでは Binding を起こせないため、Window DP の revision を軽く進める。
        Assert.That(
            source,
            Does.Contain("public static readonly DependencyProperty UpperTabPreferredMoviePathKeysRevisionProperty")
        );
        Assert.That(source, Does.Contain("public int UpperTabPreferredMoviePathKeysRevision"));
        Assert.That(
            source,
            Does.Contain("public static readonly DependencyProperty PlayerRightRailImageRevisionProperty")
        );
        Assert.That(source, Does.Contain("public int PlayerRightRailImageRevision"));
        Assert.That(source, Does.Contain("private bool _isUpperTabPreferredMoviePathKeysSnapshotPublished;"));
        Assert.That(source, Does.Contain("private int _preferredVisibleMoviePathKeysTabIndex = -1;"));
        Assert.That(applySnapshotMethod, Does.Contain("bool preferredMoviePathKeysChanged"));
        Assert.That(applySnapshotMethod, Does.Contain("bool publishStateChanged"));
        Assert.That(
            applySnapshotMethod,
            Does.Contain("_preferredVisibleMoviePathKeysTabIndex = shouldPublishSnapshot ? currentTabIndex : -1;")
        );
        Assert.That(applySnapshotMethod, Does.Contain("bool preferredMoviePathKeysGateChanged"));
        Assert.That(
            applySnapshotMethod,
            Does.Contain("UpperTabActivationGate.UpdatePreferredMoviePathKeys(nextPreferredMoviePathKeys)")
        );
        Assert.That(applySnapshotMethod, Does.Contain("if (publishStateChanged || preferredMoviePathKeysGateChanged)"));
        Assert.That(
            applySnapshotMethod,
            Does.Not.Contain("if (publishStateChanged || preferredMoviePathKeysChanged)")
        );
        Assert.That(applySnapshotMethod, Does.Contain("RefreshActiveUpperTabImageRevision();"));
        Assert.That(
            unavailableMethod,
            Does.Contain("ShouldPreservePreferredMoviePathKeysOnUnavailableViewport(")
        );
        Assert.That(unavailableMethod, Does.Contain("if (!shouldPreservePreferredMoviePathKeys)"));
        Assert.That(clearMethod, Does.Contain("if (_isUpperTabPreferredMoviePathKeysSnapshotPublished)"));
        Assert.That(clearMethod, Does.Contain("_preferredVisibleMoviePathKeysTabIndex = -1;"));
        Assert.That(clearMethod, Does.Contain("RefreshActiveUpperTabImageRevision();"));
        Assert.That(refreshSharedMethod, Does.Contain("UpperTabPreferredMoviePathKeysRevision = unchecked("));
        Assert.That(refreshSharedMethod, Does.Contain("UpperTabPreferredMoviePathKeysRevision + 1"));
        Assert.That(refreshPlayerMethod, Does.Contain("PlayerRightRailImageRevision = unchecked("));
        Assert.That(refreshPlayerMethod, Does.Contain("PlayerRightRailImageRevision + 1"));
    }

    [Test]
    public void 通常タブのスクロールはrecent_viewportだけを使いPlayerだけuser_priorityを使う()
    {
        string viewportSource = GetRepoText(
            "UpperTabs",
            "Common",
            "MainWindow.UpperTabs.Viewport.cs"
        );
        string pageScrollSource = GetRepoText(
            "UpperTabs",
            "Common",
            "MainWindow.UpperTabs.PageScroll.cs"
        );
        string requestMethod = GetMethodBlock(
            viewportSource,
            "private void RequestUpperTabVisibleRangeRefresh("
        );
        string scrollChangedMethod = GetMethodBlock(
            viewportSource,
            "private void UpperTabScrollViewer_ScrollChanged("
        );
        string pageScrollMethod = GetMethodBlock(
            pageScrollSource,
            "private bool TryHandleUpperTabPageScroll("
        );

        Assert.That(requestMethod, Does.Contain("MarkRecentViewportInteraction(reason);"));
        Assert.That(scrollChangedMethod, Does.Not.Contain("BeginUserPriorityWork("));
        Assert.That(
            pageScrollMethod,
            Does.Contain("ReferenceEquals(activeItemsControl, PlayerThumbnailList)")
        );
        Assert.That(
            pageScrollMethod,
            Does.Contain("BeginOrExtendPlayerThumbnailScrollUserPriority(triggerReason);")
        );
        Assert.That(viewportSource, Does.Contain("UiOperationRecentViewportInteractionWindowMs = 250"));
        Assert.That(
            viewportSource,
            Does.Contain("PlayerThumbnailScrollUserPriorityWindowMs = 250")
        );
    }

    [Test]
    public void Playerサムネホイールは描画前に優先区間を開始しidleとshutdownで解放する()
    {
        string viewportSource = GetRepoText(
            "UpperTabs",
            "Common",
            "MainWindow.UpperTabs.Viewport.cs"
        );
        string attachMethod = GetMethodBlock(
            viewportSource,
            "private void AttachUpperTabScrollViewer("
        );
        string beginMethod = GetMethodBlock(
            viewportSource,
            "private void BeginOrExtendPlayerThumbnailScrollUserPriority("
        );
        string initializeMethod = GetMethodBlock(
            viewportSource,
            "private void InitializeUpperTabViewportSupport()"
        );
        string releaseMethod = GetMethodBlock(
            viewportSource,
            "private void ReleasePlayerThumbnailScrollUserPriority("
        );

        Assert.That(
            attachMethod,
            Does.Contain("scrollViewer.PreviewMouseWheel += PlayerThumbnailScrollViewer_PreviewMouseWheel;")
        );
        Assert.That(beginMethod, Does.Contain("BeginUserPriorityWork(\"player-thumbnail-scroll\");"));
        Assert.That(beginMethod, Does.Contain("_isPlayerThumbnailScrollUserPriorityActive"));
        Assert.That(
            initializeMethod,
            Does.Contain("Dispatcher.ShutdownStarted += PlayerThumbnailScrollDispatcher_ShutdownStarted;")
        );
        Assert.That(releaseMethod, Does.Contain("EndUserPriorityWork(\"player-thumbnail-scroll\");"));
        Assert.That(
            viewportSource,
            Does.Contain("ReleasePlayerThumbnailScrollUserPriority(\"idle\");")
        );
        Assert.That(
            viewportSource,
            Does.Contain("ReleasePlayerThumbnailScrollUserPriority(\"shutdown\");")
        );
    }

    [Test]
    public void Playerサムネスクロール計測は最初のRenderを捉えidle時に1行へ集約する()
    {
        string viewportSource = GetRepoText(
            "UpperTabs",
            "Common",
            "MainWindow.UpperTabs.Viewport.cs"
        );
        string beginMethod = GetMethodBlock(
            viewportSource,
            "private void BeginOrExtendPlayerThumbnailScrollUserPriority("
        );
        string queueMethod = GetMethodBlock(
            viewportSource,
            "private void QueuePlayerThumbnailScrollRenderMeasure()"
        );
        string releaseMethod = GetMethodBlock(
            viewportSource,
            "private void ReleasePlayerThumbnailScrollUserPriority("
        );

        Assert.That(beginMethod, Does.Contain("_playerThumbnailScrollInputCount++;"));
        Assert.That(beginMethod, Does.Contain("QueuePlayerThumbnailScrollRenderMeasure();"));
        Assert.That(queueMethod, Does.Contain("DispatcherPriority.Render"));
        Assert.That(queueMethod, Does.Contain("_playerThumbnailScrollRenderMeasureOperation != null"));
        Assert.That(
            releaseMethod,
            Does.Contain("player thumbnail scroll render: release_reason=")
        );
        Assert.That(releaseMethod, Does.Contain("input_count={_playerThumbnailScrollInputCount}"));
        Assert.That(
            releaseMethod,
            Does.Contain("first_render_ms={_playerThumbnailScrollFirstRenderElapsedMilliseconds}")
        );
        Assert.That(queueMethod, Does.Not.Contain("DebugRuntimeLog.Write("));
    }

    [Test]
    public void Playerページ送りは選択と再生を変えずviewportだけを移動する()
    {
        string pageScrollSource = GetRepoText(
            "UpperTabs",
            "Common",
            "MainWindow.UpperTabs.PageScroll.cs"
        );
        string playerSource = GetRepoText(
            "UpperTabs",
            "Player",
            "MainWindow.UpperTabs.PlayerTab.cs"
        );
        string pageScrollMethod = GetMethodBlock(
            pageScrollSource,
            "private bool TryHandleUpperTabPageScroll("
        );

        Assert.That(pageScrollMethod, Does.Contain("UpperTabScrollNavigator.TryScrollPage("));
        Assert.That(pageScrollMethod, Does.Not.Contain("SelectedIndex"));
        Assert.That(pageScrollMethod, Does.Not.Contain("SelectedItem"));
        Assert.That(playerSource, Does.Not.Contain("TryScrollUpperTabPlayerPage("));
    }

    [Test]
    public void Player右レールwarm完了はスクロールidle後にlatest_onlyで再評価する()
    {
        string viewportSource = GetRepoText(
            "UpperTabs",
            "Common",
            "MainWindow.UpperTabs.Viewport.cs"
        );
        string completionMethod = GetMethodBlock(
            viewportSource,
            "private void HandlePlayerRightRailImageWarmCompleted("
        );
        string queueMethod = GetMethodBlock(
            viewportSource,
            "private void QueuePlayerRightRailWarmRefresh()"
        );
        string applyMethod = GetMethodBlock(
            viewportSource,
            "private void ApplyPlayerRightRailWarmRefresh()"
        );

        Assert.That(
            viewportSource,
            Does.Contain("PlayerRightRailImageSourceConverter.ImageWarmCompleted +=")
        );
        Assert.That(
            viewportSource,
            Does.Contain("PlayerRightRailImageSourceConverter.ImageWarmCompleted -=")
        );
        Assert.That(completionMethod, Does.Contain("ContainsMoviePathKey("));
        Assert.That(
            completionMethod,
            Does.Contain("if (!_isPlayerThumbnailScrollUserPriorityActive)")
        );
        Assert.That(
            queueMethod,
            Does.Contain("_playerRightRailWarmRefreshOperation.Status == DispatcherOperationStatus.Pending")
        );
        Assert.That(applyMethod, Does.Contain("Stopwatch.StartNew();"));
        Assert.That(applyMethod, Does.Contain("visibleCompletionCount++"));
        Assert.That(applyMethod, Does.Contain("RefreshPlayerRightRailImageRevision();"));
        Assert.That(applyMethod, Does.Not.Contain("RefreshUpperTabPreferredMoviePathKeysRevision();"));
        Assert.That(
            applyMethod,
            Does.Contain("player right rail warm refresh: visible_completions=")
        );
        Assert.That(applyMethod, Does.Contain("shared_revision_updated=False"));
        Assert.That(applyMethod, Does.Contain("player_revision_updated={playerRevisionUpdated}"));
        Assert.That(applyMethod, Does.Contain("elapsed_ms={stopwatch.ElapsedMilliseconds}"));
        Assert.That(
            applyMethod,
            Does.Contain("scroll_priority_active={scrollPriorityActive}")
        );
        Assert.That(applyMethod, Does.Not.Contain(".Any("));
        Assert.That(applyMethod, Does.Not.Contain("SelectedItem"));
        Assert.That(applyMethod, Does.Not.Contain("ScrollTo"));
        Assert.That(
            viewportSource,
            Does.Contain("QueuePlayerRightRailWarmRefresh();")
        );
    }

    [TestCase("movie-key", "MOVIE-KEY", true)]
    [TestCase("movie-key", "other-key", false)]
    [TestCase("movie-key", "", false)]
    public void Player右レールwarm完了はvisibleキーだけを対象にする(
        string visibleKey,
        string completedKey,
        bool expected
    )
    {
        bool actual = IndigoMovieManager.MainWindow.ContainsMoviePathKey(
            [visibleKey],
            completedKey
        );

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void PageUpDown成功後にUiShell入力snapshotをログへ残す()
    {
        string pageScrollSource = GetRepoText(
            "UpperTabs",
            "Common",
            "MainWindow.UpperTabs.PageScroll.cs"
        );
        string pageScrollMethod = GetMethodBlock(
            pageScrollSource,
            "private bool TryHandleUpperTabPageScroll("
        );

        int scrollAttemptIndex = pageScrollMethod.IndexOf(
            "if (!UpperTabScrollNavigator.TryScrollPage(",
            StringComparison.Ordinal
        );
        int refreshRequestIndex = pageScrollMethod.IndexOf(
            "RequestUpperTabVisibleRangeRefresh(",
            scrollAttemptIndex,
            StringComparison.Ordinal
        );
        int snapshotIndex = pageScrollMethod.IndexOf(
            "UiOperationSnapshot snapshot = CaptureUserPriorityOperationSnapshot(",
            StringComparison.Ordinal
        );
        int inputLogIndex = pageScrollMethod.IndexOf(
            "BuildUiShellInputLogMessage(\"scroll\", triggerReason, snapshot)",
            StringComparison.Ordinal
        );
        int endLogIndex = pageScrollMethod.IndexOf("page scroll end:", StringComparison.Ordinal);
        int inputLogCount = pageScrollMethod.Split("BuildUiShellInputLogMessage(").Length - 1;

        Assert.That(
            pageScrollMethod,
            Does.Contain("string triggerReason = scrollForward.Value ? \"page-down\" : \"page-up\";")
        );
        Assert.That(pageScrollMethod, Does.Contain("reason: triggerReason"));
        Assert.That(pageScrollMethod, Does.Contain("IsUserPriorityWorkActive()"));
        Assert.That(pageScrollMethod, Does.Contain("isManualMode: false"));
        Assert.That(inputLogCount, Is.EqualTo(1));
        Assert.That(scrollAttemptIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(refreshRequestIndex, Is.GreaterThan(scrollAttemptIndex));
        Assert.That(snapshotIndex, Is.GreaterThan(refreshRequestIndex));
        Assert.That(inputLogIndex, Is.GreaterThan(snapshotIndex));
        Assert.That(endLogIndex, Is.GreaterThan(inputLogIndex));
    }

    [Test]
    public void 上側タブ切替はUiShell入力snapshotを1回だけログへ残す()
    {
        string source = GetRepoText("BottomTabs", "Common", "MainWindow.BottomTabs.Common.cs");
        string method = GetMethodBlock(
            source,
            "private async void Tabs_SelectionChangedAsync("
        );

        int guardReturnIndex = method.IndexOf("return;", StringComparison.Ordinal);
        int snapshotIndex = method.IndexOf(
            "UiOperationSnapshot snapshot = CaptureUserPriorityOperationSnapshot(",
            StringComparison.Ordinal
        );
        int inputLogIndex = method.IndexOf(
            "BuildUiShellInputLogMessage(\"upper-tab-switch\", \"selection-changed\", snapshot)",
            StringComparison.Ordinal
        );
        int handleIndex = method.IndexOf(
            "HandleUpperTabSelectionChangedCore();",
            StringComparison.Ordinal
        );
        int inputLogCount = method.Split("BuildUiShellInputLogMessage(").Length - 1;

        Assert.That(method, Does.Contain("sender as TabControl == null || e.OriginalSource is not TabControl"));
        Assert.That(method, Does.Contain("IsUserPriorityWorkActive()"));
        Assert.That(method, Does.Contain("isManualMode: false"));
        Assert.That(
            method,
            Does.Contain("BuildUiShellInputLogMessage(\"upper-tab-switch\", \"selection-changed\", snapshot)")
        );
        Assert.That(inputLogCount, Is.EqualTo(1));
        Assert.That(guardReturnIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(snapshotIndex, Is.GreaterThan(guardReturnIndex));
        Assert.That(inputLogIndex, Is.GreaterThan(snapshotIndex));
        Assert.That(handleIndex, Is.GreaterThan(inputLogIndex));
    }

    [Test]
    public void Gate側の同一snapshot判定はclear前にno_opで返す()
    {
        string source = GetRepoText("UpperTabs", "Common", "UpperTabActivationGate.cs");
        string updateMethod = GetMethodBlock(
            source,
            "public static bool UpdatePreferredMoviePathKeys("
        );

        int equalCheckIndex = updateMethod.IndexOf(
            "ArePreferredMoviePathKeysEqual(moviePathKeys)",
            StringComparison.Ordinal
        );
        int noOpReturnIndex = updateMethod.IndexOf("return false;", equalCheckIndex, StringComparison.Ordinal);
        int clearIndex = updateMethod.IndexOf("PreferredMoviePathKeys.Clear();", noOpReturnIndex, StringComparison.Ordinal);

        // 同じ snapshot は set を空に振らず、その場で no-op として抜ける。
        Assert.That(equalCheckIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(noOpReturnIndex, Is.GreaterThan(equalCheckIndex));
        Assert.That(clearIndex, Is.GreaterThan(noOpReturnIndex));
    }

    [Test]
    public void Player右レール画像MultiBindingはPlayer専用revisionをtriggerとして渡す()
    {
        string mainWindowXaml = GetRepoText("Views", "Main", "MainWindow.xaml");
        string playerThumbnailImageBinding = GetPlayerThumbnailImageBinding(mainWindowXaml);
        string converterSource = GetRepoText(
            "UpperTabs",
            "Player",
            "PlayerRightRailImageSourceConverter.cs"
        );

        // 5番目の Binding は Player右レール request の revision として扱い、古い要求だけ捨てる。
        Assert.That(
            playerThumbnailImageBinding,
            Does.Contain("Converter=\"{StaticResource playerRightRailImageSourceConverter}\"")
        );
        Assert.That(playerThumbnailImageBinding, Does.Contain("<Binding Path=\"ThumbPathGrid\" />"));
        Assert.That(playerThumbnailImageBinding, Does.Contain("<Binding Path=\"IsExists\" />"));
        Assert.That(
            playerThumbnailImageBinding,
            Does.Contain("<Binding Source=\"{x:Reference PlayerThumbnailList}\" Path=\"IsVisible\" />")
        );
        Assert.That(playerThumbnailImageBinding, Does.Contain("<Binding Path=\"Movie_Path\" />"));
        Assert.That(
            playerThumbnailImageBinding,
            Does.Contain("<Binding Source=\"{x:Reference window}\" Path=\"PlayerRightRailImageRevision\" />")
        );
        Assert.That(
            playerThumbnailImageBinding,
            Does.Not.Contain("Path=\"UpperTabPreferredMoviePathKeysRevision\"")
        );
        Assert.That(converterSource, Does.Contain("object moviePathValue = values.Length > 3 ? values[3] : null;"));
        Assert.That(converterSource, Does.Contain("object revisionValue = values.Length > 4 ? values[4] : null;"));
        Assert.That(converterSource, Does.Contain("ResolveImageRequestRevision(revisionValue)"));
    }

    [TestCase("SmallList", "ThumbPathSmall")]
    [TestCase("BigList", "ThumbPathBig")]
    [TestCase("GridList", "ThumbPathGrid")]
    [TestCase("ListDataGrid", "ThumbPathList")]
    [TestCase("BigList10", "ThumbPathBig10")]
    public void 通常上側タブ画像MultiBindingもpreferredキーrevisionをtriggerとして渡す(
        string listName,
        string thumbnailPathProperty
    )
    {
        string mainWindowXaml = GetRepoText("Views", "Main", "MainWindow.xaml");
        string imageBinding = GetImageBindingAfterElementName(mainWindowXaml, listName);

        Assert.That(imageBinding, Does.Contain($"<Binding Path=\"{thumbnailPathProperty}\" />"));
        Assert.That(imageBinding, Does.Contain("<Binding Path=\"IsExists\" />"));
        Assert.That(
            imageBinding,
            Does.Contain($"<Binding Source=\"{{x:Reference {listName}}}\" Path=\"IsVisible\" />")
        );
        Assert.That(imageBinding, Does.Contain("<Binding Path=\"Movie_Path\" />"));
        Assert.That(
            imageBinding,
            Does.Contain("<Binding Source=\"{x:Reference window}\" Path=\"UpperTabPreferredMoviePathKeysRevision\" />")
        );
        Assert.That(imageBinding, Does.Not.Contain("Path=\"PlayerRightRailImageRevision\""));
    }

    [Test]
    public void viewport更新はPlayerだけ専用revisionを進め通常タブは共有revisionを進める()
    {
        string source = GetRepoText("UpperTabs", "Common", "MainWindow.UpperTabs.Viewport.cs");
        string routeMethod = GetMethodBlock(source, "private void RefreshActiveUpperTabImageRevision()");

        Assert.That(routeMethod, Does.Contain("bool playerActive = TabPlayer?.IsSelected == true;"));
        Assert.That(routeMethod, Does.Contain("RefreshPlayerRightRailImageRevision();"));
        Assert.That(routeMethod, Does.Contain("RefreshSharedUpperTabImageRevision();"));
        Assert.That(routeMethod, Does.Not.Contain("RefreshUpperTabPreferredMoviePathKeysRevision();"));
    }

    [Test]
    public void 外部サムネ実体変更helperは通常タブとPlayerのrevisionを両方進める()
    {
        string source = GetRepoText("UpperTabs", "Common", "MainWindow.UpperTabs.Viewport.cs");
        string helper = GetMethodBlock(source, "private void RefreshUpperTabPreferredMoviePathKeysRevision()");

        Assert.That(helper, Does.Contain("RefreshSharedUpperTabImageRevision();"));
        Assert.That(helper, Does.Contain("RefreshPlayerRightRailImageRevision();"));
    }

    private static string GetPlayerThumbnailImageBinding(string mainWindowXaml)
    {
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
        return mainWindowXaml.Substring(converterIndex, bindingEnd - converterIndex);
    }

    private static string GetImageBindingAfterElementName(string mainWindowXaml, string elementName)
    {
        int elementStart = mainWindowXaml.IndexOf(
            $"x:Name=\"{elementName}\"",
            StringComparison.Ordinal
        );
        Assert.That(elementStart, Is.GreaterThanOrEqualTo(0), $"{elementName} が見つかりません。");

        int converterIndex = mainWindowXaml.IndexOf(
            "Converter=\"{StaticResource upperTabImageSourceConverter}\"",
            elementStart,
            StringComparison.Ordinal
        );
        Assert.That(converterIndex, Is.GreaterThan(elementStart));

        int bindingEnd = mainWindowXaml.IndexOf("</MultiBinding>", converterIndex, StringComparison.Ordinal);
        Assert.That(bindingEnd, Is.GreaterThan(converterIndex));
        return mainWindowXaml.Substring(converterIndex, bindingEnd - converterIndex);
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
}
