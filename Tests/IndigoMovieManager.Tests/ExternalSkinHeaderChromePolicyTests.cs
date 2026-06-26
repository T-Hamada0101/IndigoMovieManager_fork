using System.IO;
using System.Runtime.CompilerServices;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ExternalSkinHeaderChromePolicyTests
{
    [Test]
    public void 外部skinでも共通ヘッダーを表示し最小ヘッダーを畳む()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.WebViewSkin.Chrome.cs");
        string refreshSource = GetRepoText("Views", "Main", "MainWindow.WebViewSkin.cs");
        string menuActionSource = GetRepoText("Views", "Main", "MainWindow.MenuActions.cs");
        string xaml = GetRepoText("Views", "Main", "MainWindow.xaml");
        string method = GetMethodBlock(
            source,
            "private void ApplyExternalSkinMinimalChromeVisibility("
        );
        string syncMethod = GetMethodBlock(
            source,
            "private void SyncExternalSkinMinimalSkinSelector("
        );
        string refreshMethod = GetMethodBlock(
            refreshSource,
            "private async Task RefreshExternalSkinHostPresentationAsync("
        );
        string mainHeaderBarTag = GetXmlStartTag(xaml, "x:Name=\"MainHeaderBar\"");
        string dockingManagerTag = GetXmlStartTag(xaml, "x:Name=\"uxDockingManager\"");

        Assert.Multiple(() =>
        {
            Assert.That(
                method,
                Does.Contain("MainHeaderStandardChromePanel.Visibility = Visibility.Visible;")
            );
            Assert.That(
                method,
                Does.Contain("ExternalSkinMinimalChromePanel.Visibility = Visibility.Collapsed;")
            );
            Assert.That(
                method,
                Does.Contain("SyncExternalSkinMinimalSkinSelector(true, displaySkinName);")
            );
            Assert.That(
                method,
                Does.Not.Contain("MainHeaderStandardChromePanel.Visibility = Visibility.Collapsed;")
            );
            Assert.That(
                method,
                Does.Not.Contain("ExternalSkinMinimalChromePanel.Visibility = Visibility.Visible;")
            );
            Assert.That(xaml, Does.Contain("x:Name=\"MainHeaderStandardChromePanel\""));
            Assert.That(xaml, Does.Not.Contain("<materialDesign:DrawerHost"));
            Assert.That(xaml, Does.Not.Contain("x:Name=\"MainDrawerHost\""));
            Assert.That(xaml, Does.Contain("<RowDefinition Height=\"32\" />"));
            Assert.That(xaml, Does.Contain("x:Name=\"MainHeaderBar\""));
            Assert.That(mainHeaderBarTag, Does.Contain("Grid.Row=\"0\""));
            Assert.That(mainHeaderBarTag, Does.Contain("VerticalAlignment=\"Top\""));
            Assert.That(dockingManagerTag, Does.Contain("Margin=\"0,32,0,0\""));
            Assert.That(xaml, Does.Contain("Height=\"26\""));
            Assert.That(xaml, Does.Contain("<ColumnDefinition Width=\"*\" MinWidth=\"0\" />"));
            Assert.That(xaml, Does.Contain("x:Name=\"ExternalSkinMinimalSkinSelector\""));
            Assert.That(xaml, Does.Contain("Width=\"132\""));
            Assert.That(xaml, Does.Contain("MaxWidth=\"320\""));
            Assert.That(xaml, Does.Contain("TextTrimming=\"CharacterEllipsis\""));
            Assert.That(syncMethod, Does.Contain("GetCachedAvailableSkinDefinitions()"));
            Assert.That(syncMethod, Does.Not.Contain("GetAvailableSkinDefinitions()"));
            Assert.That(refreshMethod, Does.Contain("ResolveExternalSkinDefinitionRefreshMode(reason)"));
            Assert.That(refreshSource, Does.Contain("definition_mode="));
            Assert.That(refreshMethod, Does.Contain("WriteExternalSkinRefreshEndLog("));
            Assert.That(menuActionSource, Does.Contain("\"header-reload\""));
            Assert.That(source, Does.Contain("\"minimal-chrome-reload\""));
            Assert.That(source, Does.Contain("\"fallback-notice-retry\""));
        });
    }

    [Test]
    public void 外部skin_host_refresh_queueは受理できた時だけtrueを返す()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.WebViewSkin.cs");
        string method = GetMethodBlock(
            source,
            "private bool QueueExternalSkinHostRefresh("
        );

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("if (IsExternalSkinHostTeardownStarted())"));
            Assert.That(method, Does.Contain("return false;"));
            Assert.That(method, Does.Contain("bool hasScheduler = _externalSkinHostRefreshScheduler != null;"));
            Assert.That(method, Does.Contain("if (!hasScheduler)"));
            Assert.That(method, Does.Contain("if (!_externalSkinHostRefreshScheduler.CanAcceptQueueRequests)"));
            Assert.That(method, Does.Contain("bool accepted = _externalSkinHostRefreshScheduler.Queue("));
            Assert.That(method, Does.Contain("refresh queue rejected:"));
            Assert.That(method, Does.Contain("UiWorkRequestPolicy.CreateExternalSkinHostRefreshRequest()"));
            Assert.That(method, Does.Contain("UiWorkRequestPolicy.BuildRequestAdmissionLogFields("));
            Assert.That(method, Does.Contain("UiWorkRequestPolicy.ReleaseReasonDeferred"));
            Assert.That(method, Does.Contain("UiWorkRequestPolicy.ReleaseReasonAccepted"));
            Assert.That(method, Does.Contain("UiWorkRequestPolicy.ReleaseReasonRejected"));
            Assert.That(method, Does.Contain("BuildExternalSkinRefreshCoreLogFields("));
            Assert.That(method, Does.Contain("return accepted;"));
            Assert.That(method, Does.Contain("return true;"));
        });
    }

    [Test]
    public void 外部skin_refresh_core接続ログはqueue_begin_batchで同じfieldsを出す()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.WebViewSkin.cs");
        string helperMethod = GetMethodBlock(
            source,
            "private static string BuildExternalSkinRefreshCoreLogFields("
        );
        string queueMethod = GetMethodBlock(
            source,
            "private bool QueueExternalSkinHostRefresh("
        );
        string batchEndMethod = GetMethodBlock(
            source,
            "private void EndExternalSkinHostRefreshBatch("
        );
        string refreshMethod = GetMethodBlock(
            source,
            "private async Task RefreshExternalSkinHostPresentationAsync("
        );

        Assert.Multiple(() =>
        {
            Assert.That(helperMethod, Does.Contain("core_route=skin-refresh"));
            Assert.That(helperMethod, Does.Contain("operation_reason="));
            Assert.That(helperMethod, Does.Contain("UiWorkRequestPolicy.ExternalSkinHostRefreshLogReason"));
            Assert.That(helperMethod, Does.Contain("refresh_reason="));
            Assert.That(helperMethod, Does.Contain("request_trace="));
            Assert.That(helperMethod, Does.Contain("definition_mode="));
            Assert.That(queueMethod, Does.Contain("string deferredCoreFields ="));
            Assert.That(queueMethod, Does.Contain("string queueCoreFields ="));
            Assert.That(queueMethod, Does.Contain("refresh deferred:"));
            Assert.That(queueMethod, Does.Contain("refresh queued:"));
            Assert.That(queueMethod, Does.Contain("refresh queue rejected:"));
            Assert.That(batchEndMethod, Does.Contain("string flushCoreFields ="));
            Assert.That(batchEndMethod, Does.Contain("refresh batch flush:"));
            Assert.That(refreshMethod, Does.Contain("string beginCoreFields ="));
            Assert.That(refreshMethod, Does.Contain("refresh begin:"));
            Assert.That(refreshMethod, Does.Contain("request={requestTraceId}"));
            Assert.That(refreshMethod, Does.Contain("reason={reason}"));
        });
    }

    [Test]
    public void 外部skin_refresh_core_route_helperはPhase7契約fieldsを固定する()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.WebViewSkin.cs");
        string helperMethod = GetMethodBlock(
            source,
            "private static string BuildExternalSkinRefreshCoreLogFields("
        );

        Assert.Multiple(() =>
        {
            Assert.That(helperMethod, Does.Contain("core_route=skin-refresh"));
            Assert.That(helperMethod, Does.Contain("operation_reason="));
            Assert.That(
                helperMethod,
                Does.Contain("UiWorkRequestPolicy.ExternalSkinHostRefreshLogReason")
            );
            Assert.That(helperMethod, Does.Contain("refresh_reason="));
            Assert.That(helperMethod, Does.Contain("request_trace="));
            Assert.That(helperMethod, Does.Contain("definition_mode="));
        });
    }

    [Test]
    public void 外部skin_refresh_core_route_helperはPhase7契約値を返す()
    {
        // 実ログへ出る値そのものを固定し、skin refresh の core route 語彙を戻さない。
        string dbInfoResult = MainWindow.BuildExternalSkinRefreshCoreLogFieldsForTesting(
            "dbinfo-Skin",
            "trace-dbinfo"
        );
        string headerReloadResult = MainWindow.BuildExternalSkinRefreshCoreLogFieldsForTesting(
            "header-reload",
            "trace-header"
        );

        Assert.Multiple(() =>
        {
            Assert.That(
                dbInfoResult,
                Is.EqualTo(
                    "core_route=skin-refresh operation_reason=skin.host-refresh refresh_reason=dbinfo-Skin request_trace=trace-dbinfo definition_mode=CachedSnapshot"
                )
            );
            Assert.That(
                headerReloadResult,
                Is.EqualTo(
                    "core_route=skin-refresh operation_reason=skin.host-refresh refresh_reason=header-reload request_trace=trace-header definition_mode=CatalogRefresh"
                )
            );
        });
    }

    [Test]
    public void 外部skin_refresh_core_route詳細はqueue_begin_batchログ行に同居する()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.WebViewSkin.cs");
        string queueMethod = GetMethodBlock(
            source,
            "private bool QueueExternalSkinHostRefresh("
        );
        string batchEndMethod = GetMethodBlock(
            source,
            "private void EndExternalSkinHostRefreshBatch("
        );
        string refreshMethod = GetMethodBlock(
            source,
            "private async Task RefreshExternalSkinHostPresentationAsync("
        );
        string refreshEndMethod = GetMethodBlock(
            source,
            "private void WriteExternalSkinRefreshEndLog("
        );

        Assert.Multiple(() =>
        {
            Assert.That(
                GetLineContaining(queueMethod, "refresh deferred:"),
                Does.Contain("{deferredCoreFields} {deferredSchedulerFields}")
            );
            Assert.That(
                GetLineContaining(queueMethod, "refresh queued:"),
                Does.Contain("{queueCoreFields} {queueSchedulerFields}")
            );
            Assert.That(
                GetLineContaining(queueMethod, "refresh queue rejected:"),
                Does.Contain("{queueCoreFields} {queueSchedulerFields}")
            );
            Assert.That(
                GetLineContaining(batchEndMethod, "refresh batch flush:"),
                Does.Contain("{flushCoreFields}")
            );
            Assert.That(
                GetLineContaining(refreshMethod, "refresh begin:"),
                Does.Contain("{beginCoreFields}")
            );
            Assert.That(
                GetLineContaining(refreshMethod, "refresh begin:"),
                Does.Contain("reason={reason}")
            );
            Assert.That(refreshEndMethod, Does.Contain("string endCoreFields ="));
            Assert.That(
                refreshEndMethod,
                Does.Contain("BuildExternalSkinRefreshCoreLogFields(")
            );
            Assert.That(
                GetLineContaining(refreshEndMethod, "refresh end:"),
                Does.Contain("{endCoreFields}")
            );
        });
    }

    [Test]
    public void 診断用same_document確認refreshは明示フラグとno_persist時だけ動く()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.WebViewSkin.cs");
        string refreshMethod = GetMethodBlock(
            source,
            "private async Task RefreshExternalSkinHostPresentationAsync("
        );
        string diagnosticMethod = GetMethodBlock(
            source,
            "private void QueueDiagnosticRepeatExternalSkinRefreshIfNeeded("
        );

        Assert.Multiple(() =>
        {
            Assert.That(MainWindow.IsDiagnosticRepeatSkinRefreshEnabledForTesting("1"), Is.True);
            Assert.That(MainWindow.IsDiagnosticRepeatSkinRefreshEnabledForTesting(" true "), Is.True);
            Assert.That(MainWindow.IsDiagnosticRepeatSkinRefreshEnabledForTesting("0"), Is.False);
            Assert.That(MainWindow.IsDiagnosticRepeatSkinRefreshEnabledForTesting(""), Is.False);
            Assert.That(MainWindow.IsDiagnosticRepeatSkinRefreshEnabledForTesting(null), Is.False);
            Assert.That(
                source,
                Does.Contain("INDIGO_DIAGNOSTIC_REPEAT_SKIN_REFRESH")
            );
            Assert.That(refreshMethod, Does.Contain("QueueDiagnosticRepeatExternalSkinRefreshIfNeeded("));
            Assert.That(diagnosticMethod, Does.Contain("!App.IsDiagnosticNoPersistEnabled()"));
            Assert.That(diagnosticMethod, Does.Contain("!IsDiagnosticRepeatSkinRefreshEnabled()"));
            Assert.That(diagnosticMethod, Does.Contain("operationResult?.NavigateSkipped == true"));
            Assert.That(diagnosticMethod, Does.Contain("Interlocked.Exchange(ref _diagnosticRepeatedExternalSkinRefreshRequested, 1)"));
            Assert.That(diagnosticMethod, Does.Contain("QueueExternalSkinHostRefresh(\"dbinfo-Skin\")"));
            Assert.That(diagnosticMethod, Does.Contain("diagnostic repeat skin refresh queued:"));
        });
    }

    [Test]
    public void 外部skin_host_clearはblank遷移時間をruntime_logへ分けて出す()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.WebViewSkin.Chrome.cs");
        string method = GetMethodBlock(
            source,
            "private async Task ClearExternalSkinHostBeforeRefreshAsync("
        );

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("Stopwatch clearStopwatch = Stopwatch.StartNew();"));
            Assert.That(method, Does.Contain("host clear begin: reason={reason} has_host={hasHostText}"));
            Assert.That(
                method,
                Does.Contain(
                    "host clear end: reason={reason} has_host={hasHostText} elapsed_ms={clearStopwatch.ElapsedMilliseconds}"
                )
            );
            Assert.That(
                method,
                Does.Contain(
                    "host clear failed: reason={reason} has_host={hasHostText} type={ex.GetType().Name} elapsed_ms={clearStopwatch.ElapsedMilliseconds}"
                )
            );
            Assert.That(method, Does.Contain("await hostControl.ClearAsync();"));
            Assert.That(method, Does.Not.Contain("throw;"));
        });
    }

    [Test]
    public void 外部skin_refresh_reasonごとにcatalog再確認モードを分ける()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                MainWindow.ResolveExternalSkinDefinitionRefreshModeForTesting("header-reload"),
                Is.EqualTo("CatalogRefresh")
            );
            Assert.That(
                MainWindow.ResolveExternalSkinDefinitionRefreshModeForTesting("fallback-notice-retry"),
                Is.EqualTo("CatalogRefresh")
            );
            Assert.That(
                MainWindow.ResolveExternalSkinDefinitionRefreshModeForTesting("minimal-chrome-reload"),
                Is.EqualTo("CachedSnapshot")
            );
            Assert.That(
                MainWindow.ResolveExternalSkinDefinitionRefreshModeForTesting("dbinfo-Skin"),
                Is.EqualTo("CachedSnapshot")
            );
            Assert.That(
                MainWindow.ResolveExternalSkinDefinitionRefreshModeForTesting("skin-tag-mutation"),
                Is.EqualTo("CachedSnapshot")
            );
        });
    }

    [Test]
    public void 外部skin_same_document_skipは通常dbinfo同期だけ許可する()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                MainWindow.IsExternalSkinSameDocumentNavigateSkipAllowedForTesting("dbinfo-Skin"),
                Is.True
            );
            Assert.That(
                MainWindow.IsExternalSkinSameDocumentNavigateSkipAllowedForTesting("dbinfo-DBFullPath"),
                Is.True
            );
            Assert.That(
                MainWindow.IsExternalSkinSameDocumentNavigateSkipAllowedForTesting("dbinfo-ThumbFolder"),
                Is.True
            );
            Assert.That(
                MainWindow.IsExternalSkinSameDocumentNavigateSkipAllowedForTesting("header-reload"),
                Is.False
            );
            Assert.That(
                MainWindow.IsExternalSkinSameDocumentNavigateSkipAllowedForTesting("fallback-notice-retry"),
                Is.False
            );
            Assert.That(
                MainWindow.IsExternalSkinSameDocumentNavigateSkipAllowedForTesting("minimal-chrome-reload"),
                Is.False
            );
            Assert.That(
                MainWindow.IsExternalSkinSameDocumentNavigateSkipAllowedForTesting("skin-tag-mutation"),
                Is.False
            );
            Assert.That(
                MainWindow.IsExternalSkinSameDocumentNavigateSkipAllowedForTesting(""),
                Is.False
            );
        });
    }

    [Test]
    public void 外部skin_refresh_batchではCatalogRefresh系reasonをdbinfoより優先する()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                MainWindow.SelectPreferredExternalSkinHostRefreshReasonForTesting(
                    "dbinfo-DBFullPath",
                    "header-reload"
                ),
                Is.EqualTo("header-reload")
            );
            Assert.That(
                MainWindow.SelectPreferredExternalSkinHostRefreshReasonForTesting(
                    "header-reload",
                    "dbinfo-DBFullPath"
                ),
                Is.EqualTo("header-reload")
            );
            Assert.That(
                MainWindow.SelectPreferredExternalSkinHostRefreshReasonForTesting(
                    "dbinfo-Skin",
                    "fallback-notice-retry"
                ),
                Is.EqualTo("fallback-notice-retry")
            );
            Assert.That(
                MainWindow.SelectPreferredExternalSkinHostRefreshReasonForTesting(
                    "fallback-notice-retry",
                    "dbinfo-ThumbFolder"
                ),
                Is.EqualTo("fallback-notice-retry")
            );
            Assert.That(
                MainWindow.SelectPreferredExternalSkinHostRefreshReasonForTesting(
                    "minimal-chrome-reload",
                    "dbinfo-Skin"
                ),
                Is.EqualTo("dbinfo-Skin")
            );
            Assert.That(
                MainWindow.SelectPreferredExternalSkinHostRefreshReasonForTesting(
                    "header-reload",
                    "minimal-chrome-reload"
                ),
                Is.EqualTo("header-reload")
            );
        });
    }

    [Test]
    public void 外部skinタグ変更後は局所反映だけ行い全体Refreshしない()
    {
        string apiSource = GetRepoText("Views", "Main", "MainWindow.WebViewSkin.Api.cs");
        string method = GetMethodBlock(
            apiSource,
            "private WhiteBrowserSkinTagMutationResult ApplyExternalSkinMovieTagMutation("
        );

        Assert.That(method, Does.Contain("NotifyTagEditorTagIndexChanged(movie);"));
        Assert.That(method, Does.Contain("RefreshViewsAfterTagEditorRecordChange(movie);"));
        Assert.That(method, Does.Contain("_ = QueueExternalSkinHostRefresh(\"skin-tag-mutation\");"));
        Assert.That(method, Does.Not.Match(@"(?m)^\s*Refresh\(\);\s*$"));
    }

    [Test]
    public void 外部skin_sortは通常時に全件reloadへ戻らない()
    {
        string apiSource = GetRepoText("Views", "Main", "MainWindow.WebViewSkin.Api.cs");
        string method = GetMethodBlock(apiSource, "private async Task<bool> SortExternalSkinAsync(");

        Assert.That(method, Does.Contain("await SortDataAsync(resolvedSortId);"));
        Assert.That(method, Does.Not.Contain("FilterAndSort(resolvedSortId, true);"));
        Assert.That(method, Does.Contain("CancelStartupFeed(\"skin-sort\");"));
        Assert.That(method, Does.Contain("await FilterAndSortAsync(resolvedSortId, isGetNew: true);"));
        Assert.That(method, Does.Contain("partial-feed-needs-complete-source"));
    }

    [Test]
    public void 外部skin_UI操作は非UIスレッドからBackground優先度で投げる()
    {
        string apiSource = GetRepoText("Views", "Main", "MainWindow.WebViewSkin.Api.cs");
        string actionMethod = GetMethodBlock(
            apiSource,
            "private Task<T> InvokeExternalSkinUiActionAsync<T>("
        );
        string taskMethod = GetMethodBlock(
            apiSource,
            "private Task<T> InvokeExternalSkinUiTaskAsync<T>("
        );

        Assert.Multiple(() =>
        {
            Assert.That(actionMethod, Does.Contain("Dispatcher.CheckAccess()"));
            Assert.That(actionMethod, Does.Contain("Task.FromResult(action())"));
            Assert.That(actionMethod, Does.Contain("DispatcherPriority.Background"));
            Assert.That(taskMethod, Does.Contain("Dispatcher.CheckAccess()"));
            Assert.That(taskMethod, Does.Contain("return action();"));
            Assert.That(taskMethod, Does.Contain("DispatcherPriority.Background"));
            Assert.That(taskMethod, Does.Contain(".Task.Unwrap()"));
        });
    }

    [Test]
    public void 外部skin_catalog再確認reasonはAsync経路で行う()
    {
        string refreshSource = GetRepoText("Views", "Main", "MainWindow.WebViewSkin.cs");
        string orchestratorSource = GetRepoText("WhiteBrowserSkin", "WhiteBrowserSkinOrchestrator.cs");
        string mainWindowSkinSource = GetRepoText("WhiteBrowserSkin", "MainWindow.Skin.cs");
        string refreshMethod = GetMethodBlock(
            refreshSource,
            "private async Task RefreshExternalSkinHostPresentationAsync("
        );
        string syncDefinitionMethod = GetMethodBlock(
            refreshSource,
            "private WhiteBrowserSkinDefinition GetCurrentExternalSkinDefinition("
        );
        string asyncDefinitionMethod = GetMethodBlock(
            refreshSource,
            "private async Task<WhiteBrowserSkinDefinition> GetCurrentExternalSkinDefinitionAsync("
        );
        string priorityMethod = GetMethodBlock(
            refreshSource,
            "private static int GetExternalSkinHostRefreshReasonPriority("
        );
        string orchestratorAsyncMethod = GetMethodBlock(
            orchestratorSource,
            "public async Task<WhiteBrowserSkinDefinition> RefreshCurrentSkinDefinitionAsync("
        );

        Assert.Multiple(() =>
        {
            Assert.That(refreshMethod, Does.Contain("await GetCurrentExternalSkinDefinitionAsync("));
            Assert.That(refreshMethod, Does.Contain("definitionRefreshMode"));
            Assert.That(refreshMethod, Does.Not.Contain("GetCurrentExternalSkinDefinition("));
            Assert.That(syncDefinitionMethod, Does.Not.Contain("forceCatalogRefresh"));
            Assert.That(syncDefinitionMethod, Does.Not.Contain("RefreshCurrentSkinDefinition("));
            Assert.That(refreshSource, Does.Contain("\"header-reload\" => ExternalSkinDefinitionRefreshMode.CatalogRefresh"));
            Assert.That(refreshSource, Does.Contain("\"fallback-notice-retry\" => ExternalSkinDefinitionRefreshMode.CatalogRefresh"));
            Assert.That(refreshSource, Does.Contain("_ => ExternalSkinDefinitionRefreshMode.CachedSnapshot"));
            Assert.That(priorityMethod, Does.Contain("ResolveExternalSkinDefinitionRefreshMode(reason)"));
            Assert.That(priorityMethod, Does.Not.Contain("\"header-reload\" => 400"));
            Assert.That(priorityMethod, Does.Not.Contain("\"fallback-notice-retry\" => 400"));
            Assert.That(priorityMethod, Does.Contain("\"minimal-chrome-reload\" => 50"));
            Assert.That(refreshSource, Does.Not.Contain("private WhiteBrowserSkinDefinition RefreshCurrentExternalSkinDefinition("));
            Assert.That(asyncDefinitionMethod, Does.Contain("await RefreshCurrentExternalSkinDefinitionAsync()"));
            Assert.That(refreshSource, Does.Contain("private async Task<WhiteBrowserSkinDefinition> RefreshCurrentExternalSkinDefinitionAsync()"));
            Assert.That(mainWindowSkinSource, Does.Contain("RefreshCurrentSkinDefinitionAsync()"));
            Assert.That(orchestratorAsyncMethod, Does.Contain("await Task.Run(() => WhiteBrowserSkinCatalogService.Load(skinRootPath))"));
            Assert.That(orchestratorAsyncMethod, Does.Contain("availableSkinDefinitions = loadedDefinitions;"));
            Assert.That(orchestratorAsyncMethod, Does.Contain("activeSkinDefinition ="));
            Assert.That(orchestratorAsyncMethod, Does.Not.Contain("ApplySkinByName("));
        });
    }

    [Test]
    public void 外部skin_refresh_endはnavigateとskipを同じpayloadで出す()
    {
        string refreshSource = GetRepoText("Views", "Main", "MainWindow.WebViewSkin.cs");
        string debugLogSource = GetRepoText("Infrastructure", "DebugRuntimeLog.cs");
        string refreshEndMethod = GetMethodBlock(
            refreshSource,
            "private void WriteExternalSkinRefreshEndLog("
        );
        string prepareMethod = GetMethodBlock(
            refreshSource,
            "private async Task<WhiteBrowserSkinHostOperationResult> TryPrepareExternalSkinHostAsync("
        );

        Assert.Multiple(() =>
        {
            Assert.That(refreshEndMethod, Does.Contain("includeZeroValues: true"));
            Assert.That(refreshEndMethod, Does.Contain("metricSummary"));
            Assert.That(refreshEndMethod, Does.Contain("skip_stage="));
            Assert.That(refreshEndMethod, Does.Contain("prepare_ms="));
            Assert.That(refreshEndMethod, Does.Contain("file_prepare_ms="));
            Assert.That(refreshEndMethod, Does.Contain("host_navigate_ms="));
            Assert.That(refreshEndMethod, Does.Contain("initial_doc_ms="));
            Assert.That(refreshEndMethod, Does.Contain("navigate_to_string_ms="));
            Assert.That(refreshSource, Does.Contain("RecordSkinRefreshStaleSkipped()"));
            Assert.That(refreshSource, Does.Contain("RecordSkinRefreshTeardownSkipped()"));
            Assert.That(prepareMethod, Does.Contain("RecordSkinNavigateAttempted()"));
            Assert.That(prepareMethod, Does.Contain("RecordSkinNavigateSucceeded()"));
            Assert.That(prepareMethod, Does.Contain("RecordSkinNavigateFailed()"));
            Assert.That(prepareMethod, Does.Contain("RecordSkinNavigateSkipped()"));
            Assert.That(debugLogSource, Does.Contain("navigate_attempted"));
            Assert.That(debugLogSource, Does.Contain("refresh_stale_skipped"));
            Assert.That(debugLogSource, Does.Contain("refresh_teardown_skipped"));
        });
    }

    [Test]
    public void 外部skin_host_prepareとfallback_logは軽いファイルIOを背景helperへ逃がす()
    {
        string refreshSource = GetRepoText("Views", "Main", "MainWindow.WebViewSkin.cs");
        string chromeSource = GetRepoText("Views", "Main", "MainWindow.WebViewSkin.Chrome.cs");
        string hostSource = GetRepoText(
            "WhiteBrowserSkin",
            "Host",
            "WhiteBrowserSkinHostControl.xaml.cs"
        );
        string renderCoordinatorSource = GetRepoText(
            "WhiteBrowserSkin",
            "Runtime",
            "WhiteBrowserSkinRenderCoordinator.cs"
        );
        string operationResultSource = GetRepoText(
            "WhiteBrowserSkin",
            "Runtime",
            "WhiteBrowserSkinHostOperationResult.cs"
        );
        string prepareMethod = GetMethodBlock(
            refreshSource,
            "private async Task<WhiteBrowserSkinHostOperationResult> TryPrepareExternalSkinHostAsync("
        );
        string prepareIoHelper = GetMethodBlock(
            refreshSource,
            "private static Task<ExternalSkinHostFilePreparationResult> PrepareExternalSkinHostFileSystemAsync("
        );
        string openLogMethod = GetMethodBlock(
            chromeSource,
            "private async void ExternalSkinFallbackOpenLogButton_Click("
        );
        string openLogHelper = GetMethodBlock(
            chromeSource,
            "private static Task<ExternalSkinFallbackLogExplorerTarget> ResolveExternalSkinFallbackLogExplorerTargetAsync("
        );
        string navigateMethod = GetMethodBlock(
            hostSource,
            "public async Task<WhiteBrowserSkinHostOperationResult> TryNavigateAsync("
        );
        string refreshEndMethod = GetMethodBlock(
            refreshSource,
            "private void WriteExternalSkinRefreshEndLog("
        );
        string skipPolicyMethod = GetMethodBlock(
            refreshSource,
            "private static bool IsExternalSkinSameDocumentNavigateSkipAllowed("
        );
        string asyncBuildMethod = GetMethodBlock(
            renderCoordinatorSource,
            "public Task<WhiteBrowserSkinRenderDocument> BuildInitialDocumentAsync("
        );
        int prepareBeforeIoGuardIndex = prepareMethod.IndexOf(
            "\"prepare-before-navigate\"",
            StringComparison.Ordinal
        );
        int prepareIoIndex = prepareMethod.IndexOf(
            "await PrepareExternalSkinHostFileSystemAsync(",
            StringComparison.Ordinal
        );
        int documentBuildIndex = navigateMethod.IndexOf(
            "await renderCoordinator.BuildInitialDocumentAsync(",
            StringComparison.Ordinal
        );
        int navigateSkipIndex = navigateMethod.IndexOf(
            "CreateNavigateSkipped(requestedSkinName, \"same-document\")",
            StringComparison.Ordinal
        );
        int invalidateReuseKeyIndex = navigateMethod.IndexOf(
            "lastSuccessfulNavigationKey = null;",
            navigateSkipIndex,
            StringComparison.Ordinal
        );
        int handleSkinLeaveIndex = navigateMethod.IndexOf(
            "await runtimeBridge.HandleSkinLeaveAsync()",
            StringComparison.Ordinal
        );
        int navigateToStringIndex = navigateMethod.IndexOf(
            "await NavigateToStringAsync(document.Html)",
            StringComparison.Ordinal
        );

        Assert.Multiple(() =>
        {
            Assert.That(prepareMethod, Does.Contain("await PrepareExternalSkinHostFileSystemAsync("));
            Assert.That(prepareMethod, Does.Contain("filePrepareStopwatch"));
            Assert.That(prepareMethod, Does.Contain("hostNavigateStopwatch"));
            Assert.That(prepareMethod, Does.Contain("filePrepareElapsedMilliseconds"));
            Assert.That(prepareMethod, Does.Contain("hostNavigateElapsedMilliseconds"));
            Assert.That(prepareMethod, Does.Contain("\"HostNavigateReturnedNull\""));
            Assert.That(prepareMethod, Does.Contain("External skin host navigate returned null."));
            Assert.That(prepareMethod, Does.Contain("IsExternalSkinSameDocumentNavigateSkipAllowed("));
            Assert.That(prepareMethod, Does.Contain("ResolveExternalSkinDefinitionRefreshMode(reason)"));
            Assert.That(prepareMethod, Does.Contain("navigateResult?.NavigateSkipped == true"));
            Assert.That(prepareMethod, Does.Contain("skip_reason='{navigateResult.NavigateSkipReason}'"));
            Assert.That(prepareMethod, Does.Not.Contain("navigateResult ?? WhiteBrowserSkinHostOperationResult.CreateSuccess("));
            Assert.That(prepareMethod, Does.Not.Contain("File.Exists("));
            Assert.That(prepareMethod, Does.Not.Contain("Directory.CreateDirectory("));
            Assert.That(prepareMethod, Does.Contain("\"prepare-before-navigate\""));
            Assert.That(prepareBeforeIoGuardIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(prepareIoIndex, Is.GreaterThan(prepareBeforeIoGuardIndex));
            Assert.That(prepareIoHelper, Does.Contain("Task.Run("));
            Assert.That(prepareIoHelper, Does.Contain("File.Exists(htmlPath)"));
            Assert.That(prepareIoHelper, Does.Contain("Directory.CreateDirectory(userDataFolder)"));

            Assert.That(openLogMethod, Does.Contain("await ResolveExternalSkinFallbackLogExplorerTargetAsync("));
            Assert.That(openLogMethod, Does.Contain("Process.Start(\"explorer.exe\", explorerTarget.Arguments);"));
            Assert.That(openLogMethod, Does.Not.Contain("File.Exists("));
            Assert.That(openLogMethod, Does.Not.Contain("Directory.Exists("));
            Assert.That(openLogHelper, Does.Contain("Task.Run("));
            Assert.That(openLogHelper, Does.Contain("File.Exists(logPath)"));
            Assert.That(openLogHelper, Does.Contain("Directory.Exists(targetDirectory)"));
            Assert.That(openLogHelper, Does.Not.Contain("Process.Start("));

            Assert.That(navigateMethod, Does.Contain("await runtimeBridge.HandleSkinLeaveAsync()"));
            Assert.That(navigateMethod, Does.Contain("await renderCoordinator.BuildInitialDocumentAsync("));
            Assert.That(navigateMethod, Does.Not.Contain("renderCoordinator.BuildInitialDocument("));
            Assert.That(navigateMethod, Does.Contain("await ResumeOnHostDispatcherAsync()"));
            Assert.That(navigateMethod, Does.Contain("await NavigateToStringAsync(document.Html)"));
            Assert.That(navigateMethod, Does.Contain("allowSameDocumentNavigateSkip"));
            Assert.That(navigateMethod, Does.Contain("CreateNavigateSkipped(requestedSkinName, \"same-document\")"));
            Assert.That(navigateMethod, Does.Contain("lastSuccessfulNavigationKey"));
            Assert.That(navigateMethod, Does.Contain("initialDocumentStopwatch"));
            Assert.That(navigateMethod, Does.Contain("navigateToStringStopwatch"));
            Assert.That(navigateMethod, Does.Contain("initialDocumentBuildElapsedMilliseconds"));
            Assert.That(navigateMethod, Does.Contain("navigateToStringElapsedMilliseconds"));
            Assert.That(documentBuildIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(navigateSkipIndex, Is.GreaterThan(documentBuildIndex));
            Assert.That(invalidateReuseKeyIndex, Is.GreaterThan(navigateSkipIndex));
            Assert.That(handleSkinLeaveIndex, Is.GreaterThan(invalidateReuseKeyIndex));
            Assert.That(handleSkinLeaveIndex, Is.GreaterThan(navigateSkipIndex));
            Assert.That(navigateToStringIndex, Is.GreaterThan(handleSkinLeaveIndex));
            Assert.That(refreshEndMethod, Does.Contain("navigate_skipped_current={(operationResult?.NavigateSkipped == true)}"));
            Assert.That(refreshEndMethod, Does.Contain("navigate_skip_reason='{operationResult?.NavigateSkipReason ?? \"\"}'"));
            Assert.That(skipPolicyMethod, Does.Contain("ExternalSkinDefinitionRefreshMode.CachedSnapshot"));
            Assert.That(skipPolicyMethod, Does.Contain("StartsWith(\"dbinfo-\", StringComparison.Ordinal)"));
            Assert.That(operationResultSource, Does.Contain("PrepareElapsedMilliseconds"));
            Assert.That(operationResultSource, Does.Contain("FilePrepareElapsedMilliseconds"));
            Assert.That(operationResultSource, Does.Contain("HostNavigateElapsedMilliseconds"));
            Assert.That(operationResultSource, Does.Contain("InitialDocumentBuildElapsedMilliseconds"));
            Assert.That(operationResultSource, Does.Contain("NavigateToStringElapsedMilliseconds"));
            Assert.That(operationResultSource, Does.Contain("NavigateSkipped"));
            Assert.That(operationResultSource, Does.Contain("NavigateSkipReason"));
            Assert.That(operationResultSource, Does.Contain("CreateNavigateSkipped"));
            Assert.That(operationResultSource, Does.Contain("succeeded: true"));
            Assert.That(operationResultSource, Does.Contain("errorType: \"HostNavigateSkippedSameDocument\""));
            Assert.That(operationResultSource, Does.Contain("navigateSkipped: true"));
            Assert.That(operationResultSource, Does.Contain("navigateSkipReason: reason"));
            Assert.That(operationResultSource, Does.Contain("WithTimings("));
            Assert.That(asyncBuildMethod, Does.Contain("Task.Run(() => BuildInitialDocument("));
        });
    }

    private static string GetRepoText(params string[] relativePathParts)
    {
        foreach (string startDirectory in EnumerateRepoSearchDirectories())
        {
            if (string.IsNullOrWhiteSpace(startDirectory))
            {
                continue;
            }

            DirectoryInfo? current = new(startDirectory);
            while (current != null)
            {
                string candidate = Path.Combine([current.FullName, .. relativePathParts]);
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }

                current = current.Parent;
            }
        }

        Assert.Fail($"{Path.Combine(relativePathParts)} の位置を repo root から解決できませんでした。");
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

    private static IEnumerable<string> EnumerateRepoSearchDirectories(
        [CallerFilePath] string callerFilePath = ""
    )
    {
        yield return Path.GetDirectoryName(callerFilePath) ?? "";
        yield return Environment.GetEnvironmentVariable("IMM_REPO_ROOT") ?? "";
        yield return TestContext.CurrentContext.TestDirectory;
        yield return TestContext.CurrentContext.WorkDirectory;
        yield return Directory.GetCurrentDirectory();
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

    private static string GetXmlElementBlock(
        string source,
        string startTag,
        string endTag,
        string marker
    )
    {
        int markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.That(markerIndex, Is.GreaterThanOrEqualTo(0), $"{marker} が見つかりません。");

        int start = source.LastIndexOf(startTag, markerIndex, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{marker} を含む {startTag} が見つかりません。");

        int end = source.IndexOf(endTag, markerIndex, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThanOrEqualTo(0), $"{marker} を含む {endTag} が見つかりません。");

        return source.Substring(start, end - start + endTag.Length);
    }

    private static string GetXmlStartTag(string source, string marker)
    {
        int markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.That(markerIndex, Is.GreaterThanOrEqualTo(0), $"{marker} が見つかりません。");

        // x:Name を持つ開始タグだけを切り出し、配置の固定値を局所的に確認する。
        int start = source.LastIndexOf('<', markerIndex);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{marker} の開始タグが見つかりません。");

        int end = source.IndexOf('>', markerIndex);
        Assert.That(end, Is.GreaterThanOrEqualTo(0), $"{marker} の開始タグ終端が見つかりません。");

        return source.Substring(start, end - start + 1);
    }
}
