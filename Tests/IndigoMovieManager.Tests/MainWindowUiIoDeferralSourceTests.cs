using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MainWindowUiIoDeferralSourceTests
{
    [Test]
    public void DbSwitch後処理の旧Pending削除は背景タスクへ逃がす()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.DbSwitch.cs");

        Assert.That(source, Does.Contain("Task.Run("));
        Assert.That(
            source,
            Does.Contain("DiscardPreviousDbPendingThumbnailQueueItemsInBackground(")
        );
    }

    [Test]
    public void Startup軽サービスのサムネ成功索引プリウォームは背景で実行する()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.Startup.cs");

        Assert.That(source, Does.Contain("Task.Run(() => PrewarmThumbnailSuccessIndexCore("));
        Assert.That(source, Does.Contain("private void PrewarmThumbnailSuccessIndexCore("));
    }

    [Test]
    public void StartupEverythingLiteRoot存在確認は背景Planで実行し後着Guard後に流す()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.Startup.cs");
        string queueMethod = ExtractMethod(
            source,
            "private void QueueEverythingLiteWatchRootPrewarm()"
        );
        string runMethod = ExtractMethod(
            source,
            "private async Task RunEverythingLiteWatchRootPrewarmAsync("
        );
        string planMethod = ExtractMethod(
            source,
            "private EverythingLiteWatchRootPrewarmPlan PrewarmEverythingLiteWatchRoots("
        );
        string guardMethod = ExtractMethod(
            source,
            "private bool IsEverythingLiteWatchRootPrewarmCurrent("
        );

        Assert.That(queueMethod, Does.Contain("RunEverythingLiteWatchRootPrewarmAsync("));
        Assert.That(queueMethod, Does.Not.Contain("Path.Exists("));
        Assert.That(runMethod, Does.Contain("Task.Run("));
        Assert.That(runMethod, Does.Contain("PrewarmEverythingLiteWatchRoots("));
        Assert.That(runMethod, Does.Contain("IsEverythingLiteWatchRootPrewarmCurrent("));
        Assert.That(runMethod, Does.Contain("DispatcherPriority.Background"));
        Assert.That(planMethod, Does.Contain("Path.Exists(watchRoot)"));
        Assert.That(guardMethod, Does.Contain("_startupLoadCoordinator.IsCurrent(revision)"));
        Assert.That(guardMethod, Does.Contain("AreSameMainDbPath("));
        Assert.That(guardMethod, Does.Contain("watchRoots.All("));
    }

    [Test]
    public void ContentRenderedではThumbnailProgressSnapshotを直接更新しない()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string contentRendered = ExtractMethod(source, "private void MainWindow_ContentRendered(");

        Assert.That(contentRendered, Does.Contain("EnsureThumbnailProgressUiTimerRunning();"));
        Assert.That(contentRendered, Does.Not.Contain("RequestThumbnailProgressSnapshotRefresh();"));
        Assert.That(contentRendered, Does.Not.Contain("UpdateThumbnailProgressSnapshotUi();"));
    }

    [Test]
    public void ContentRenderedではStartupAutoOpenLastDoc存在確認を直接実行しない()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string contentRendered = ExtractMethod(source, "private void MainWindow_ContentRendered(");

        Assert.That(contentRendered, Does.Contain("QueueStartupAutoOpenLastDocSwitch();"));
        Assert.That(contentRendered, Does.Not.Contain("Path.Exists("));
        Assert.That(contentRendered, Does.Not.Contain("TrySwitchMainDb("));
    }

    [Test]
    public void StartupAutoOpenLastDoc存在確認は背景で実行しUI側で切り替える()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string queueMethod = ExtractMethod(
            source,
            "private void QueueStartupAutoOpenLastDocSwitch()"
        );
        string runMethod = ExtractMethod(
            source,
            "private async Task RunStartupAutoOpenLastDocSwitchAsync("
        );

        Assert.That(queueMethod, Does.Contain("bool autoOpenSnapshot"));
        Assert.That(queueMethod, Does.Contain("string lastDocSnapshot"));
        Assert.That(queueMethod, Does.Contain("RunStartupAutoOpenLastDocSwitchAsync("));
        Assert.That(runMethod, Does.Contain("Task.Run(() => Path.Exists(lastDocSnapshot))"));
        Assert.That(runMethod, Does.Contain("Dispatcher.InvokeAsync"));
        Assert.That(
            runMethod,
            Does.Contain("return TrySwitchMainDb(")
        );
    }

    [Test]
    public void DbSwitch_preflightは旧DB停止前に背景で実行しsystemDataをBootへ渡す()
    {
        string dbSwitchSource = GetRepoText("Views", "Main", "MainWindow.DbSwitch.cs");
        string mainWindowSource = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string switchMethod = ExtractMethod(
            dbSwitchSource,
            "private async Task<bool> TrySwitchMainDb("
        );
        string preflightMethod = ExtractMethod(
            dbSwitchSource,
            "private Task<MainDbSwitchPreflightResult> RunMainDbSwitchPreflightAsync("
        );
        string activateMethod = ExtractMethod(
            dbSwitchSource,
            "private bool TryActivateMainDbSession("
        );
        string openMethod = ExtractMethod(
            mainWindowSource,
            "private bool OpenDatafile(string dbFullPath, DataTable preflightSystemData = null)"
        );
        string bootMethod = ExtractMethod(
            mainWindowSource,
            "private void BootNewDb(string dbFullPath, DataTable preflightSystemData)"
        );
        string systemMethod = ExtractMethod(
            mainWindowSource,
            "private void GetSystemTable(string dbPath, DataTable preflightSystemData = null)"
        );

        Assert.That(switchMethod, Does.Contain("await RunMainDbSwitchPreflightAsync("));
        Assert.That(switchMethod, Does.Contain("IsMainDbSwitchPreflightCurrent("));
        Assert.That(switchMethod.IndexOf("RunMainDbPreSwitch(", StringComparison.Ordinal), Is.GreaterThan(switchMethod.IndexOf("preflightResult.IsValid", StringComparison.Ordinal)));
        Assert.That(preflightMethod, Does.Contain("Task.Run("));
        Assert.That(preflightMethod, Does.Contain("TryValidateMainDatabaseSchema(targetDbFullPath"));
        Assert.That(preflightMethod, Does.Contain("_mainDbMovieReadFacade.LoadSystemTable("));
        Assert.That(activateMethod, Does.Contain("preflightResult.SystemData"));
        Assert.That(openMethod, Does.Contain("if (preflightSystemData == null)"));
        Assert.That(openMethod, Does.Contain("open fallback preflight: synchronous schema validation"));
        Assert.That(openMethod, Does.Contain("TryValidateMainDatabaseSchema(dbFullPath"));
        Assert.That(openMethod, Does.Not.Contain("_mainDbMovieReadFacade.LoadSystemTable("));
        Assert.That(openMethod, Does.Contain("BootNewDb(dbFullPath, preflightSystemData);"));
        Assert.That(bootMethod, Does.Contain("GetSystemTable(dbFullPath, preflightSystemData);"));
        Assert.That(systemMethod, Does.Contain("system fallback load: synchronous system read"));
        Assert.That(systemMethod, Does.Contain("systemData = preflightSystemData ?? _mainDbMovieReadFacade.LoadSystemTable(dbPath);"));
    }

    [Test]
    public void StartupAutoOpenLastDoc復帰前に設定Snapshotと終了状態を確認する()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string runMethod = ExtractMethod(
            source,
            "private async Task RunStartupAutoOpenLastDocSwitchAsync("
        );
        string guardMethod = ExtractMethod(
            source,
            "private bool IsStartupAutoOpenLastDocSnapshotCurrent("
        );
        string shutdownMethod = ExtractMethod(
            source,
            "private bool IsStartupAutoOpenLastDocSwitchShutdownStarted()"
        );

        Assert.That(runMethod, Does.Contain("IsStartupAutoOpenLastDocSwitchShutdownStarted()"));
        Assert.That(runMethod, Does.Contain("IsStartupAutoOpenLastDocSnapshotCurrent("));
        Assert.That(guardMethod, Does.Contain("autoOpenSnapshot"));
        Assert.That(guardMethod, Does.Contain("Properties.Settings.Default.AutoOpen"));
        Assert.That(guardMethod, Does.Contain("Properties.Settings.Default.LastDoc"));
        Assert.That(guardMethod, Does.Contain("StringComparison.Ordinal"));
        Assert.That(shutdownMethod, Does.Contain("Volatile.Read(ref _mainWindowClosingStarted)"));
        Assert.That(shutdownMethod, Does.Contain("Dispatcher.HasShutdownStarted"));
        Assert.That(shutdownMethod, Does.Contain("Dispatcher.HasShutdownFinished"));
    }

    [Test]
    public void Startup軽サービスでThumbnailProgressSnapshot更新を予約する()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.Startup.cs");
        string deferredServices = ExtractMethod(
            source,
            "private async Task RunStartupDeferredServicesAsync(int revision)"
        );
        string queueMethod = ExtractMethod(
            source,
            "private void QueueStartupThumbnailProgressSnapshotRefresh()"
        );

        Assert.That(
            deferredServices,
            Does.Contain("QueueStartupThumbnailProgressSnapshotRefresh();")
        );
        Assert.That(queueMethod, Does.Contain("RequestThumbnailProgressSnapshotRefresh();"));
        Assert.That(queueMethod, Does.Not.Contain("UpdateThumbnailProgressSnapshotUi();"));
    }

    [Test]
    public void Fallback起動でもThumbnailProgressSnapshot更新を予約する()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.Startup.cs");
        string fallbackMethod = ExtractMethod(
            source,
            "private void FallbackToLegacyStartupLoad(string sortId, int revision)"
        );

        Assert.That(fallbackMethod, Does.Contain("QueueStartupThumbnailProgressSnapshotRefresh();"));
        Assert.That(fallbackMethod, Does.Not.Contain("RequestThumbnailProgressSnapshotRefresh();"));
        Assert.That(fallbackMethod, Does.Not.Contain("UpdateThumbnailProgressSnapshotUi();"));

        int reloadIndex = fallbackMethod.IndexOf("ReloadBookmarkTabData();", StringComparison.Ordinal);
        int queueIndex = fallbackMethod.IndexOf(
            "QueueStartupThumbnailProgressSnapshotRefresh();",
            StringComparison.Ordinal
        );
        int filterIndex = fallbackMethod.IndexOf("FilterAndSort(sortId, true);", StringComparison.Ordinal);
        int queueCallCount =
            fallbackMethod.Split("QueueStartupThumbnailProgressSnapshotRefresh();").Length - 1;

        Assert.That(queueCallCount, Is.EqualTo(1));
        Assert.That(reloadIndex, Is.LessThan(queueIndex));
        Assert.That(queueIndex, Is.LessThan(filterIndex));
    }

    [Test]
    public void Fallback起動のFilterAndSortTrueはDB初期読込復旧の許容fallbackとして残す()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.Startup.cs");
        string fallbackMethod = ExtractMethod(
            source,
            "private void FallbackToLegacyStartupLoad(string sortId, int revision)"
        );

        Assert.That(fallbackMethod, Does.Contain("CancelStartupFeed(\"startup-fallback\");"));
        Assert.That(fallbackMethod, Does.Contain("StartStartupHeavyServicesIfNeeded("));
        Assert.That(fallbackMethod, Does.Contain("FilterAndSort(sortId, true);"));
        Assert.That(fallbackMethod, Does.Contain("CreateWatcher();"));
    }

    [Test]
    public void ThumbnailProgressUi反映は救済workerのDBとファイルIOを直接実行しない()
    {
        string source = GetRepoText(
            "BottomTabs",
            "ThumbnailProgress",
            "MainWindow.BottomTab.ThumbnailProgress.cs"
        );
        string updateMethod = ExtractMethod(
            source,
            "private void UpdateThumbnailProgressSnapshotUi("
        );

        Assert.That(updateMethod, Does.Contain("ResolveCachedThumbnailProgressRescueWorkerSnapshot("));
        Assert.That(updateMethod, Does.Not.Contain("ResolveThumbnailProgressRescueWorkerSnapshot("));
        Assert.That(updateMethod, Does.Not.Contain("ResolveCurrentThumbnailFailureDbService("));
        Assert.That(updateMethod, Does.Not.Contain("GetLatestRescueDisplayRecord("));
        Assert.That(updateMethod, Does.Not.Contain("DeleteMainFailureRecords("));
        Assert.That(updateMethod, Does.Not.Contain("File.Exists("));
    }

    [Test]
    public void ThumbnailProgress救済workerSnapshotは背景で読みDB一致時だけ反映する()
    {
        string source = GetRepoText(
            "BottomTabs",
            "ThumbnailProgress",
            "MainWindow.BottomTab.ThumbnailProgress.cs"
        );
        string runMethod = ExtractMethod(
            source,
            "private async Task RunThumbnailProgressRescueWorkerSnapshotRefreshAsync("
        );
        string applyMethod = ExtractMethod(
            source,
            "private void ApplyThumbnailProgressRescueWorkerSnapshotResult("
        );
        string guardMethod = ExtractMethod(
            source,
            "private bool IsCurrentThumbnailProgressRescueWorkerSnapshotRequest("
        );

        Assert.That(runMethod, Does.Contain("Task.Run("));
        Assert.That(runMethod, Does.Contain("LoadThumbnailProgressRescueWorkerSnapshotCore("));
        Assert.That(applyMethod, Does.Contain("IsCurrentThumbnailProgressRescueWorkerSnapshotRequest("));
        Assert.That(guardMethod, Does.Contain("AreSameMainDbPath("));
        Assert.That(applyMethod, Does.Contain("RequestThumbnailErrorSnapshotRefresh();"));
        Assert.That(applyMethod, Does.Contain("RequestThumbnailProgressSnapshotRefresh();"));
    }

    [Test]
    public void 詳細サムネUI入口は同期IOとFailureDbを直接踏まない()
    {
        string source = GetRepoText(
            "BottomTabs",
            "Extension",
            "MainWindow.BottomTab.Extension.DetailThumbnail.cs"
        );
        string prepareMethod = ExtractMethod(
            source,
            "private void PrepareExtensionDetailThumbnail("
        );
        string ensureMissingMethod = ExtractMethod(
            source,
            "private void EnsureMissingDetailThumbnailCreation("
        );

        Assert.That(prepareMethod, Does.Contain("QueueExtensionDetailThumbnailSnapshotRefresh("));
        Assert.That(prepareMethod, Does.Not.Contain("ResolveExistingExtensionDetailThumbnailPath("));
        Assert.That(prepareMethod, Does.Not.Contain("HasExtensionDetailErrorMarker("));
        Assert.That(prepareMethod, Does.Not.Contain("HasOpenExtensionDetailRescueRequest("));
        Assert.That(prepareMethod, Does.Not.Contain("TryEnqueueMissingExtensionDetailThumbnailManualCreate("));
        Assert.That(prepareMethod, Does.Not.Contain("Path.Exists("));
        Assert.That(prepareMethod, Does.Not.Contain("Directory.Exists("));
        Assert.That(prepareMethod, Does.Not.Contain("ResolveCurrentThumbnailFailureDbService("));

        Assert.That(ensureMissingMethod, Does.Contain("QueueExtensionDetailThumbnailSnapshotRefresh("));
        Assert.That(ensureMissingMethod, Does.Not.Contain("HasOpenExtensionDetailRescueRequest("));
        Assert.That(ensureMissingMethod, Does.Not.Contain("Path.Exists("));
        Assert.That(ensureMissingMethod, Does.Not.Contain("TryEnqueueMissingExtensionDetailThumbnailManualCreate("));
    }

    [Test]
    public void 詳細サムネ確認は背景で実行しDB一致時だけUIへ戻す()
    {
        string source = GetRepoText(
            "BottomTabs",
            "Extension",
            "MainWindow.BottomTab.Extension.DetailThumbnail.cs"
        );
        string runMethod = ExtractMethod(
            source,
            "private async Task RunExtensionDetailThumbnailSnapshotRefreshAsync("
        );
        string coreMethod = ExtractMethod(
            source,
            "private ExtensionDetailThumbnailSnapshotResult LoadExtensionDetailThumbnailSnapshotCore("
        );
        string applyMethod = ExtractMethod(
            source,
            "private void ApplyExtensionDetailThumbnailSnapshotResult("
        );
        string captureMethod = ExtractMethod(
            source,
            "private ExtensionDetailThumbnailSnapshotRequest CaptureExtensionDetailThumbnailSnapshotRequest("
        );
        string guardMethod = ExtractMethod(
            source,
            "private bool IsExtensionDetailThumbnailSnapshotRequestCurrent("
        );
        string shutdownMethod = ExtractMethod(
            source,
            "private bool IsExtensionDetailThumbnailShutdownStarted()"
        );

        Assert.That(runMethod, Does.Contain(".Run(() => LoadExtensionDetailThumbnailSnapshotCore("));
        Assert.That(runMethod, Does.Contain("DispatcherPriority.Background"));
        Assert.That(coreMethod, Does.Contain("ResolveExistingExtensionDetailThumbnailPath(request)"));
        Assert.That(coreMethod, Does.Contain("HasOpenExtensionDetailRescueRequest(request)"));
        Assert.That(coreMethod, Does.Contain("TryEnqueueMissingExtensionDetailThumbnailManualCreate("));
        Assert.That(coreMethod, Does.Contain("TryEnqueueExtensionDetailThumbnailRescue("));
        Assert.That(coreMethod, Does.Not.Contain("MainVM"));
        Assert.That(captureMethod, Does.Contain("MainVM?.DbInfo?.DBFullPath ?? \"\""));
        Assert.That(captureMethod, Does.Not.Contain("Path.Exists("));
        Assert.That(captureMethod, Does.Not.Contain("Directory.Exists("));
        Assert.That(applyMethod, Does.Contain("IsExtensionDetailThumbnailSnapshotRequestCurrent("));
        Assert.That(guardMethod, Does.Contain("AreSameMainDbPath("));
        Assert.That(guardMethod, Does.Contain("GetSelectedItemByTabIndex()"));
        Assert.That(shutdownMethod, Does.Contain("Dispatcher.HasShutdownStarted"));
    }

    [Test]
    public void レイアウト復元は検証済みテキストを再利用し二重読込しない()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.xaml.cs");

        Assert.That(source, Does.Contain("using var reader = new StringReader(loadResult.LayoutText);"));
        Assert.That(source, Does.Not.Contain("using var reader = new StreamReader(layoutFilePath);"));
    }

    [Test]
    public void レイアウト復元入口はファイルIOを直接実行しない()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string restoreMethod = ExtractMethod(source, "private void TryRestoreDockLayout()");
        string restoreFileMethod = ExtractMethod(
            source,
            "private async Task<bool> TryRestoreDockLayoutFromFile("
        );
        string loadMethod = ExtractMethod(
            source,
            "private DockLayoutRestoreFileLoadResult LoadDockLayoutRestoreText("
        );
        string deserializeMethod = ExtractMethod(
            source,
            "private bool TryDeserializeDockLayoutText("
        );

        Assert.That(restoreMethod, Does.Contain("RunRestoreDockLayoutAsync();"));
        Assert.That(restoreMethod, Does.Not.Contain("Path.Exists("));
        Assert.That(restoreMethod, Does.Not.Contain("File.ReadAllText("));

        Assert.That(restoreFileMethod, Does.Contain("Task.Run("));
        Assert.That(restoreFileMethod, Does.Contain("LoadDockLayoutRestoreText("));
        Assert.That(restoreFileMethod, Does.Contain("DispatcherPriority.ContextIdle"));
        Assert.That(restoreFileMethod, Does.Not.Contain("Path.Exists("));
        Assert.That(restoreFileMethod, Does.Not.Contain("File.ReadAllText("));

        Assert.That(loadMethod, Does.Contain("Path.Exists(layoutFilePath)"));
        Assert.That(loadMethod, Does.Contain("File.ReadAllText(layoutFilePath)"));
        Assert.That(loadMethod, Does.Contain("FindMissingRequiredDockLayoutReason("));

        Assert.That(deserializeMethod, Does.Contain("new StringReader(loadResult.LayoutText)"));
        Assert.That(deserializeMethod, Does.Not.Contain("Path.Exists("));
        Assert.That(deserializeMethod, Does.Not.Contain("File.ReadAllText("));
    }

    [Test]
    public void EverythingPoll入口はDB存在確認とwatch存在確認を背景Planへ逃がす()
    {
        string mainWindowSource = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string policySource = GetRepoText(
            "Views",
            "Main",
            "MainWindow.EverythingWatchPollPolicy.cs"
        );
        string loopMethod = ExtractMethod(
            mainWindowSource,
            "private async Task RunEverythingWatchPollLoopAsync("
        );
        string asyncMethod = ExtractMethod(
            policySource,
            "private async Task<bool> ShouldRunEverythingWatchPollPolicyAsync("
        );
        string captureMethod = ExtractMethod(
            policySource,
            "private EverythingWatchPollPlanRequest CaptureEverythingWatchPollPlanRequest()"
        );
        string buildMethod = ExtractMethod(
            policySource,
            "private EverythingWatchPollPlanResult BuildEverythingWatchPollPlan("
        );
        string guardMethod = ExtractMethod(
            policySource,
            "private bool IsCurrentEverythingWatchPollPlan("
        );

        Assert.That(loopMethod, Does.Contain("await ShouldRunEverythingWatchPollPolicyAsync(cts)"));
        Assert.That(loopMethod, Does.Not.Contain("Path.Exists("));
        Assert.That(asyncMethod, Does.Contain("Task.Run("));
        Assert.That(asyncMethod, Does.Contain("BuildEverythingWatchPollPlan(request)"));
        Assert.That(asyncMethod, Does.Contain("IsCurrentEverythingWatchPollPlan(result)"));
        Assert.That(asyncMethod, Does.Not.Contain("Path.Exists("));
        Assert.That(captureMethod, Does.Contain("MainVM?.DbInfo?.DBFullPath ?? \"\""));
        Assert.That(captureMethod, Does.Not.Contain("Path.Exists("));
        Assert.That(buildMethod, Does.Contain("Path.Exists(request.DbPath)"));
        Assert.That(buildMethod, Does.Contain("GetEverythingPollEligibleWatchFoldersSnapshot("));
        Assert.That(buildMethod, Does.Contain("isDbPathKnownToExist: true"));
        Assert.That(buildMethod, Does.Contain("Path.Exists(path)"));
        Assert.That(guardMethod, Does.Contain("Volatile.Read(ref _everythingWatchPollPlanRevision)"));
        Assert.That(guardMethod, Does.Contain("Dispatcher.HasShutdownStarted"));
        Assert.That(guardMethod, Does.Contain("AreSameMainDbPath("));
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

    private static string ExtractMethod(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{signature} が見つかりません。");

        int braceStart = source.IndexOf('{', start);
        Assert.That(braceStart, Is.GreaterThanOrEqualTo(0));

        int depth = 0;
        for (int index = braceStart; index < source.Length; index++)
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

        Assert.Fail($"{signature} の終端を解決できませんでした。");
        return string.Empty;
    }
}
