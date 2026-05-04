using System.IO;
using System.Runtime.CompilerServices;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UiHangOverlayLifecycleSourceTests
{
    [Test]
    public void MainWindowはSourceInitializedとStart時にowner更新を呼ぶ()
    {
        string mainWindowSource = GetSourceText(
            new[] { "Views", "Main", "MainWindow.xaml.cs" }
        );
        string uiHangSource = GetSourceText(
            new[] { "Views", "Main", "MainWindow.UiHangNotification.cs" }
        );

        Assert.That(mainWindowSource, Does.Contain("UpdateUiHangNotificationOwnerWindow();"));
        Assert.That(uiHangSource, Does.Contain("private void UpdateUiHangNotificationOwnerWindow()"));
        Assert.That(
            uiHangSource,
            Does.Contain("_uiHangNotificationCoordinator.UpdateOwnerWindowHandle(windowHandle);")
        );
    }

    [Test]
    public void MainWindow_Closingはwatcher入力停止後にwatch_queueとcreated_pipelineを待つ()
    {
        string mainWindowSource = GetSourceText(new[] { "Views", "Main", "MainWindow.xaml.cs" })
            .Replace("\r\n", "\n");
        string watcherQueueSource = GetSourceText(new[] { "Watcher", "MainWindow.WatcherEventQueue.cs" })
            .Replace("\r\n", "\n");

        Assert.That(mainWindowSource, Does.Contain("StopAndClearFileWatchers();"));
        Assert.That(
            mainWindowSource,
            Does.Contain("BeginWatchEventQueueShutdownForClosing();")
        );
        Assert.That(
            mainWindowSource,
            Does.Contain("DrainWatchEventPipelinesForShutdown();")
        );
        Assert.That(
            watcherQueueSource,
            Does.Contain("private void BeginWatchEventQueueShutdownForClosing()")
        );
        Assert.That(
            watcherQueueSource,
            Does.Contain("private void DrainWatchEventPipelinesForShutdown()")
        );
        Assert.That(
            watcherQueueSource,
            Does.Contain("WaitWatchPipelineTaskForShutdown(")
        );
    }

    [Test]
    public void Watcher作成は背景計画の後着をrevisionで捨てる()
    {
        string mainWindowSource = GetSourceText(new[] { "Views", "Main", "MainWindow.xaml.cs" })
            .Replace("\r\n", "\n");
        string startupSource = GetSourceText(new[] { "Views", "Main", "MainWindow.Startup.cs" })
            .Replace("\r\n", "\n");
        string watcherSource = GetSourceText(new[] { "Watcher", "MainWindow.Watcher.cs" })
            .Replace("\r\n", "\n");
        string watchScanCoordinatorSource = GetSourceText(
                new[] { "Watcher", "MainWindow.WatchScanCoordinator.cs" }
            )
            .Replace("\r\n", "\n");
        string watcherRegistrationSource = GetSourceText(
                new[] { "Watcher", "MainWindow.WatcherRegistration.cs" }
            )
            .Replace("\r\n", "\n");

        Assert.That(watcherRegistrationSource, Does.Contain("_watcherCreationRevision"));
        Assert.That(watcherRegistrationSource, Does.Contain("BuildWatcherCreationPlan("));
        Assert.That(watcherRegistrationSource, Does.Contain("ApplyWatcherCreationPlan("));
        Assert.That(
            watcherRegistrationSource,
            Does.Contain("int currentRevision = Volatile.Read(ref _watcherCreationRevision)")
        );
        Assert.That(
            watcherRegistrationSource,
            Does.Contain("revision != currentRevision")
        );
        Assert.That(
            watcherRegistrationSource,
            Does.Contain("watcher create skipped by stale revision")
        );
        Assert.That(
            watcherRegistrationSource,
            Does.Contain("applyAvailability = _indexProviderFacade.CheckAvailability(plan.IntegrationMode);")
        );
        Assert.That(
            watcherRegistrationSource,
            Does.Contain("TrySetFileWatcherEnabled(item, enabled: true, \"register\")")
        );
        Assert.That(
            watcherRegistrationSource,
            Does.Not.Contain("if (!Path.Exists(watchFolder))")
        );
        Assert.That(
            watcherRegistrationSource,
            Does.Contain("_watcherCreationActiveTaskCount")
        );
        Assert.That(watcherRegistrationSource, Does.Contain("active={activeTaskCount}"));
        Assert.That(mainWindowSource, Does.Contain("InvalidateWatcherCreation(\"window-closing\")"));
        Assert.That(
            mainWindowSource,
            Does.Contain("InvalidateWatcherCreation(\"shutdown-current-db\")")
        );
        Assert.That(
            watcherSource,
            Does.Contain("out DataTable watchTableForScan")
        );
        Assert.That(watcherSource, Does.Contain("foreach (DataRow row in watchTableForScan.Rows)"));
        Assert.That(
            watchScanCoordinatorSource,
            Does.Contain("out DataTable watchTable")
        );
        Assert.That(
            watchScanCoordinatorSource,
            Does.Contain("watchTable = GetWatchTableSnapshot(snapshotDbFullPath, sql);")
        );
        Assert.That(startupSource, Does.Contain("PrewarmEverythingLiteWatchRoots("));
        Assert.That(
            startupSource,
            Does.Contain("DataTable watchTable = GetWatchTableSnapshot(")
        );
        Assert.That(startupSource, Does.Not.Contain("if (watchData == null || watchData.Rows.Count < 1)"));
    }

    [Test]
    public void ExternalSkinApiの非UIスレッド同期読み取りはBackground優先度で渡す()
    {
        string source = GetSourceText(new[] { "Views", "Main", "MainWindow.WebViewSkin.Api.cs" })
            .Replace("\r\n", "\n");
        string method = ExtractMethod(source, "private T ReadExternalSkinUiState<T>(");

        Assert.That(method, Does.Contain("Dispatcher.CheckAccess()"));
        Assert.That(method, Does.Contain("return reader();"));
        Assert.That(method, Does.Contain("Dispatcher.Invoke("));
        Assert.That(method, Does.Contain("DispatcherPriority.Background"));
    }

    [Test]
    public void ExternalSkinThumbnailCallbackはUIスナップショット取得後に背景組み立てする()
    {
        string source = GetSourceText(new[] { "Views", "Main", "MainWindow.WebViewSkin.Api.cs" })
            .Replace("\r\n", "\n");
        string queueMethod = ExtractMethod(source, "private void TryQueueExternalSkinThumbnailUpdated(");
        string callbackQueueMethod = ExtractMethod(
            source,
            "private void QueueExternalSkinThumbnailUpdatedCallback("
        );
        string chainedCallbackMethod = ExtractMethod(
            source,
            "private async Task RunExternalSkinThumbnailUpdatedCallbackAfterAsync("
        );
        string buildDispatchMethod = ExtractMethod(
            source,
            "private async Task BuildAndDispatchExternalSkinThumbnailUpdatedAsync("
        );
        string staleCheckMethod = ExtractMethod(
            source,
            "private bool IsExternalSkinThumbnailCallbackStillCurrent("
        );
        string payloadBuilderMethod = ExtractMethod(
            source,
            "private WhiteBrowserSkinThumbnailUpdateCallbackPayload BuildExternalSkinThumbnailUpdateCallbackPayload("
        );
        string captureContextMethod = ExtractMethod(
            source,
            "CaptureExternalSkinThumbnailUpdateUiContextOnUiThread()\n        {"
        );

        Assert.That(
            queueMethod,
            Does.Contain("callbackUiContext = CaptureExternalSkinThumbnailUpdateUiContextOnUiThread();")
        );
        Assert.That(queueMethod, Does.Contain("MovieRecords movieSnapshot = CloneExternalSkinThumbnailCallbackMovie(movie);"));
        Assert.That(queueMethod, Does.Contain("QueueExternalSkinThumbnailUpdatedCallback("));
        Assert.That(queueMethod, Does.Contain("callbackUiContext.DbFullPath"));
        Assert.That(queueMethod, Does.Contain("callbackUiContext.ThumbFolder"));
        Assert.That(queueMethod, Does.Contain("callbackUiContext.SelectedMovieId"));
        Assert.That(queueMethod, Does.Contain("callbackUiContext.SelectedMovieIds"));
        Assert.That(queueMethod, Does.Not.Contain("BuildExternalSkinThumbnailUpdateCallbackPayload("));

        Assert.That(callbackQueueMethod, Does.Contain("lock (_externalSkinThumbnailCallbackQueueSync)"));
        Assert.That(callbackQueueMethod, Does.Contain("Task previousTask = _externalSkinThumbnailCallbackQueueTask;"));
        Assert.That(callbackQueueMethod, Does.Contain("_externalSkinThumbnailCallbackQueueTask = RunExternalSkinThumbnailUpdatedCallbackAfterAsync("));
        Assert.That(chainedCallbackMethod, Does.Contain("await previousTask.ConfigureAwait(false);"));
        Assert.That(chainedCallbackMethod, Does.Contain("thumbnail callback pipeline failed"));

        Assert.That(captureContextMethod, Does.Contain("GetSelectedItemByTabIndex()"));
        Assert.That(captureContextMethod, Does.Contain("GetSelectedItemsByTabIndex()"));
        Assert.That(captureContextMethod, Does.Contain("ResolveExternalSkinApiTabIndexOnUiThread()"));
        Assert.That(captureContextMethod, Does.Contain("MainVM?.DbInfo?.DBFullPath ?? \"\""));
        Assert.That(captureContextMethod, Does.Contain("MainVM?.DbInfo?.ThumbFolder ?? \"\""));

        Assert.That(buildDispatchMethod, Does.Contain("await Task.Run(() =>"));
        Assert.That(
            buildDispatchMethod,
            Does.Contain("BuildExternalSkinThumbnailUpdateCallbackPayload(")
        );
        Assert.That(
            buildDispatchMethod,
            Does.Contain("thumbnail callback skipped before build by stale host/tab/db")
        );
        Assert.That(buildDispatchMethod, Does.Contain("await InvokeExternalSkinUiTaskAsync("));
        Assert.That(buildDispatchMethod, Does.Contain("IsExternalSkinThumbnailCallbackStillCurrent("));
        Assert.That(buildDispatchMethod, Does.Contain("dbFullPath"));
        Assert.That(
            buildDispatchMethod,
            Does.Contain("hostControl.RegisterExternalThumbnailPath(thumbPath);")
        );
        Assert.That(buildDispatchMethod, Does.Contain("thumbnail callback build failed"));
        Assert.That(buildDispatchMethod, Does.Not.Contain("MainVM?.DbInfo"));
        Assert.That(buildDispatchMethod, Does.Not.Contain("GetSelectedItemByTabIndex("));
        Assert.That(buildDispatchMethod, Does.Not.Contain("GetSelectedItemsByTabIndex("));
        Assert.That(
            buildDispatchMethod,
            Does.Contain("await DispatchExternalSkinThumbnailUpdatedAsync(hostControl, payload, reason);")
        );

        Assert.That(payloadBuilderMethod, Does.Contain("DbFullPath = dbFullPath ?? \"\""));
        Assert.That(payloadBuilderMethod, Does.Contain("ManagedThumbnailRootPath = thumbFolder ?? \"\""));
        Assert.That(payloadBuilderMethod, Does.Contain("SelectedMovieId = selectedMovieId"));
        Assert.That(staleCheckMethod, Does.Contain("string dbFullPath"));
        Assert.That(staleCheckMethod, Does.Contain("AreSameMainDbPath("));
        Assert.That(payloadBuilderMethod, Does.Contain("SelectedMovieIds = selectedMovieIds ?? []"));
        Assert.That(payloadBuilderMethod, Does.Contain("ResolveExternalSkinThumbUrlFromSnapshot("));
        Assert.That(payloadBuilderMethod, Does.Not.Contain("MainVM?.DbInfo"));
        Assert.That(payloadBuilderMethod, Does.Not.Contain("GetSelectedItemByTabIndex("));
        Assert.That(payloadBuilderMethod, Does.Not.Contain("GetSelectedItemsByTabIndex("));
    }

    [Test]
    public void NativeOverlayHostはowner付き生成と停止時即hideを持つ()
    {
        string source = GetSourceText(new[] { "Views", "Main", "NativeOverlayHost.cs" });
        string overlayThreadMain = ExtractMethod(source, "private void OverlayThreadMain()");

        Assert.That(source, Does.Contain("_ownerWindowHandle"));
        Assert.That(source, Does.Contain("internal void UpdateOwnerWindowHandle(nint ownerWindowHandle)"));
        Assert.That(source, Does.Contain("_ownerWindowHandle,"));
        Assert.That(source, Does.Contain("ForceHideNativeOverlayImmediately(overlayHwnd);"));
        Assert.That(source, Does.Contain("RequestOverlayClose(overlayHwnd);"));
        Assert.That(source, Does.Contain("_overlayThread != null && _overlayThread.IsAlive"));
        Assert.That(source, Does.Contain("overlay thread start skipped; previous thread still alive"));
        Assert.That(source, Does.Contain("private void HandleOverlayDispatcherUnhandledException("));
        Assert.That(source, Does.Contain("overlay dispatcher action failed"));
        Assert.That(source, Does.Contain("e.Handled = true;"));
        Assert.That(overlayThreadMain, Does.Contain("try"));
        Assert.That(overlayThreadMain, Does.Contain("UnhandledException +="));
        Assert.That(overlayThreadMain, Does.Contain("UnhandledException -="));
        Assert.That(overlayThreadMain, Does.Contain("catch (Exception ex)"));
        Assert.That(overlayThreadMain, Does.Contain("overlay thread failed"));
        Assert.That(overlayThreadMain, Does.Contain("finally"));
        Assert.That(overlayThreadMain, Does.Contain("DestroyOverlayOnCurrentThread();"));
        Assert.That(overlayThreadMain, Does.Contain("ReferenceEquals(_overlayDispatcher, currentDispatcher)"));
        Assert.That(overlayThreadMain, Does.Contain("ReferenceEquals(_overlayThread, Thread.CurrentThread)"));
        Assert.That(overlayThreadMain, Does.Contain("overlay thread destroyed"));
    }

    [Test]
    public void NativeOverlayHostNativeMethodsはowner変更とclose要求を持つ()
    {
        string source = GetSourceText(
            new[] { "Views", "Main", "NativeOverlayHost.NativeMethods.cs" }
        );

        Assert.That(source, Does.Contain("GwlHwndParent = -8"));
        Assert.That(source, Does.Contain("WM_CLOSE = 0x0010"));
        Assert.That(source, Does.Contain("private static extern bool PostMessage("));
    }

    private static string GetSourceText(
        string[] relativeSegments,
        [CallerFilePath] string testSourcePath = ""
    )
    {
        string? repoRootFromSource = ResolveRepoRootFromCallerSource(testSourcePath);
        if (!string.IsNullOrEmpty(repoRootFromSource))
        {
            string[] sourceSegments = new string[relativeSegments.Length + 1];
            sourceSegments[0] = repoRootFromSource;
            Array.Copy(relativeSegments, 0, sourceSegments, 1, relativeSegments.Length);
            string sourceCandidate = Path.Combine(sourceSegments);
            if (File.Exists(sourceCandidate))
            {
                return File.ReadAllText(sourceCandidate);
            }
        }

        string[] cwdSegments = new string[relativeSegments.Length + 1];
        cwdSegments[0] = Directory.GetCurrentDirectory();
        Array.Copy(relativeSegments, 0, cwdSegments, 1, relativeSegments.Length);
        string cwdCandidate = Path.Combine(cwdSegments);
        if (File.Exists(cwdCandidate))
        {
            return File.ReadAllText(cwdCandidate);
        }

        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current != null)
        {
            string[] segments = new string[relativeSegments.Length + 1];
            segments[0] = current.FullName;
            Array.Copy(relativeSegments, 0, segments, 1, relativeSegments.Length);
            string candidate = Path.Combine(segments);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        Assert.Fail($"source file を repo root から解決できませんでした: {string.Join(Path.DirectorySeparatorChar, relativeSegments)}");
        return string.Empty;
    }

    private static string? ResolveRepoRootFromCallerSource(string testSourcePath)
    {
        if (string.IsNullOrWhiteSpace(testSourcePath))
        {
            return null;
        }

        string? sourceDirectory = Path.GetDirectoryName(testSourcePath);
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            return null;
        }

        DirectoryInfo? current = new(sourceDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, "IndigoMovieManager.sln");
            if (File.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string ExtractMethod(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{signature} が見つかりません。");

        int braceStart = source.IndexOf('{', start);
        Assert.That(braceStart, Is.GreaterThanOrEqualTo(0), $"{signature} の開始波括弧が見つかりません。");

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

        Assert.Fail($"{signature} の終端が見つかりません。");
        return string.Empty;
    }
}
