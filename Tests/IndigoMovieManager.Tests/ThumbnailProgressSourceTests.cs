using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailProgressSourceTests
{
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
