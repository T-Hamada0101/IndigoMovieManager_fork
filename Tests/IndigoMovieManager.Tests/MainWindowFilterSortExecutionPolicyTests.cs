namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MainWindowFilterSortExecutionPolicyTests
{
    [TestCase(false, false, false, "full-reload")]
    [TestCase(false, true, false, "query-only")]
    [TestCase(true, false, false, "query-only")]
    [TestCase(true, true, false, "query-only")]
    [TestCase(false, false, true, "full-reload")]
    [TestCase(true, true, true, "full-reload")]
    public void ResolveFilterSortExecutionRouteLabel_経路を短い札で返せる(
        bool hasSnapshotData,
        bool startupFeedLoadedAllPages,
        bool isGetNew,
        string expected
    )
    {
        string actual = MainWindow.ResolveFilterSortExecutionRouteLabel(
            hasSnapshotData,
            startupFeedLoadedAllPages,
            isGetNew
        );

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase(false, false, false, "no-snapshot-startup-partial")]
    [TestCase(false, true, false, "none")]
    [TestCase(true, false, false, "none")]
    [TestCase(true, true, false, "none")]
    [TestCase(false, false, true, "is-get-new")]
    [TestCase(true, true, true, "is-get-new")]
    public void ResolveFilterSortFullReloadReason_full_reload理由を短い札で返せる(
        bool hasSnapshotData,
        bool startupFeedLoadedAllPages,
        bool isGetNew,
        string expected
    )
    {
        string actual = MainWindow.ResolveFilterSortFullReloadReason(
            hasSnapshotData,
            startupFeedLoadedAllPages,
            isGetNew
        );

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase(-1, false)]
    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(63, false)]
    [TestCase(64, true)]
    [TestCase(120, true)]
    public void ShouldRunFilterSortOnBackground_件数閾値で実行方式を切り替えられる(
        int sourceCount,
        bool expected
    )
    {
        bool actual = MainWindow.ShouldRunFilterSortOnBackground(sourceCount);
        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase(63, false)]
    [TestCase(64, true)]
    public void ShouldUseFastAsciiSearchProjection_大件数だけ読み仮名fallbackを省く(
        int sourceCount,
        bool expected
    )
    {
        bool actual = MainWindow.ShouldUseFastAsciiSearchProjection(sourceCount);
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void LinkSearch_ユーザーコントロールから検索正本へ合流する()
    {
        string searchSource = GetRepoText("Views", "Main", "MainWindow.Search.cs");
        string tagSource = GetRepoText("UserControls", "TagControl.xaml.cs");
        string detailSource = GetRepoText("UserControls", "ExtDetail.xaml.cs");
        string bookmarkSource = GetRepoText("UserControls", "Bookmark.xaml.cs");

        Assert.That(searchSource, Does.Contain("public async Task ApplySearchKeywordFromLinkAsync("));
        Assert.That(searchSource, Does.Contain("SearchExecutor.ExecuteAsync(keyword ?? \"\", syncSearchText: true)"));
        Assert.That(searchSource, Does.Contain("if (SearchBox != null && !SearchBox.IsKeyboardFocusWithin)"));
        Assert.That(searchSource, Does.Contain("catch (Exception ex)"));
        Assert.That(searchSource, Does.Contain("link search failed:"));
        Assert.That(tagSource, Does.Contain("await ownerWindow.ApplySearchKeywordFromLinkAsync(keyword);"));
        Assert.That(detailSource, Does.Contain("await ownerWindow.ApplySearchKeywordFromLinkAsync(quoted);"));
        Assert.That(detailSource, Does.Contain("await ownerWindow.ApplySearchKeywordFromLinkAsync(mv.Ext);"));
        Assert.That(bookmarkSource, Does.Contain("await ownerWindow.ApplySearchKeywordFromLinkAsync(mv.Movie_Body ?? \"\");"));
        Assert.That(tagSource, Does.Not.Contain("FilterAndSort(ownerWindow.MainVM.DbInfo.Sort, true);"));
        Assert.That(detailSource, Does.Not.Contain("FilterAndSort(ownerWindow.MainVM.DbInfo.Sort, true);"));
        Assert.That(bookmarkSource, Does.Not.Contain("ownerWindow.SearchBox.Text ="));
    }

    [Test]
    public void SearchHistory_検索確定後のDB読み書きは背景へ逃がす()
    {
        string searchSource = GetRepoText("Views", "Main", "MainWindow.Search.cs");
        string persistMethod = GetMethodBlock(
            searchSource,
            "private void PersistSearchHistoryAfterSearch("
        );
        string refreshMethod = GetMethodBlock(searchSource, "private void QueueSearchHistoryRefresh(");
        string asyncRefreshMethod = GetMethodBlock(
            searchSource,
            "private async Task RefreshSearchHistoryAsync("
        );
        string lostFocusMethod = GetMethodBlock(searchSource, "private void SearchBox_LostFocus(");

        Assert.That(persistMethod, Does.Not.Contain("SearchHistoryService.PersistSuccessfulSearch("));
        Assert.That(persistMethod, Does.Not.Contain("GetHistoryTable("));
        Assert.That(persistMethod, Does.Contain("QueueSearchHistoryRefresh("));
        Assert.That(refreshMethod, Does.Contain("RefreshSearchHistoryAsync("));
        Assert.That(refreshMethod, Does.Not.Contain("ContinueWith("));
        Assert.That(refreshMethod, Does.Not.Contain("task.Result"));
        Assert.That(asyncRefreshMethod, Does.Contain("Task.Run("));
        Assert.That(asyncRefreshMethod, Does.Contain("SearchHistoryService.PersistSuccessfulSearch("));
        Assert.That(asyncRefreshMethod, Does.Contain("SearchHistoryService.LoadLatestHistory("));
        Assert.That(asyncRefreshMethod, Does.Contain(".InvokeAsync("));
        Assert.That(asyncRefreshMethod, Does.Contain(".ConfigureAwait(false);"));
        Assert.That(asyncRefreshMethod, Does.Contain(".Task.ConfigureAwait(false);"));
        Assert.That(asyncRefreshMethod, Does.Contain("AreSameMainDbPath("));
        Assert.That(asyncRefreshMethod, Does.Contain("ApplySearchHistoryRecords(records, SearchBox?.Text ?? \"\")"));
        Assert.That(asyncRefreshMethod, Does.Contain("catch (TaskCanceledException)"));
        Assert.That(asyncRefreshMethod, Does.Contain("catch (InvalidOperationException)"));
        Assert.That(asyncRefreshMethod, Does.Contain("history apply failed"));
        Assert.That(asyncRefreshMethod, Does.Not.Contain("ContinueWith("));
        Assert.That(asyncRefreshMethod, Does.Not.Contain("task.Result"));
        Assert.That(lostFocusMethod, Does.Contain("QueueSearchHistoryUsageRecord("));
        Assert.That(lostFocusMethod, Does.Not.Contain("SearchHistoryService.RecordSearchUsage("));
    }

    [Test]
    public void SearchBox_TextChangedの検索解除は検索正本へ合流する()
    {
        string searchSource = GetRepoText("Views", "Main", "MainWindow.Search.cs");
        string textChangedMethod = GetMethodBlock(
            searchSource,
            "private void SearchBox_TextChanged("
        );

        Assert.That(textChangedMethod, Does.Contain("ExecuteSearchKeywordAsync(text, false);"));
        Assert.That(textChangedMethod, Does.Not.Contain("RestartThumbnailTask();"));
        Assert.That(textChangedMethod, Does.Not.Contain("FilterAndSort(MainVM.DbInfo.Sort, IsStartupFeedPartialActive);"));
    }

    [Test]
    public void RefreshMovieViewFromCurrentSourceAsync_後着キャンセルtokenをin_memory再計算へ通す()
    {
        string mainWindowSource = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string method = GetMethodBlock(
            mainWindowSource,
            "private async Task RefreshMovieViewFromCurrentSourceAsync("
        );

        Assert.That(method, Does.Contain("BeginFilterAndSortCancellation();"));
        Assert.That(method, Does.Contain("CancellationToken refreshCancellationToken"));
        Assert.That(method, Does.Contain("refreshCancellationToken.ThrowIfCancellationRequested();"));
        Assert.That(method, Does.Contain("Task.Run("));
        Assert.That(method, Does.Contain("MainVM.FilterMovies("));
        Assert.That(method, Does.Contain("refreshCancellationToken,"));
        Assert.That(
            method,
            Does.Contain(
                "catch (OperationCanceledException) when (refreshCancellationToken.IsCancellationRequested)"
            )
        );
        Assert.That(method, Does.Contain("refresh canceled: revision="));
        Assert.That(method, Does.Not.Contain("CancellationToken.None"));
    }

    [Test]
    public void SortDataAsync_大件数sortはbackgroundとrevision_guardへ寄せる()
    {
        string mainWindowSource = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string legacyWrapper = GetMethodBlock(mainWindowSource, "private void SortData(");
        string legacyAsyncWrapper = GetMethodBlock(
            mainWindowSource,
            "private async Task SortDataFromLegacyCallerAsync("
        );
        string sortAsync = GetMethodBlock(mainWindowSource, "private async Task<bool> SortDataAsync(");
        string comboChanged = GetMethodBlock(
            mainWindowSource,
            "private async void ComboSort_SelectionChanged("
        );

        Assert.That(legacyWrapper, Does.Contain("SortDataFromLegacyCallerAsync(id);"));
        Assert.That(legacyAsyncWrapper, Does.Contain("await SortDataAsync(id);"));
        Assert.That(legacyAsyncWrapper, Does.Contain("sort legacy caller failed:"));
        Assert.That(sortAsync, Does.Contain("Interlocked.Increment(ref _filterAndSortRequestRevision);"));
        Assert.That(sortAsync, Does.Contain("BeginFilterAndSortCancellation();"));
        Assert.That(sortAsync, Does.Contain("ShouldRunFilterSortOnBackground(source.Length);"));
        Assert.That(sortAsync, Does.Contain("Task.Run("));
        Assert.That(sortAsync, Does.Contain("sort skip stale:"));
        Assert.That(sortAsync, Does.Contain("sort canceled:"));
        Assert.That(sortAsync, Does.Contain("sort end: revision="));
        Assert.That(comboChanged, Does.Contain("await SortDataAsync(id.ToString());"));
        Assert.That(comboChanged, Does.Contain("if (shouldSelectFirstItem)"));
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

        Assert.Fail($"Repository file not found: {Path.Combine(relativePathParts)}");
        return "";
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
        return "";
    }
}
