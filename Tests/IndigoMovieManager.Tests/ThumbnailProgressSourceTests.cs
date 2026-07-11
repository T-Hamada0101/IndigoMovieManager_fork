using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailProgressSourceTests
{
    [Test]
    public void Snapshot計測CSVはUI更新から背景キューへ引き渡す()
    {
        string source = GetThumbnailProgressSource();
        string method = ExtractMethod(
            source,
            "private void UpdateThumbnailProgressSnapshotUi(bool requireVisibleSelection = true)"
        );
        string queueSource = GetRepoText(
            "BottomTabs",
            "ThumbnailProgress",
            "ThumbnailProgressSnapshotMetricsQueue.cs"
        ).Replace("\r\n", "\n");

        Assert.That(method, Does.Contain("_thumbnailProgressSnapshotMetricsQueue.Enqueue("));
        Assert.That(method, Does.Not.Contain("ThumbnailProgressUiMetricsLogger.RecordSnapshotApply("));
        Assert.That(queueSource, Does.Contain("ConcurrentQueue<ThumbnailProgressSnapshotApplyMetric>"));
        Assert.That(queueSource, Does.Contain("Interlocked.CompareExchange(ref drainRunning, 1, 0)"));
        Assert.That(queueSource, Does.Contain("Task.Run(Drain)"));
        Assert.That(queueSource, Does.Contain("ThumbnailProgressUiMetricsLogger.RecordSnapshotApply("));
    }

    [Test]
    public void UpdateThumbnailProgressSnapshotUi_救済Worker用の同期IOを持たない()
    {
        string source = GetThumbnailProgressSource();
        string method = ExtractMethod(
            source,
            "private void UpdateThumbnailProgressSnapshotUi(bool requireVisibleSelection = true)"
        );

        Assert.That(method, Does.Not.Contain("GetLatestRescueDisplayRecord"));
        Assert.That(method, Does.Not.Contain("DeleteMainFailureRecords"));
        Assert.That(method, Does.Not.Contain("File.Exists"));
        Assert.That(method, Does.Not.Contain("ResolveCurrentThumbnailFailureDbService"));
        Assert.That(method, Does.Contain("ResolveCachedThumbnailProgressRescueWorkerSnapshot"));
    }

    [Test]
    public void 救済Worker背景更新はSingleFlightで要求時DB情報を保持する()
    {
        string source = GetThumbnailProgressSource();
        string method = ExtractMethod(
            source,
            "private void RequestThumbnailProgressRescueWorkerSnapshotRefresh()"
        );

        Assert.That(method, Does.Contain("_thumbnailProgressPendingRescueWorkerSnapshotRequest = request;"));
        Assert.That(method, Does.Contain("Interlocked.CompareExchange"));
        Assert.That(method, Does.Contain("_thumbnailProgressRescueWorkerSnapshotRefreshRunning"));
        Assert.That(method, Does.Contain("RunThumbnailProgressRescueWorkerSnapshotRefreshAsync(request)"));
    }

    [Test]
    public void FailureDb読み取りとFileExistsは背景ロード側にだけ置く()
    {
        string source = GetThumbnailProgressSource();
        string updateMethod = ExtractMethod(
            source,
            "private void UpdateThumbnailProgressSnapshotUi(bool requireVisibleSelection = true)"
        );
        string loadMethod = ExtractMethod(
            source,
            "private static ThumbnailProgressRescueWorkerSnapshotResult LoadThumbnailProgressRescueWorkerSnapshotCore("
        );

        Assert.That(updateMethod, Does.Not.Contain("File.Exists"));
        Assert.That(loadMethod, Does.Contain("GetLatestRescueDisplayRecord"));
        Assert.That(loadMethod, Does.Contain("DeleteMainFailureRecords"));
        Assert.That(loadMethod, Does.Contain("File.Exists"));
    }

    [Test]
    public void Stale削除後のErrorSnapshot更新はDispatcher反映側で予約する()
    {
        string source = GetThumbnailProgressSource();
        string runMethod = ExtractMethod(
            source,
            "private async Task RunThumbnailProgressRescueWorkerSnapshotRefreshAsync("
        );
        string loadMethod = ExtractMethod(
            source,
            "private static ThumbnailProgressRescueWorkerSnapshotResult LoadThumbnailProgressRescueWorkerSnapshotCore("
        );
        string applyMethod = ExtractMethod(
            source,
            "private void ApplyThumbnailProgressRescueWorkerSnapshotResult("
        );

        Assert.That(runMethod, Does.Contain("Dispatcher"));
        Assert.That(runMethod, Does.Contain("InvokeAsync"));
        Assert.That(loadMethod, Does.Not.Contain("RequestThumbnailErrorSnapshotRefresh"));
        Assert.That(applyMethod, Does.Contain("RequestThumbnailErrorSnapshotRefresh();"));
    }

    [Test]
    public void Queue進捗通知はRuntime差分がある時だけSnapshot予約する()
    {
        string source = GetRepoText("Thumbnail", "MainWindow.ThumbnailCreation.cs")
            .Replace("\r\n", "\n");
        string checkThumbMethod = ExtractMethod(source, "private async Task CheckThumbAsync(");
        string guardMethod = ExtractMethod(
            source,
            "private void UpdateThumbnailProgressRuntimeAndRequestIfChanged("
        );

        Assert.That(
            checkThumbMethod,
            Does.Contain("UpdateThumbnailProgressRuntimeAndRequestIfChanged(() =>")
        );
        Assert.That(
            checkThumbMethod,
            Does.Not.Contain("RequestThumbnailProgressSnapshotRefresh();")
        );
        Assert.That(guardMethod, Does.Contain("_thumbnailProgressRuntime.CurrentVersion"));
        Assert.That(guardMethod, Does.Not.Contain("CreateSnapshot()"));
        Assert.That(guardMethod, Does.Contain("currentVersion == previousVersion"));
        Assert.That(guardMethod, Does.Contain("RequestThumbnailProgressSnapshotRefresh();"));
    }

    [Test]
    public void Queue投入成功直後の進捗通知はRuntime差分Guard経由にする()
    {
        string source = GetRepoText("Thumbnail", "MainWindow.ThumbnailQueue.cs")
            .Replace("\r\n", "\n");
        string enqueueMethod = ExtractMethod(
            source,
            "private bool TryEnqueueThumbnailJob("
        );

        Assert.That(enqueueMethod, Does.Contain("if (!TryWriteQueueRequest(queueObj))"));
        Assert.That(
            enqueueMethod,
            Does.Contain("UpdateThumbnailProgressRuntimeAndRequestIfChanged(")
        );
        Assert.That(
            enqueueMethod,
            Does.Contain("() => _thumbnailProgressRuntime.RecordEnqueue(queueObj)")
        );
        Assert.That(
            enqueueMethod,
            Does.Not.Contain("RequestThumbnailProgressSnapshotRefresh();")
        );
    }

    [Test]
    public void 初期作成数反映はRuntime差分Guard経由にする()
    {
        string source = GetRepoText("Thumbnail", "MainWindow.ThumbnailQueue.cs")
            .Replace("\r\n", "\n");
        string initialCountMethod = ExtractMethod(
            source,
            "private void QueueThumbnailProgressInitialCreatedCountRefresh()"
        );
        string initialCountAsyncMethod = ExtractMethod(
            source,
            "private async Task RefreshThumbnailProgressInitialCreatedCountAsync("
        );

        Assert.That(
            initialCountMethod,
            Does.Contain("_ = RefreshThumbnailProgressInitialCreatedCountAsync(")
        );
        Assert.That(initialCountAsyncMethod, Does.Contain("Task.Run("));
        Assert.That(initialCountAsyncMethod, Does.Contain(".InvokeAsync("));
        Assert.That(initialCountAsyncMethod, Does.Contain("AreSameMainDbPath("));
        Assert.That(
            initialCountAsyncMethod,
            Does.Contain("UpdateThumbnailProgressRuntimeAndRequestIfChanged(")
        );
        Assert.That(
            initialCountAsyncMethod,
            Does.Contain("_thumbnailProgressRuntime.ApplyInitialTotalCreatedCount(")
        );
        Assert.That(source, Does.Not.Contain(".ContinueWith("));
        Assert.That(source, Does.Not.Contain("task.Result"));
        Assert.That(
            initialCountMethod,
            Does.Not.Contain("RequestThumbnailProgressSnapshotRefresh();")
        );
        Assert.That(
            initialCountAsyncMethod,
            Does.Not.Contain("RequestThumbnailProgressSnapshotRefresh();")
        );
    }

    [Test]
    public void QueueClearの進捗ResetはRuntime差分Guard経由にする()
    {
        string source = GetRepoText("Thumbnail", "MainWindow.ThumbnailQueue.cs")
            .Replace("\r\n", "\n");
        string clearMethod = ExtractMethod(
            source,
            "private void ClearThumbnailQueue("
        );

        Assert.That(
            clearMethod,
            Does.Contain("UpdateThumbnailProgressRuntimeAndRequestIfChanged(")
        );
        Assert.That(
            clearMethod,
            Does.Contain("() => _thumbnailProgressRuntime.Reset()")
        );
        Assert.That(clearMethod, Does.Contain("ThumbnailPreviewCache.Shared.Clear();"));
        Assert.That(clearMethod, Does.Contain("ThumbnailPreviewLatencyTracker.Reset();"));
        Assert.That(clearMethod, Does.Contain("QueueThumbnailProgressInitialCreatedCountRefresh();"));
        Assert.That(
            clearMethod,
            Does.Not.Contain("RequestThumbnailProgressSnapshotRefresh();")
        );
    }

    [Test]
    public void SnapshotRefresh予約はCoalesceとLatestOnlyで1本化する()
    {
        string source = GetThumbnailProgressSource();
        string requestMethod = ExtractMethod(
            source,
            "private void RequestThumbnailProgressSnapshotRefresh()"
        );
        string delayedQueueMethod = ExtractMethod(
            source,
            "private void QueueDelayedThumbnailProgressSnapshotRefresh("
        );
        string delayedRunMethod = ExtractMethod(
            source,
            "private async Task RunDelayedThumbnailProgressSnapshotRefreshAsync("
        );
        string schedulerMethod = ExtractMethod(
            source,
            "private bool TryQueueThumbnailProgressSnapshotRefreshWork("
        );
        string processMethod = ExtractMethod(
            source,
            "private void ProcessThumbnailProgressSnapshotRefreshQueue(UiWorkRequest request)"
        );

        Assert.That(
            requestMethod,
            Does.Contain(
                "UiWorkRequest request = UiWorkRequestPolicy.CreateThumbnailProgressSnapshotRefreshRequest();"
            )
        );
        Assert.That(
            requestMethod,
            Does.Contain("if (!CanAcceptThumbnailProgressSnapshotRefresh(request))")
        );
        Assert.That(
            requestMethod,
            Does.Contain("Interlocked.Exchange(ref _thumbnailProgressSnapshotRefreshRequested, 1)")
        );
        Assert.That(
            requestMethod,
            Does.Contain(
                "Interlocked.CompareExchange(ref _thumbnailProgressSnapshotRefreshQueued, 0, 0) == 1"
            )
        );
        Assert.That(
            requestMethod,
            Does.Contain("QueueDelayedThumbnailProgressSnapshotRefresh(decision.Delay, request);")
        );
        Assert.That(
            source,
            Does.Contain(
                "private readonly UiWorkSchedulerRuntime _uiWorkSchedulerRuntime ="
            )
        );
        Assert.That(
            requestMethod,
            Does.Contain(
                "TryQueueThumbnailProgressSnapshotRefreshWork(request, out UiWorkRequest queuedRequest)"
            )
        );
        Assert.That(requestMethod, Does.Contain("DispatcherPriority.Background"));
        Assert.That(
            requestMethod.IndexOf(
                "QueueDelayedThumbnailProgressSnapshotRefresh(decision.Delay, request);",
                StringComparison.Ordinal
            ),
            Is.LessThan(requestMethod.IndexOf("Dispatcher.BeginInvoke(", StringComparison.Ordinal))
        );
        Assert.That(
            requestMethod.IndexOf(
                "TryQueueThumbnailProgressSnapshotRefreshWork(request, out UiWorkRequest queuedRequest)",
                StringComparison.Ordinal
            ),
            Is.LessThan(requestMethod.IndexOf("Dispatcher.BeginInvoke(", StringComparison.Ordinal))
        );
        Assert.That(
            requestMethod,
            Does.Contain("ProcessThumbnailProgressSnapshotRefreshQueue(queuedRequest)")
        );

        Assert.That(
            delayedQueueMethod,
            Does.Contain(
                "Interlocked.Exchange(ref _thumbnailProgressSnapshotRefreshDelayQueued, 1) == 1"
            )
        );
        Assert.That(
            delayedQueueMethod,
            Does.Contain("RunDelayedThumbnailProgressSnapshotRefreshAsync(delay, request)")
        );
        Assert.That(
            delayedRunMethod,
            Does.Contain("Interlocked.Exchange(ref _thumbnailProgressSnapshotRefreshDelayQueued, 0)")
        );
        Assert.That(
            delayedRunMethod,
            Does.Contain(
                "Interlocked.CompareExchange(ref _thumbnailProgressSnapshotRefreshRequested, 0, 0) == 1"
            )
        );
        Assert.That(delayedRunMethod, Does.Contain("RequestThumbnailProgressSnapshotRefresh();"));
        Assert.That(
            delayedRunMethod,
            Does.Contain("UiWorkRequestPolicy.BuildRequestAdmissionLogFields(")
        );
        Assert.That(delayedRunMethod, Does.Contain("UiWorkRequestPolicy.ReleaseReasonFailed"));
        Assert.That(schedulerMethod, Does.Contain("lock (_uiWorkSchedulerRuntimeSyncRoot)"));
        Assert.That(schedulerMethod, Does.Contain("_uiWorkSchedulerRuntime.Queue(request)"));
        Assert.That(schedulerMethod, Does.Contain("_uiWorkSchedulerRuntime.TryTakeNext()"));
        Assert.That(
            schedulerMethod,
            Does.Contain("UiWorkSchedulerPolicy.BuildAdmissionLogFields(")
        );
        Assert.That(
            schedulerMethod,
            Does.Contain(
                "snapshot refresh scheduler admitted: {UiWorkSchedulerPolicy.BuildAdmissionLogFields(request, queueResult.Decision)} pending_count={queueResult.PendingCount}"
            )
        );
        Assert.That(
            schedulerMethod,
            Does.Contain(
                "snapshot refresh scheduler released: {UiWorkSchedulerPolicy.BuildTakeLogFields(takeResult.PendingRequest, takeResult.Decision, takeResult.PendingCount, UiWorkRequestPolicy.ReleaseReasonReleased)}"
            )
        );

        Assert.That(
            processMethod,
            Does.Contain("Interlocked.Exchange(ref _thumbnailProgressSnapshotRefreshQueued, 0)")
        );
        Assert.That(
            processMethod,
            Does.Contain(
                "Interlocked.CompareExchange(ref _thumbnailProgressSnapshotRefreshRequested, 0, 0) == 1"
            )
        );
        Assert.That(processMethod, Does.Contain("RequestThumbnailProgressSnapshotRefresh();"));
        Assert.That(
            processMethod,
            Does.Contain("UiWorkRequestPolicy.BuildRequestAdmissionLogFields(")
        );
        Assert.That(processMethod, Does.Contain("UiWorkRequestPolicy.ReleaseReasonCompleted"));
        Assert.That(processMethod, Does.Contain("UiWorkRequestPolicy.ReleaseReasonFailed"));
    }

    [Test]
    public void SnapshotRefreshは入力優先中の受付時と実行直前の双方で後送りする()
    {
        string source = GetThumbnailProgressSource();
        string requestMethod = ExtractMethod(
            source,
            "private void RequestThumbnailProgressSnapshotRefresh()"
        );
        string processMethod = ExtractMethod(
            source,
            "private void ProcessThumbnailProgressSnapshotRefreshQueue(UiWorkRequest request)"
        );
        const string priorityGuard = "if (IsUserPriorityWorkActive())";
        const string latestOnlyFlag =
            "Interlocked.Exchange(ref _thumbnailProgressSnapshotRefreshRequested, 1)";
        const string delayed = "QueueDelayedThumbnailProgressSnapshotRefresh(";

        Assert.That(requestMethod, Does.Contain(priorityGuard));
        Assert.That(
            requestMethod.IndexOf(latestOnlyFlag, StringComparison.Ordinal),
            Is.LessThan(requestMethod.IndexOf(priorityGuard, StringComparison.Ordinal))
        );
        Assert.That(requestMethod, Does.Contain(delayed));
        Assert.That(
            requestMethod.IndexOf(priorityGuard, StringComparison.Ordinal),
            Is.LessThan(requestMethod.IndexOf("Dispatcher.BeginInvoke(", StringComparison.Ordinal))
        );

        Assert.That(processMethod, Does.Contain(priorityGuard));
        Assert.That(processMethod, Does.Contain(delayed));
        Assert.That(
            processMethod.IndexOf(priorityGuard, StringComparison.Ordinal),
            Is.LessThan(
                processMethod.IndexOf(
                    "UpdateThumbnailProgressSnapshotUi(requireVisibleSelection: false)",
                    StringComparison.Ordinal
                )
            )
        );
        Assert.That(
            processMethod,
            Does.Contain("ThumbnailProgressSnapshotVisibleCoalesceMs")
        );
    }

    [Test]
    public void SnapshotRefresh予約はShutdownまたはDispatcher未使用時に積み増さない()
    {
        string source = GetThumbnailProgressSource();
        string enabledMethod = ExtractMethod(source, "private bool IsThumbnailProgressUiEnabled()");
        string acceptMethod = ExtractMethod(
            source,
            "private bool CanAcceptThumbnailProgressSnapshotRefresh(UiWorkRequest request)"
        );
        string requestMethod = ExtractMethod(
            source,
            "private void RequestThumbnailProgressSnapshotRefresh()"
        );

        Assert.That(enabledMethod, Does.Contain("Dispatcher != null"));
        Assert.That(enabledMethod, Does.Contain("!Dispatcher.HasShutdownStarted"));
        Assert.That(enabledMethod, Does.Contain("!Dispatcher.HasShutdownFinished"));
        Assert.That(acceptMethod, Does.Contain("UiWorkRequestPolicy.CanAcceptForDispatcher("));
        Assert.That(acceptMethod, Does.Contain("Dispatcher != null"));
        Assert.That(acceptMethod, Does.Contain("Dispatcher?.HasShutdownStarted == true"));
        Assert.That(acceptMethod, Does.Contain("Dispatcher?.HasShutdownFinished == true"));
        Assert.That(
            acceptMethod,
            Does.Contain("UiWorkRequestPolicy.BuildRequestAdmissionLogFields(")
        );
        Assert.That(acceptMethod, Does.Contain("acceptance.ReleaseReason"));
        Assert.That(acceptMethod, Does.Contain("skip_reason={acceptance.SkipReason}"));
        Assert.That(
            requestMethod,
            Does.Contain("if (!CanAcceptThumbnailProgressSnapshotRefresh(request))")
        );

        int guardIndex = requestMethod.IndexOf(
            "if (!CanAcceptThumbnailProgressSnapshotRefresh(request))",
            StringComparison.Ordinal
        );
        int requestFlagIndex = requestMethod.IndexOf(
            "_thumbnailProgressSnapshotRefreshRequested",
            StringComparison.Ordinal
        );
        int beginInvokeIndex = requestMethod.IndexOf(
            "Dispatcher.BeginInvoke(",
            StringComparison.Ordinal
        );

        Assert.That(guardIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(requestFlagIndex, Is.GreaterThan(guardIndex));
        Assert.That(beginInvokeIndex, Is.GreaterThan(guardIndex));
    }

    private static string GetThumbnailProgressSource()
    {
        return GetRepoText(
                "BottomTabs",
                "ThumbnailProgress",
                "MainWindow.BottomTab.ThumbnailProgress.cs"
            )
            .Replace("\r\n", "\n");
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
