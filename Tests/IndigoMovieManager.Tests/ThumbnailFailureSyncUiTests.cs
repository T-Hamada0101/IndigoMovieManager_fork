using IndigoMovieManager;
using IndigoMovieManager.Thumbnail;
using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailFailureSyncUiTests
{
    [Test]
    public void ApplyThumbnailPathWithForcedRebind_同一パスなら空経由で再通知する()
    {
        MovieRecords item = new()
        {
            ThumbPathGrid = @"C:\thumb\grid.#hash.jpg",
        };
        int changedCount = 0;
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MovieRecords.ThumbPathGrid))
            {
                changedCount++;
            }
        };

        bool applied = MainWindow.TryApplyThumbnailPathToMovieRecord(
            item,
            2,
            @"C:\thumb\grid.#hash.jpg"
        );

        Assert.That(applied, Is.True);
        Assert.That(item.ThumbPathGrid, Is.EqualTo(@"C:\thumb\grid.#hash.jpg"));
        Assert.That(changedCount, Is.EqualTo(2));
    }

    [Test]
    public void ApplyThumbnailPathWithForcedRebind_別パスなら通常の一回通知で更新する()
    {
        MovieRecords item = new()
        {
            ThumbPathGrid = @"C:\thumb\old.#hash.jpg",
        };
        int changedCount = 0;
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MovieRecords.ThumbPathGrid))
            {
                changedCount++;
            }
        };

        bool applied = MainWindow.TryApplyThumbnailPathToMovieRecord(
            item,
            2,
            @"C:\thumb\new.#hash.jpg"
        );

        Assert.That(applied, Is.True);
        Assert.That(item.ThumbPathGrid, Is.EqualTo(@"C:\thumb\new.#hash.jpg"));
        Assert.That(changedCount, Is.EqualTo(1));
    }

    [Test]
    public void ThumbPathBig10_更新時は正しいPropertyNameで通知する()
    {
        MovieRecords item = new();
        string propertyName = "";
        item.PropertyChanged += (_, e) => propertyName = e.PropertyName ?? "";

        item.ThumbPathBig10 = @"C:\thumb\big10.#hash.jpg";

        Assert.That(propertyName, Is.EqualTo(nameof(MovieRecords.ThumbPathBig10)));
    }

    [Test]
    public void ShouldRefreshVisibleThumbnailUiAfterCreate_PreferredだけTrueを返す()
    {
        QueueObj preferred = new() { Priority = ThumbnailQueuePriority.Preferred };
        QueueObj normal = new() { Priority = ThumbnailQueuePriority.Normal };

        Assert.That(MainWindow.ShouldRefreshVisibleThumbnailUiAfterCreate(preferred), Is.True);
        Assert.That(MainWindow.ShouldRefreshVisibleThumbnailUiAfterCreate(normal), Is.False);
        Assert.That(MainWindow.ShouldRefreshVisibleThumbnailUiAfterCreate(null), Is.False);
    }

    [Test]
    public void ShouldRequestMainTabLocalRefreshAfterThumbnailSuccess_直接反映済みならFalseを返す()
    {
        QueueObj preferred = new() { Priority = ThumbnailQueuePriority.Preferred };
        QueueObj normal = new() { Priority = ThumbnailQueuePriority.Normal };

        Assert.That(
            MainWindow.ShouldRequestMainTabLocalRefreshAfterThumbnailSuccess(
                preferred,
                appliedDirectlyToMainMovie: true
            ),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldRequestMainTabLocalRefreshAfterThumbnailSuccess(
                preferred,
                appliedDirectlyToMainMovie: false
            ),
            Is.True
        );
        Assert.That(
            MainWindow.ShouldRequestMainTabLocalRefreshAfterThumbnailSuccess(
                normal,
                appliedDirectlyToMainMovie: false
            ),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldRequestMainTabLocalRefreshAfterThumbnailSuccess(
                shouldRefreshVisibleUi: true,
                appliedDirectlyToMainMovie: true
            ),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldRequestMainTabLocalRefreshAfterThumbnailSuccess(
                shouldRefreshVisibleUi: true,
                appliedDirectlyToMainMovie: false
            ),
            Is.True
        );
    }

    [Test]
    public void サムネ成功後段予約は非UIスレッドからBackground優先度で戻す()
    {
        string source = GetRepoText("Thumbnail", "MainWindow.ThumbnailFailureSync.cs")
            .Replace("\r\n", "\n");
        string method = ExtractMethod(
            source,
            "private void RequestMainTabLocalRefreshAfterThumbnailSuccess("
        );

        int checkAccessIndex = method.IndexOf(
            "if (!Dispatcher.CheckAccess())",
            StringComparison.Ordinal
        );
        int shutdownGuardIndex = method.IndexOf(
            "Dispatcher.HasShutdownStarted",
            checkAccessIndex,
            StringComparison.Ordinal
        );
        int invokeIndex = method.IndexOf(
            "Dispatcher.InvokeAsync(",
            checkAccessIndex,
            StringComparison.Ordinal
        );
        int backgroundPriorityIndex = method.IndexOf(
            "DispatcherPriority.Background",
            invokeIndex,
            StringComparison.Ordinal
        );

        Assert.That(checkAccessIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(shutdownGuardIndex, Is.GreaterThan(checkAccessIndex));
        Assert.That(shutdownGuardIndex, Is.LessThan(invokeIndex));
        Assert.That(backgroundPriorityIndex, Is.GreaterThan(invokeIndex));
    }

    [Test]
    public void ShouldResortAfterThumbnailSuccessLocalRefresh_サムネERROR順だけTrue()
    {
        Assert.That(MainWindow.ShouldResortAfterThumbnailSuccessLocalRefresh("28"), Is.True);
        Assert.That(MainWindow.ShouldResortAfterThumbnailSuccessLocalRefresh(" 28 "), Is.True);
        Assert.That(MainWindow.ShouldResortAfterThumbnailSuccessLocalRefresh("0"), Is.False);
        Assert.That(MainWindow.ShouldResortAfterThumbnailSuccessLocalRefresh(null), Is.False);
    }

    [Test]
    public void RescuedThumbnailUiApplyResult_AppliedCountが1以上ならUi反映済み()
    {
        MainWindow.RescuedThumbnailUiApplyResult nonSelectedApplied = new(
            AppliedCount: 1,
            AppliedToSelectedRecord: false
        );
        MainWindow.RescuedThumbnailUiApplyResult selectedApplied = new(
            AppliedCount: 1,
            AppliedToSelectedRecord: true
        );
        MainWindow.RescuedThumbnailUiApplyResult notApplied = new(
            AppliedCount: 0,
            AppliedToSelectedRecord: false
        );

        Assert.That(nonSelectedApplied.AppliedToUi, Is.True);
        Assert.That(nonSelectedApplied.AppliedToSelectedRecord, Is.False);
        Assert.That(selectedApplied.AppliedToUi, Is.True);
        Assert.That(selectedApplied.AppliedToSelectedRecord, Is.True);
        Assert.That(notApplied.AppliedToUi, Is.False);
    }

    [Test]
    public void ShouldRefreshSelectedThumbnailDetailAfterRescuedSync_選択中へ反映された時だけTrue()
    {
        Assert.That(
            MainWindow.ShouldRefreshSelectedThumbnailDetailAfterRescuedSync(
                appliedToSelectedRecord: true
            ),
            Is.True
        );
        Assert.That(
            MainWindow.ShouldRefreshSelectedThumbnailDetailAfterRescuedSync(
                appliedToSelectedRecord: false
            ),
            Is.False
        );
    }

    [Test]
    public void ShouldRefreshUpperTabViewportAfterImmediateThumbnailSuccess_直接反映済みならFalse()
    {
        Assert.That(
            MainWindow.ShouldRefreshUpperTabViewportAfterImmediateThumbnailSuccess(
                appliedDirectlyToMainMovie: true
            ),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldRefreshUpperTabViewportAfterImmediateThumbnailSuccess(
                appliedDirectlyToMainMovie: false
            ),
            Is.True
        );
    }

    [Test]
    public void ShouldRefreshSelectedThumbnailDetailAfterImmediateThumbnailSuccess_直接反映済みならFalse()
    {
        Assert.That(
            MainWindow.ShouldRefreshSelectedThumbnailDetailAfterImmediateThumbnailSuccess(
                appliedDirectlyToMainMovie: true
            ),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldRefreshSelectedThumbnailDetailAfterImmediateThumbnailSuccess(
                appliedDirectlyToMainMovie: false
            ),
            Is.True
        );
    }

    [Test]
    public void 即時サムネ成功の詳細更新は直接反映済みなら省く()
    {
        string source = GetRepoText("Thumbnail", "MainWindow.ThumbnailFailureSync.cs")
            .Replace("\r\n", "\n");
        string method = ExtractMethod(
            source,
            "private void RefreshVisibleThumbnailUiAfterImmediateThumbnailSuccess("
        );

        int detailPolicyIndex = method.IndexOf(
            "ShouldRefreshSelectedThumbnailDetailAfterImmediateThumbnailSuccess(",
            StringComparison.Ordinal
        );
        int detailRefreshIndex = method.IndexOf(
            "RefreshSelectedThumbnailDetail();",
            detailPolicyIndex,
            StringComparison.Ordinal
        );
        int viewportPolicyIndex = method.IndexOf(
            "ShouldRefreshUpperTabViewportAfterImmediateThumbnailSuccess(",
            StringComparison.Ordinal
        );

        Assert.That(detailPolicyIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(detailRefreshIndex, Is.GreaterThan(detailPolicyIndex));
        Assert.That(viewportPolicyIndex, Is.GreaterThan(detailRefreshIndex));
        Assert.That(method, Does.Not.Match(@"(?m)^\s*Refresh\(\);\s*$"));
    }

    [Test]
    public void RescuedSync完了時は詳細更新だけ選択中反映で絞る()
    {
        string source = GetRepoText("Thumbnail", "MainWindow.ThumbnailFailureSync.cs")
            .Replace("\r\n", "\n");
        string syncMethod = ExtractMethod(
            source,
            "private async Task TrySyncRescuedThumbnailRecordsAsync("
        );

        int invalidateIndex = syncMethod.IndexOf(
            "InvalidateThumbnailErrorRecords(refreshIfVisible: true);",
            StringComparison.Ordinal
        );
        int policyIndex = syncMethod.IndexOf(
            "ShouldRefreshSelectedThumbnailDetailAfterRescuedSync(",
            StringComparison.Ordinal
        );
        int detailRefreshIndex = syncMethod.IndexOf(
            "RefreshSelectedThumbnailDetail();",
            policyIndex,
            StringComparison.Ordinal
        );
        int progressIndex = syncMethod.IndexOf(
            "RequestThumbnailProgressSnapshotRefresh();",
            StringComparison.Ordinal
        );

        Assert.That(invalidateIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(policyIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(detailRefreshIndex, Is.GreaterThan(policyIndex));
        Assert.That(progressIndex, Is.GreaterThan(detailRefreshIndex));
        Assert.That(syncMethod, Does.Not.Match(@"(?m)^\s*Refresh\(\);\s*$"));
    }

    [Test]
    public void RescuedSyncはbatch単位で索引を作りDispatcherへ一度だけ渡す()
    {
        string source = GetRepoText("Thumbnail", "MainWindow.ThumbnailFailureSync.cs")
            .Replace("\r\n", "\n");
        string applyMethod = ExtractMethod(
            source,
            "private async Task<RescuedThumbnailBatchUiApplyResult> ApplyRescuedThumbnailRecordsToUiAsync("
        );

        Assert.That(CountOccurrences(applyMethod, "Dispatcher.InvokeAsync("), Is.EqualTo(1));
        Assert.That(
            CountOccurrences(
                applyMethod,
                "foreach (MovieRecords movie in MainVM?.MovieRecs ?? [])"
            ),
            Is.EqualTo(1)
        );
        Assert.That(applyMethod, Does.Not.Contain(".Where("));
        Assert.That(applyMethod, Does.Not.Contain("ApplyRescuedThumbnailRecordToUiAsync("));
    }

    [Test]
    public void RescuedSyncはuser_priorityを入口とapply直前で確認しbounded延期する()
    {
        string source = GetRepoText("Thumbnail", "MainWindow.ThumbnailFailureSync.cs")
            .Replace("\r\n", "\n");
        string applyMethod = ExtractMethod(
            source,
            "private async Task<RescuedThumbnailBatchUiApplyResult> ApplyRescuedThumbnailRecordsToUiAsync("
        );

        int requestBuildIndex = applyMethod.IndexOf(
            "List<(ThumbnailFailureRecord Record, long MovieId)> requests",
            StringComparison.Ordinal
        );
        int dispatcherIndex = applyMethod.IndexOf("Dispatcher.InvokeAsync(", StringComparison.Ordinal);
        int firstGateIndex = applyMethod.IndexOf("IsUserPriorityWorkActive()", StringComparison.Ordinal);
        int lastGateIndex = applyMethod.LastIndexOf("IsUserPriorityWorkActive()", StringComparison.Ordinal);

        Assert.That(firstGateIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(firstGateIndex, Is.LessThan(requestBuildIndex));
        Assert.That(lastGateIndex, Is.GreaterThan(requestBuildIndex));
        Assert.That(lastGateIndex, Is.GreaterThan(dispatcherIndex));
        Assert.That(applyMethod, Does.Contain("Task.Delay("));
        Assert.That(applyMethod, Does.Contain("ThumbnailFailureSyncUserPriorityDelayMs"));
    }

    [Test]
    public void RescuedSyncのbatch反映ログは件数と延期状態を観測できる()
    {
        string source = GetRepoText("Thumbnail", "MainWindow.ThumbnailFailureSync.cs")
            .Replace("\r\n", "\n");
        string syncMethod = ExtractMethod(
            source,
            "private async Task TrySyncRescuedThumbnailRecordsAsync("
        );

        Assert.That(syncMethod, Does.Contain("rescued sync completed:"));
        Assert.That(syncMethod, Does.Contain("trigger="));
        Assert.That(syncMethod, Does.Contain("batch_count="));
        Assert.That(syncMethod, Does.Contain("matched_count="));
        Assert.That(syncMethod, Does.Contain("apply_ms="));
        Assert.That(syncMethod, Does.Contain("deferred="));
    }

    [Test]
    public void サムネ成功後段はDB再読込ではなく局所refreshへ寄せる()
    {
        string source = GetRepoText("Thumbnail", "MainWindow.ThumbnailFailureSync.cs")
            .Replace("\r\n", "\n");
        string tickMethod = ExtractMethod(
            source,
            "private async void ThumbnailSuccessMainTabReloadTimer_Tick("
        );
        string refreshMethod = ExtractMethod(
            source,
            "private async Task RefreshMainTabLocallyAfterThumbnailSuccessAsync("
        );

        Assert.That(tickMethod, Does.Contain("thumbnail success local refresh:"));
        Assert.That(tickMethod, Does.Contain("RefreshMainTabLocallyAfterThumbnailSuccessAsync("));
        Assert.That(tickMethod, Does.Not.Contain("FilterAndSort("));
        Assert.That(refreshMethod, Does.Contain("InvalidateThumbnailErrorRecords(refreshIfVisible: true);"));
        Assert.That(refreshMethod, Does.Contain("RequestUpperTabVisibleRangeRefresh("));
        Assert.That(refreshMethod, Does.Contain("RefreshUpperTabPreferredMoviePathKeysRevision();"));
        Assert.That(refreshMethod, Does.Contain("RequestThumbnailErrorSnapshotRefresh();"));
        Assert.That(refreshMethod, Does.Contain("RequestThumbnailProgressSnapshotRefresh();"));
        Assert.That(refreshMethod, Does.Contain("await SortDataAsync(sortId);"));
        Assert.That(refreshMethod, Does.Not.Contain("FilterAndSort("));
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

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int startIndex = 0;
        while ((startIndex = source.IndexOf(value, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += value.Length;
        }

        return count;
    }
}
