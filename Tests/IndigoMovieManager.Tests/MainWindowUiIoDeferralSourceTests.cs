using System.IO;
using System.Runtime.CompilerServices;

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
        string source = GetRepoText("Views", "Main", "MainWindow.Lifecycle.cs");
        string contentRendered = ExtractMethod(source, "private void MainWindow_ContentRendered(");

        Assert.That(contentRendered, Does.Contain("EnsureThumbnailProgressUiTimerRunning();"));
        Assert.That(contentRendered, Does.Not.Contain("RequestThumbnailProgressSnapshotRefresh();"));
        Assert.That(contentRendered, Does.Not.Contain("UpdateThumbnailProgressSnapshotUi();"));
    }

    [Test]
    public void ContentRenderedではStartupAutoOpenLastDoc存在確認を直接実行しない()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.Lifecycle.cs");
        string contentRendered = ExtractMethod(source, "private void MainWindow_ContentRendered(");

        Assert.That(contentRendered, Does.Contain("QueueStartupAutoOpenLastDocSwitch();"));
        Assert.That(contentRendered, Does.Not.Contain("Path.Exists("));
        Assert.That(contentRendered, Does.Not.Contain("TrySwitchMainDb("));
    }

    [Test]
    public void StartupAutoOpenLastDoc存在確認は背景で実行しUI側で切り替える()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.Lifecycle.cs");
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
        string mainDbRuntimeSource = GetRepoText(
            "Views",
            "Main",
            "MainWindow.MainDbRuntime.cs"
        );
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
            mainDbRuntimeSource,
            "private bool OpenDatafile(string dbFullPath, DataTable preflightSystemData = null)"
        );
        string bootMethod = ExtractMethod(
            mainDbRuntimeSource,
            "private void BootNewDb(string dbFullPath, DataTable preflightSystemData)"
        );
        string systemMethod = ExtractMethod(
            mainDbRuntimeSource,
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
    public void MainDbRuntime境界はMainWindow本体へ戻さない()
    {
        string mainWindowSource = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string mainDbRuntimeSource = GetRepoText(
            "Views",
            "Main",
            "MainWindow.MainDbRuntime.cs"
        );
        string dbSwitchSource = GetRepoText("Views", "Main", "MainWindow.DbSwitch.cs");

        string[] runtimeSignatures =
        [
            "private void ResetMainHeaderCounts()",
            "private void QueueRegisteredMovieCountRefresh(",
            "private void TryAdjustRegisteredMovieCount(",
            "private async Task RefreshRegisteredMovieCountAsync(",
            "private bool OpenDatafile(string dbFullPath, DataTable preflightSystemData = null)",
            "private void ShutdownCurrentDb()",
            "private void BootNewDb(string dbFullPath, DataTable preflightSystemData)",
            "private void ApplyColdStartSystemDefaults()",
            "public string SelectSystemTable(",
            "private void ApplyRuntimeSystemValue(",
            "private void UpsertSystemDataRow(",
            "private void QueueSearchHistoryReload(",
            "private async Task ReloadSearchHistoryForDbSwitchAsync(",
            "private void GetSystemTable(",
            "private void GetWatchTable(",
            "private static DataTable GetWatchTableSnapshot(",
            "private void UpdateSort()",
            "private void UpdateSkin()",
            "private void SwitchTab(",
        ];

        foreach (string signature in runtimeSignatures)
        {
            Assert.That(mainDbRuntimeSource, Does.Contain(signature));
            Assert.That(mainWindowSource, Does.Not.Contain(signature));
        }

        Assert.That(dbSwitchSource, Does.Contain("private async Task<bool> TrySwitchMainDb("));
        Assert.That(dbSwitchSource, Does.Contain("private void RunMainDbPreSwitch("));
        Assert.That(dbSwitchSource, Does.Contain("private void RunMainDbPostSwitch("));
        Assert.That(mainDbRuntimeSource, Does.Contain("BeginExternalSkinHostRefreshBatch(\"dbinfo-DBFullPath\")"));
        Assert.That(mainDbRuntimeSource, Does.Contain("ApplySkinByName(skin, persistToCurrentDb: false)"));
        Assert.That(mainDbRuntimeSource, Does.Contain("WatchTableRowNormalizer.Normalize(snapshot);"));
        Assert.That(mainDbRuntimeSource, Does.Contain("Volatile.Read(ref _registeredMovieCountRevision)"));
        Assert.That(mainDbRuntimeSource, Does.Contain("DispatcherPriority.Background"));
    }

    [Test]
    public void LifecycleとDockLayout境界はMainWindow本体へ戻さない()
    {
        string mainWindowSource = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string lifecycleSource = GetRepoText("Views", "Main", "MainWindow.Lifecycle.cs");
        string dockLayoutSource = GetRepoText("Views", "Main", "MainWindow.DockLayout.cs");
        string dockLayoutPolicySource = GetRepoText(
            "Views",
            "Main",
            "DockLayoutRestorePolicy.cs"
        );

        string[] lifecycleSignatures =
        [
            "private void MainWindow_ContentRendered(",
            "private void QueueStartupAutoOpenLastDocSwitch(",
            "private async Task RunStartupAutoOpenLastDocSwitchAsync(",
            "private bool IsStartupAutoOpenLastDocSnapshotCurrent(",
            "private bool IsStartupAutoOpenLastDocSwitchShutdownStarted()",
            "internal static string ResolveDiagnosticStartupDbOverrideForTesting(",
            "private void MainWindow_Closing(",
            "private static void WaitBackgroundTaskForShutdown(",
            "private static void WaitBackgroundTasksForShutdown(",
        ];

        foreach (string signature in lifecycleSignatures)
        {
            Assert.That(lifecycleSource, Does.Contain(signature));
            Assert.That(mainWindowSource, Does.Not.Contain(signature));
        }

        string[] dockLayoutSignatures =
        [
            "private void TryRestoreDockLayout()",
            "private async Task RunRestoreDockLayoutAsync(",
            "private async Task<bool> TryRestoreDockLayoutFromFile(",
            "private DockLayoutRestoreFileLoadResult LoadDockLayoutRestoreText(",
            "private bool TryDeserializeDockLayoutText(",
            "internal static string FindMissingRequiredDockLayoutReason(",
            "private sealed record DockLayoutRestoreFileLoadResult(",
            "private void EnsureRequiredBottomTabsPresent()",
            "private void SaveDockLayoutToFile(",
            "private static void BackupLegacyDockLayout(",
            "private void RestoreWindowBoundsSafely()",
        ];

        foreach (string signature in dockLayoutSignatures)
        {
            Assert.That(dockLayoutSource, Does.Contain(signature));
            Assert.That(mainWindowSource, Does.Not.Contain(signature));
        }

        Assert.That(lifecycleSource, Does.Contain("SkipMainWindowClosingSideEffectsForTesting || App.IsDiagnosticNoPersistEnabled()"));
        Assert.That(lifecycleSource, Does.Contain("QueueApplicationSettingsSave(\"main-window-closing\")"));
        Assert.That(lifecycleSource, Does.Contain("DrainWatchEventPipelinesForShutdown();"));
        Assert.That(dockLayoutSource, Does.Contain("DispatcherPriority.ContextIdle"));
        Assert.That(dockLayoutSource, Does.Contain("DockLayoutRestorePolicy.FindMissingRequiredDockLayoutReason("));
        Assert.That(
            dockLayoutPolicySource,
            Does.Contain("internal static string FindMissingRequiredDockLayoutReason(")
        );
        Assert.That(
            dockLayoutPolicySource,
            Does.Contain("internal static bool ShouldRequireThumbnailErrorBottomTab(")
        );
        Assert.That(dockLayoutPolicySource, Does.Not.Contain("System.Windows"));
        Assert.That(dockLayoutPolicySource, Does.Not.Contain("AvalonDock"));
        Assert.That(dockLayoutPolicySource, Does.Not.Contain("Dispatcher"));
        Assert.That(dockLayoutPolicySource, Does.Not.Contain("LayoutAnchorable"));
        Assert.That(dockLayoutPolicySource, Does.Not.Contain("File."));
        Assert.That(dockLayoutPolicySource, Does.Not.Contain("Path."));
        Assert.That(dockLayoutPolicySource, Does.Not.Contain("DebugRuntimeLog"));
        Assert.That(dockLayoutPolicySource, Does.Not.Contain("MainVM"));
        Assert.That(dockLayoutPolicySource, Does.Not.Contain("ObservableCollection"));
        Assert.That(dockLayoutSource, Does.Contain("EnsureRequiredBottomTabsPresent();"));
    }

    [Test]
    public void StartupAutoOpenLastDoc復帰前に設定Snapshotと終了状態を確認する()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.Lifecycle.cs");
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
        int refreshIndex = fallbackMethod.IndexOf(
            "RefreshStartupFallbackMovieView(sortId, revision, warmPathTrigger);",
            StringComparison.Ordinal
        );
        int queueCallCount =
            fallbackMethod.Split("QueueStartupThumbnailProgressSnapshotRefresh();").Length - 1;

        Assert.That(queueCallCount, Is.EqualTo(1));
        Assert.That(reloadIndex, Is.LessThan(queueIndex));
        Assert.That(queueIndex, Is.LessThan(refreshIndex));
    }

    [Test]
    public void Fallback起動は全件sourceがある時だけmemoryRefreshへ逃がす()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.Startup.cs");
        string fallbackMethod = ExtractMethod(
            source,
            "private void FallbackToLegacyStartupLoad(string sortId, int revision)"
        );
        string refreshMethod = ExtractMethod(
            source,
            "private void RefreshStartupFallbackMovieView("
        );
        string memoryMethod = ExtractMethod(
            source,
            "private async Task RefreshStartupFallbackMovieViewFromCurrentSourceAsync("
        );

        Assert.That(fallbackMethod, Does.Contain("CancelStartupFeed(\"startup-fallback\");"));
        Assert.That(fallbackMethod, Does.Contain("StartStartupHeavyServicesIfNeeded("));
        Assert.That(fallbackMethod, Does.Contain("RefreshStartupFallbackMovieView(sortId, revision, warmPathTrigger);"));
        Assert.That(fallbackMethod, Does.Contain("CreateWatcher();"));
        Assert.That(refreshMethod, Does.Contain("ShouldUseStartupFallbackMemoryRefresh("));
        Assert.That(refreshMethod, Does.Contain("RefreshStartupFallbackMovieViewFromCurrentSourceAsync(sortId, revision);"));
        Assert.That(refreshMethod, Does.Contain("FilterAndSort(sortId, true);"));
        Assert.That(memoryMethod, Does.Contain("RefreshMovieViewFromCurrentSourceAsync("));
        Assert.That(memoryMethod, Does.Contain("\"startup-fallback\""));
        Assert.That(memoryMethod, Does.Contain("UiHangActivityKind.Startup"));
        Assert.That(MainWindow.ShouldUseStartupFallbackMemoryRefresh(true, 1), Is.True);
        Assert.That(MainWindow.ShouldUseStartupFallbackMemoryRefresh(true, 0), Is.True);
        Assert.That(MainWindow.ShouldUseStartupFallbackMemoryRefresh(true, -1), Is.False);
        Assert.That(MainWindow.ShouldUseStartupFallbackMemoryRefresh(false, 1), Is.False);
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
        string source = GetRepoText("Views", "Main", "MainWindow.DockLayout.cs");

        Assert.That(source, Does.Contain("using var reader = new StringReader(loadResult.LayoutText);"));
        Assert.That(source, Does.Not.Contain("using var reader = new StreamReader(layoutFilePath);"));
    }

    [Test]
    public void レイアウト復元入口はファイルIOを直接実行しない()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.DockLayout.cs");
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
        Assert.That(
            loadMethod,
            Does.Contain("DockLayoutRestorePolicy.FindMissingRequiredDockLayoutReason(")
        );

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
        foreach (DirectoryInfo searchRoot in EnumerateRepoSearchRoots())
        {
            DirectoryInfo? current = searchRoot;
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

    private static IEnumerable<DirectoryInfo> EnumerateRepoSearchRoots(
        [CallerFilePath] string callerFilePath = ""
    )
    {
        string? callerDirectory = Path.GetDirectoryName(callerFilePath);
        if (!string.IsNullOrWhiteSpace(callerDirectory))
        {
            yield return new DirectoryInfo(callerDirectory);
        }

        yield return new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        yield return new DirectoryInfo(TestContext.CurrentContext.WorkDirectory);
        yield return new DirectoryInfo(Directory.GetCurrentDirectory());
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
