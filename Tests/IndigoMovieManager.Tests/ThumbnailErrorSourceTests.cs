using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailErrorSourceTests
{
    [Test]
    public void ShouldPollThumbnailErrorProgress_UI上でFailureDbを読まない()
    {
        string source = GetRepoText(
            "BottomTabs",
            "ThumbnailError",
            "MainWindow.BottomTab.ThumbnailError.Progress.cs"
        );
        string method = ExtractMethod(source, "private bool ShouldPollThumbnailErrorProgress()");

        Assert.That(method, Does.Contain("RequestThumbnailErrorPendingRescueWorkRefresh(dbFullPath);"));
        Assert.That(method, Does.Contain("HasCachedThumbnailErrorPendingRescueWork(dbFullPath)"));
        Assert.That(method, Does.Not.Contain("ResolveCurrentThumbnailFailureDbService"));
        Assert.That(method, Does.Not.Contain("HasPendingRescueWork"));
    }

    [Test]
    public void ThumbnailErrorPendingRescueWorkは背景で読みDB一致時だけ反映する()
    {
        string source = GetRepoText(
            "BottomTabs",
            "ThumbnailError",
            "MainWindow.BottomTab.ThumbnailError.Progress.cs"
        );
        string runMethod = ExtractMethod(
            source,
            "private async Task RunThumbnailErrorPendingRescueWorkRefreshAsync("
        );
        string loadMethod = ExtractMethod(
            source,
            "private static bool LoadThumbnailErrorPendingRescueWorkCore("
        );
        string applyMethod = ExtractMethod(
            source,
            "private void ApplyThumbnailErrorPendingRescueWorkResult("
        );

        Assert.That(runMethod, Does.Contain(".Run(() => LoadThumbnailErrorPendingRescueWorkCore("));
        Assert.That(loadMethod, Does.Contain("HasPendingRescueWork"));
        Assert.That(applyMethod, Does.Contain("AreSameMainDbPath("));
        Assert.That(applyMethod, Does.Contain("_thumbnailErrorPendingRescueWorkCachedDbFullPath"));
        Assert.That(applyMethod, Does.Contain("Dispatcher.HasShutdownStarted"));
        Assert.That(applyMethod, Does.Contain("RequestThumbnailErrorSnapshotRefresh();"));
    }

    [Test]
    public void ThumbnailErrorSnapshot反映はDB切替後着を捨てる()
    {
        string source = GetRepoText("Watcher", "MainWindow.ThumbnailFailedTab.cs");
        string captureMethod = ExtractMethod(
            source,
            "private ThumbnailErrorRefreshContext CaptureThumbnailErrorRefreshContext()"
        );
        string coreMethod = ExtractMethod(
            source,
            "private async Task RefreshThumbnailErrorRecordsCoreAsync("
        );

        Assert.That(captureMethod, Does.Contain("DbFullPath = MainVM?.DbInfo?.DBFullPath ?? \"\""));
        Assert.That(captureMethod, Does.Not.Contain("ResolveCurrentThumbnailFailureDbService("));
        Assert.That(coreMethod, Does.Contain(".Run(() => BuildThumbnailErrorRefreshResult(context))"));
        Assert.That(coreMethod, Does.Contain("AreSameMainDbPath("));
        Assert.That(coreMethod, Does.Contain("Dispatcher.HasShutdownStarted"));
        Assert.That(coreMethod, Does.Contain("Interlocked.Exchange(ref _thumbnailErrorRecordsDirty, 1);"));
    }

    [Test]
    public void ThumbnailErrorSnapshot用FailureDbService生成は背景集計側へ寄せる()
    {
        string source = GetRepoText("Watcher", "MainWindow.ThumbnailFailedTab.cs");
        string buildMethod = ExtractMethod(
            source,
            "private ThumbnailErrorRefreshResult BuildThumbnailErrorRefreshResult("
        );

        Assert.That(buildMethod, Does.Contain("CreateThumbnailErrorFailureDbService("));
        Assert.That(buildMethod, Does.Contain("LoadLatestThumbnailErrorRecordsByKey(failureDbService)"));
        Assert.That(source, Does.Not.Contain("FailureDbService { get; init; }"));
    }

    [Test]
    public void TryPromoteVisibleThumbnailErrorRecordsはUI上で同期投入とFailureDbを触らない()
    {
        string source = GetRepoText("Watcher", "MainWindow.ThumbnailFailedTab.cs");
        string method = ExtractMethod(
            source,
            "private void TryPromoteVisibleThumbnailErrorRecords()"
        );

        Assert.That(method, Does.Contain("CaptureThumbnailErrorVisiblePromotionRequest("));
        Assert.That(method, Does.Contain("TryQueueThumbnailErrorVisiblePromotion("));
        Assert.That(method, Does.Not.Contain("EnqueueThumbnailErrorRecordsToRescue("));
        Assert.That(method, Does.Not.Contain("RunThumbnailErrorBulkRescueAsync("));
        Assert.That(method, Does.Not.Contain("TryEnqueueThumbnailDisplayErrorRescueJob("));
        Assert.That(method, Does.Not.Contain("TryDeleteThumbnailErrorMarker("));
        Assert.That(method, Does.Not.Contain("ResolveCurrentThumbnailFailureDbService("));
        Assert.That(method, Does.Not.Contain("DeleteMainFailureRecords("));
        Assert.That(method, Does.Not.Contain("HasFailureHistory("));
    }

    [Test]
    public void ThumbnailError可視行優先投入は背景で実行しDB一致時だけUIへ戻す()
    {
        string source = GetRepoText("Watcher", "MainWindow.ThumbnailFailedTab.cs");
        string requestMethod = ExtractMethod(
            source,
            "private ThumbnailErrorVisiblePromotionRequest CaptureThumbnailErrorVisiblePromotionRequest("
        );
        string queueMethod = ExtractMethod(
            source,
            "private bool TryQueueThumbnailErrorVisiblePromotion("
        );
        string runMethod = ExtractMethod(
            source,
            "private async Task RunThumbnailErrorVisiblePromotionAsync("
        );
        string coreMethod = ExtractMethod(
            source,
            "private ThumbnailErrorBulkRescueResult EnqueueThumbnailErrorRecordSnapshotsToRescue("
        );
        string applyMethod = ExtractMethod(
            source,
            "private void ApplyThumbnailErrorVisiblePromotionResult("
        );
        string guardMethod = ExtractMethod(
            source,
            "private bool IsThumbnailErrorVisiblePromotionRequestCurrent("
        );

        Assert.That(requestMethod, Does.Contain("MainVM?.DbInfo?.DBFullPath ?? \"\""));
        Assert.That(queueMethod, Does.Contain("Interlocked.CompareExchange"));
        Assert.That(runMethod, Does.Contain(".Run(() => EnqueueThumbnailErrorRecordSnapshotsToRescue(request))"));
        Assert.That(coreMethod, Does.Contain("CreateThumbnailErrorFailureDbService("));
        Assert.That(coreMethod, Does.Not.Contain("MainVM"));
        Assert.That(applyMethod, Does.Contain("IsThumbnailErrorVisiblePromotionRequestCurrent("));
        Assert.That(guardMethod, Does.Contain("Dispatcher.HasShutdownStarted"));
        Assert.That(guardMethod, Does.Contain("AreSameMainDbPath("));
        Assert.That(applyMethod, Does.Contain("QueuedTabCount > 0"));
        Assert.That(applyMethod, Does.Contain("RequestThumbnailErrorSnapshotRefresh();"));
    }

    [Test]
    public void ClearThumbnailErrorListButtonはUI上でMarker削除とFailureDb削除を実行しない()
    {
        string source = GetRepoText("Watcher", "MainWindow.ThumbnailFailedTab.cs");
        string method = ExtractMethod(
            source,
            "private async void ClearThumbnailErrorListButton_Click("
        );

        Assert.That(method, Does.Contain("CaptureThumbnailErrorClearRequest("));
        Assert.That(method, Does.Contain("Task.Run("));
        Assert.That(method, Does.Contain("ClearThumbnailErrorRecordsCore(request)"));
        Assert.That(method, Does.Contain("IsThumbnailErrorClearRequestCurrent("));
        Assert.That(method, Does.Not.Contain("TryDeleteThumbnailErrorMarker("));
        Assert.That(method, Does.Not.Contain("ResolveCurrentThumbnailFailureDbService("));
        Assert.That(method, Does.Not.Contain("DeleteMainFailureRecords("));
    }

    [Test]
    public void ThumbnailError一覧クリアは背景で削除しDB一致時だけUI反映する()
    {
        string source = GetRepoText("Watcher", "MainWindow.ThumbnailFailedTab.cs");
        string captureMethod = ExtractMethod(
            source,
            "private ThumbnailErrorClearRequest CaptureThumbnailErrorClearRequest("
        );
        string coreMethod = ExtractMethod(
            source,
            "private ThumbnailErrorClearResult ClearThumbnailErrorRecordsCore("
        );
        string guardMethod = ExtractMethod(
            source,
            "private bool IsThumbnailErrorClearRequestCurrent("
        );

        Assert.That(captureMethod, Does.Contain("MainVM?.DbInfo?.DBFullPath ?? \"\""));
        Assert.That(coreMethod, Does.Contain("TryDeleteThumbnailErrorMarker("));
        Assert.That(coreMethod, Does.Contain("CreateThumbnailErrorFailureDbService("));
        Assert.That(coreMethod, Does.Contain("DeleteMainFailureRecords(targets)"));
        Assert.That(coreMethod, Does.Not.Contain("MainVM"));
        Assert.That(guardMethod, Does.Contain("Dispatcher.HasShutdownStarted"));
        Assert.That(guardMethod, Does.Contain("AreSameMainDbPath("));
    }

    [Test]
    public void DisplayError救済のFailureDb履歴読み取りはtabErrorPlaceholderだけに限定する()
    {
        string rescueLaneSource = GetRepoText("Thumbnail", "MainWindow.ThumbnailRescueLane.cs");
        string displayMethod = ExtractMethod(
            rescueLaneSource,
            "private bool TryEnqueueThumbnailDisplayErrorRescueJob("
        );
        string historyGuardMethod = ExtractMethod(
            rescueLaneSource,
            "internal static bool ShouldReadFailureHistoryForDisplayError("
        );

        Assert.That(historyGuardMethod, Does.Contain("\"tab-error-placeholder\""));
        Assert.That(displayMethod, Does.Contain("if (ShouldReadFailureHistoryForDisplayError(reason))"));
        Assert.That(
            displayMethod.IndexOf("if (ShouldReadFailureHistoryForDisplayError(reason))", StringComparison.Ordinal),
            Is.LessThan(displayMethod.IndexOf("HasFailureHistory(", StringComparison.Ordinal))
        );
    }

    [Test]
    public void ThumbnailError可視行優先投入も不要なFailureDb履歴読み取りを避ける()
    {
        string source = GetRepoText("Watcher", "MainWindow.ThumbnailFailedTab.cs");
        string method = ExtractMethod(
            source,
            "private bool TryEnqueueThumbnailErrorRescueSnapshotJob("
        );

        Assert.That(method, Does.Contain("if (ShouldReadFailureHistoryForDisplayError(reason))"));
        Assert.That(
            method.IndexOf("if (ShouldReadFailureHistoryForDisplayError(reason))", StringComparison.Ordinal),
            Is.LessThan(method.IndexOf("HasFailureHistory(", StringComparison.Ordinal))
        );
        Assert.That(method, Does.Contain("IsThumbnailErrorVisiblePromotionShutdownStarted()"));
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
