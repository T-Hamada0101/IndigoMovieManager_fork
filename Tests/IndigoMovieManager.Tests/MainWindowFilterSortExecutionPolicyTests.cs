using System.Runtime.CompilerServices;

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
    public void BootNewDb_検索履歴初期読込は背景へ逃がす()
    {
        string mainDbRuntimeSource = GetRepoText(
            "Views",
            "Main",
            "MainWindow.MainDbRuntime.cs"
        );
        string bootMethod = GetMethodBlock(
            mainDbRuntimeSource,
            "private void BootNewDb(string dbFullPath, DataTable preflightSystemData)"
        );
        string queueMethod = GetMethodBlock(
            mainDbRuntimeSource,
            "private void QueueSearchHistoryReload("
        );
        string asyncMethod = GetMethodBlock(
            mainDbRuntimeSource,
            "private async Task ReloadSearchHistoryForDbSwitchAsync("
        );

        Assert.That(bootMethod, Does.Contain("QueueSearchHistoryReload(dbFullPath);"));
        Assert.That(bootMethod, Does.Not.Contain("GetHistoryTable("));
        Assert.That(bootMethod, Does.Not.Contain("SearchHistoryService.LoadLatestHistory("));
        Assert.That(queueMethod, Does.Contain("string dbFullPathSnapshot = dbFullPath ?? \"\";"));
        Assert.That(queueMethod, Does.Contain("string searchTextSnapshot = SearchBox?.Text ?? \"\";"));
        Assert.That(queueMethod, Does.Contain("Interlocked.Increment(ref _searchHistoryRefreshStamp);"));
        Assert.That(asyncMethod, Does.Contain("Task.Run("));
        Assert.That(asyncMethod, Does.Contain("SearchHistoryService.LoadLatestHistory(dbFullPathSnapshot)"));
        Assert.That(asyncMethod, Does.Contain(".InvokeAsync("));
        Assert.That(asyncMethod, Does.Contain("DispatcherPriority.Background"));
        Assert.That(asyncMethod, Does.Contain("Dispatcher.HasShutdownStarted"));
        Assert.That(asyncMethod, Does.Contain("Dispatcher.HasShutdownFinished"));
        Assert.That(asyncMethod, Does.Contain("AreSameMainDbPath("));
        Assert.That(
            asyncMethod,
            Does.Contain("ApplySearchHistoryRecords(records, SearchBox?.Text ?? searchTextSnapshot)")
        );
        Assert.That(asyncMethod, Does.Contain("history reload failed:"));
        Assert.That(asyncMethod, Does.Contain("history reload apply failed:"));
        Assert.That(asyncMethod, Does.Not.Contain("ContinueWith("));
        Assert.That(asyncMethod, Does.Not.Contain("task.Result"));
    }

    [Test]
    public void SearchHistory_同一候補ならUIコレクションを差し替えない()
    {
        string searchSource = GetRepoText("Views", "Main", "MainWindow.Search.cs");
        string applyMethod = GetMethodBlock(
            searchSource,
            "private void ApplySearchHistoryRecordItems("
        );
        string compareMethod = GetMethodBlock(
            searchSource,
            "private static bool AreSameSearchHistoryRecords("
        );

        Assert.That(applyMethod, Does.Contain("AreSameSearchHistoryRecords(MainVM.HistoryRecs, nextRecords)"));
        Assert.That(applyMethod, Does.Contain("return;"));
        Assert.That(
            applyMethod.IndexOf("return;", StringComparison.Ordinal),
            Is.LessThan(applyMethod.IndexOf("MainVM.HistoryRecs.Clear();", StringComparison.Ordinal))
        );
        Assert.That(applyMethod, Does.Contain("MainVM.HistoryRecs.Clear();"));
        Assert.That(applyMethod, Does.Contain("MainVM.HistoryRecs.Add(item);"));
        Assert.That(compareMethod, Does.Contain("currentRecords.Count != nextRecords.Count"));
        Assert.That(compareMethod, Does.Contain("current.Find_Id != next.Find_Id"));
        Assert.That(compareMethod, Does.Contain("StringComparison.Ordinal"));
    }

    [Test]
    public void MainDbRuntime境界は専用partialへ固定する()
    {
        string mainWindowSource = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string mainDbRuntimeSource = GetRepoText(
            "Views",
            "Main",
            "MainWindow.MainDbRuntime.cs"
        );
        string registeredMovieCountPolicySource = GetRepoText(
            "Views",
            "Main",
            "RegisteredMovieCountRefreshPolicy.cs"
        );
        string[] signatures =
        [
            "private void ResetMainHeaderCounts(",
            "private void QueueRegisteredMovieCountRefresh(",
            "private void TryAdjustRegisteredMovieCount(",
            "private async Task RefreshRegisteredMovieCountAsync(",
            "private bool OpenDatafile(",
            "private void ShutdownCurrentDb(",
            "private void BootNewDb(string dbFullPath)",
            "private void BootNewDb(string dbFullPath, DataTable preflightSystemData)",
            "private static void StopAndClearFileWatchers(",
            "private void ApplyColdStartSystemDefaults(",
            "public string SelectSystemTable(",
            "private void ApplyRuntimeSystemValue(",
            "private void UpsertSystemDataRow(",
            "private void QueueSearchHistoryReload(",
            "private async Task ReloadSearchHistoryForDbSwitchAsync(",
            "private void GetSystemTable(",
            "private static string NormalizeSkinName(",
            "private void GetWatchTable(",
            "private static DataTable GetWatchTableSnapshot(",
            "private void UpdateSort(",
            "private void UpdateSort(string dbFullPath)",
            "private void UpdateSkin(",
            "private void UpdateSkin(string dbFullPath)",
            "private void SwitchTab(",
        ];

        foreach (string signature in signatures)
        {
            Assert.That(
                mainDbRuntimeSource,
                Does.Contain(signature),
                $"{signature} は MainWindow.MainDbRuntime.cs に置く。"
            );
            Assert.That(
                mainWindowSource,
                Does.Not.Contain(signature),
                $"{signature} を MainWindow.xaml.cs へ戻さない。"
            );
        }

        Assert.That(
            mainDbRuntimeSource,
            Does.Contain("RegisteredMovieCountRefreshPolicy.ShouldApplyRefreshResult(")
        );
        Assert.That(
            registeredMovieCountPolicySource,
            Does.Contain("internal static class RegisteredMovieCountRefreshPolicy")
        );
        Assert.That(registeredMovieCountPolicySource, Does.Not.Contain("System.Windows"));
        Assert.That(registeredMovieCountPolicySource, Does.Not.Contain("Dispatcher"));
    }

    [Test]
    public void SearchHistory_履歴候補差し替え中の選択変更はユーザー選択扱いしない()
    {
        string searchSource = GetRepoText("Views", "Main", "MainWindow.Search.cs");
        string selectionChanged = GetMethodBlock(
            searchSource,
            "private void SearchBox_SelectionChanged("
        );

        Assert.That(selectionChanged, Does.Contain("if (_suppressSearchBoxTextChangedHandling)"));
        Assert.That(selectionChanged, Does.Contain("_searchBoxItemSelectedByUser = false;"));
        Assert.That(
            selectionChanged.IndexOf(
                "if (_suppressSearchBoxTextChangedHandling)",
                StringComparison.Ordinal
            ),
            Is.LessThan(selectionChanged.IndexOf("if (SearchBox.IsDropDownOpen)", StringComparison.Ordinal))
        );
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
        string requestSource = GetRepoText("Views", "Main", "MainWindow.MovieViewRequests.cs");
        string method = GetMethodBlock(
            requestSource,
            "private async Task RefreshMovieViewFromCurrentSourceAsync("
        );

        Assert.That(requestSource, Does.Contain("private async Task RefreshMovieViewFromCurrentSourceAsync("));
        Assert.That(mainWindowSource, Does.Not.Contain("private async Task RefreshMovieViewFromCurrentSourceAsync("));
        Assert.That(method, Does.Contain("BeginFilterAndSortCancellation();"));
        Assert.That(method, Does.Contain("CancellationToken refreshCancellationToken"));
        Assert.That(method, Does.Contain("refreshCancellationToken.ThrowIfCancellationRequested();"));
        Assert.That(method, Does.Contain("Task.Run("));
        Assert.That(method, Does.Contain("MovieViewReadModelRequest readModelRequest = new()"));
        Assert.That(method, Does.Contain("MovieViewReadModelBuilder.Build(readModelRequest)"));
        Assert.That(method, Does.Contain("MainVM.FilterMovies("));
        Assert.That(method, Does.Contain("refreshCancellationToken,"));
        int dispatcherPriorityIndex = method.IndexOf(
            "DispatcherPriority.Background",
            StringComparison.Ordinal
        );
        int dispatcherCancellationTokenIndex = method.IndexOf(
            "refreshCancellationToken",
            dispatcherPriorityIndex,
            StringComparison.Ordinal
        );
        Assert.That(dispatcherPriorityIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(dispatcherCancellationTokenIndex, Is.GreaterThan(dispatcherPriorityIndex));
        Assert.That(method, Does.Contain("resolvedTraceName"));
        Assert.That(
            method,
            Does.Contain(
                "catch (OperationCanceledException) when (refreshCancellationToken.IsCancellationRequested)"
            )
        );
        Assert.That(method, Does.Contain("stage=apply-dispatch"));
        Assert.That(method, Does.Contain("refresh canceled: revision="));
        Assert.That(method, Does.Not.Contain("CancellationToken.None"));
    }

    [Test]
    public void FilterAndSortAsync_full_reloadのDB読込にも後着キャンセルtokenを通す()
    {
        string mainWindowSource = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string requestSource = GetRepoText("Views", "Main", "MainWindow.MovieViewRequests.cs");
        string method = GetMethodBlock(requestSource, "private async Task FilterAndSortAsync(");
        int loadIndex = method.IndexOf(
            "_mainDbMovieReadFacade.LoadMovieTableForSort(dbFullPath, id)",
            StringComparison.Ordinal
        );
        int computeStageIndex = method.IndexOf(
            "stage=filter-sort-compute",
            StringComparison.Ordinal
        );
        int dbReloadCancelCatchIndex = method.IndexOf(
            "catch (OperationCanceledException) when (",
            loadIndex,
            StringComparison.Ordinal
        );

        Assert.That(requestSource, Does.Contain("private async Task FilterAndSortAsync("));
        Assert.That(mainWindowSource, Does.Not.Contain("private async Task FilterAndSortAsync("));
        Assert.That(loadIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(
            method.IndexOf(
                "filterAndSortCancellationToken.ThrowIfCancellationRequested();",
                StringComparison.Ordinal
            ),
            Is.LessThan(loadIndex)
        );
        Assert.That(
            method.IndexOf("filterAndSortCancellationToken", loadIndex, StringComparison.Ordinal),
            Is.GreaterThan(loadIndex)
        );
        Assert.That(
            method.IndexOf(
                "filterAndSortCancellationToken.ThrowIfCancellationRequested();",
                loadIndex,
                StringComparison.Ordinal
            ),
            Is.GreaterThan(loadIndex)
        );
        Assert.That(computeStageIndex, Is.GreaterThan(loadIndex));
        Assert.That(dbReloadCancelCatchIndex, Is.GreaterThan(loadIndex));
        Assert.That(dbReloadCancelCatchIndex, Is.LessThan(computeStageIndex));
        Assert.That(
            method.IndexOf(
                "filterAndSortCancellationToken.IsCancellationRequested",
                dbReloadCancelCatchIndex,
                StringComparison.Ordinal
            ),
            Is.LessThan(computeStageIndex)
        );
        Assert.That(
            method.IndexOf(
                "filter canceled: revision={requestRevision} stage=db-reload",
                loadIndex,
                StringComparison.Ordinal
            ),
            Is.InRange(dbReloadCancelCatchIndex, computeStageIndex)
        );
    }

    [Test]
    public void DataRowToViewData_単発追加の存在確認は背景bulk経路と後追い更新へ逃がす()
    {
        string mainWindowSource = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string movieRecordFactorySource = GetRepoText(
            "Views",
            "Main",
            "MainWindow.MovieRecordFactory.cs"
        );
        string method = GetMethodBlock(
            movieRecordFactorySource,
            "private async Task DataRowToViewData("
        );
        string watcherSource = GetRepoText("Watcher", "MainWindow.WatcherUiBridge.cs");
        string appendMethod = GetMethodBlock(
            watcherSource,
            "private async Task TryAppendMovieToViewByPathAsync("
        );
        string compactMethod = string.Concat(method.Where(c => !char.IsWhiteSpace(c)));

        Assert.That(method, Does.Contain("string expectedDbFullPath = \"\""));
        Assert.That(method, Does.Contain("CaptureMovieRecordBulkBuildContext()"));
        Assert.That(method, Does.Contain("Task.Run(() =>"));
        Assert.That(method, Does.Contain("BuildMovieRecordBulkBuildCache(bulkContext)"));
        Assert.That(method, Does.Contain("CreateMovieRecordFromDataRow("));
        Assert.That(method, Does.Contain("bulkContext"));
        Assert.That(method, Does.Contain("bulkCache"));
        Assert.That(method, Does.Contain("resolveMovieExists: false"));
        Assert.That(method, Does.Contain("!string.IsNullOrWhiteSpace(expectedDbFullPath)"));
        Assert.That(method, Does.Contain("AreSameMainDbPath("));
        Assert.That(method, Does.Contain("expectedDbFullPath,"));
        Assert.That(
            compactMethod,
            Does.Contain("AreSameMainDbPath(expectedDbFullPath,MainVM?.DbInfo?.DBFullPath??\"\")")
        );
        Assert.That(
            method.IndexOf("AreSameMainDbPath(", StringComparison.Ordinal),
            Is.LessThan(method.IndexOf("MainVM.MovieRecs.Add(item);", StringComparison.Ordinal))
        );
        Assert.That(method, Does.Contain("QueueMovieExistsRefresh([item], _filterAndSortRequestRevision);"));
        Assert.That(method, Does.Not.Contain("CreateMovieRecordFromDataRow(row);"));
        Assert.That(method, Does.Not.Contain("Path.Exists("));
        Assert.That(movieRecordFactorySource, Does.Contain("private MovieRecords CreateMovieRecordFromDataRow("));
        Assert.That(movieRecordFactorySource, Does.Contain("private readonly record struct MovieRecordBulkBuildContext"));
        Assert.That(movieRecordFactorySource, Does.Contain("private sealed class MovieRecordBulkBuildCache"));
        Assert.That(movieRecordFactorySource, Does.Contain("private MovieRecordBulkBuildContext CaptureMovieRecordBulkBuildContext("));
        Assert.That(movieRecordFactorySource, Does.Contain("private static MovieRecordBulkBuildCache BuildMovieRecordBulkBuildCache("));
        Assert.That(movieRecordFactorySource, Does.Contain("private static string ResolveThumbnailDisplayPath("));
        Assert.That(movieRecordFactorySource, Does.Contain("private async Task<MovieRecords[]> SetRecordsToSource("));
        Assert.That(movieRecordFactorySource, Does.Contain("private void QueueMovieExistsRefresh("));
        Assert.That(movieRecordFactorySource, Does.Contain("private Task ApplyMovieExistsRefreshBatchAsync("));
        Assert.That(mainWindowSource, Does.Not.Contain("private async Task DataRowToViewData("));
        Assert.That(mainWindowSource, Does.Not.Contain("private MovieRecords CreateMovieRecordFromDataRow("));
        Assert.That(mainWindowSource, Does.Not.Contain("private readonly record struct MovieRecordBulkBuildContext"));
        Assert.That(mainWindowSource, Does.Not.Contain("private sealed class MovieRecordBulkBuildCache"));
        Assert.That(mainWindowSource, Does.Not.Contain("private MovieRecordBulkBuildContext CaptureMovieRecordBulkBuildContext("));
        Assert.That(mainWindowSource, Does.Not.Contain("private static MovieRecordBulkBuildCache BuildMovieRecordBulkBuildCache("));
        Assert.That(mainWindowSource, Does.Not.Contain("private static string ResolveThumbnailDisplayPath("));
        Assert.That(mainWindowSource, Does.Not.Contain("private async Task<MovieRecords[]> SetRecordsToSource("));
        Assert.That(mainWindowSource, Does.Not.Contain("private Task ApplyMovieExistsRefreshBatchAsync("));
        Assert.That(appendMethod, Does.Contain("DataRowToViewData(targetRow, snapshotDbFullPath);"));
    }

    [Test]
    public void SortDataAsync_大件数sortはbackgroundとrevision_guardへ寄せる()
    {
        string mainWindowSource = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string inputRoutingSource = GetRepoText("Views", "Main", "MainWindow.InputRouting.cs");
        string readModelUiSource = GetRepoText("Views", "Main", "MainWindow.MovieViewReadModel.cs");
        string legacyWrapper = GetMethodBlock(readModelUiSource, "private void SortData(");
        string legacyAsyncWrapper = GetMethodBlock(
            readModelUiSource,
            "private async Task SortDataFromLegacyCallerAsync("
        );
        string sortAsync = GetMethodBlock(readModelUiSource, "private async Task<bool> SortDataAsync(");
        string comboChanged = GetMethodBlock(
            inputRoutingSource,
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
        Assert.That(sortAsync, Does.Contain("snapshot_ms="));
        Assert.That(sortAsync, Does.Contain("MovieViewReadModelResult.FromSorted("));
        Assert.That(sortAsync, Does.Contain("TryApplyMovieViewReadModelResultOnUiThread("));
        Assert.That(sortAsync, Does.Contain("sort end: revision="));
        Assert.That(comboChanged, Does.Contain("SortComboSelectionPolicy.BuildPlan("));
        Assert.That(comboChanged, Does.Contain("BeginUserPriorityWork(\"sort\");"));
        Assert.That(comboChanged, Does.Contain("await SortDataAsync(plan.SortId);"));
        Assert.That(comboChanged, Does.Contain("finally"));
        Assert.That(comboChanged, Does.Contain("EndUserPriorityWork(\"sort\");"));
        Assert.That(
            comboChanged.IndexOf("BeginUserPriorityWork(\"sort\");", StringComparison.Ordinal),
            Is.LessThan(
                comboChanged.IndexOf("await SortDataAsync(plan.SortId);", StringComparison.Ordinal)
            )
        );
        Assert.That(
            comboChanged.IndexOf("EndUserPriorityWork(\"sort\");", StringComparison.Ordinal),
            Is.GreaterThan(
                comboChanged.IndexOf("await SortDataAsync(plan.SortId);", StringComparison.Ordinal)
            )
        );
        Assert.That(comboChanged, Does.Contain("if (shouldSelectFirstItem)"));
        Assert.That(mainWindowSource, Does.Not.Contain("private async void ComboSort_SelectionChanged("));
    }

    [Test]
    public void 一覧更新後の互換Refreshは選択変化時だけ詳細タグへ流す()
    {
        string mainWindowSource = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string requestSource = GetRepoText("Views", "Main", "MainWindow.MovieViewRequests.cs");
        string readModelUiSource = GetRepoText("Views", "Main", "MainWindow.MovieViewReadModel.cs");
        string filterAsync = GetMethodBlock(
            requestSource,
            "private async Task FilterAndSortAsync("
        );
        string applyReadModel = GetMethodBlock(
            readModelUiSource,
            "private bool TryApplyMovieViewReadModelResultOnUiThread("
        );
        string sortAsync = GetMethodBlock(readModelUiSource, "private async Task<bool> SortDataAsync(");
        string helper = GetMethodBlock(
            mainWindowSource,
            "private bool RefreshSelectionDetailAfterCollectionApplyIfNeeded("
        );

        Assert.That(filterAsync, Does.Contain("TryApplyMovieViewReadModelResultOnUiThread("));
        Assert.That(filterAsync, Does.Not.Contain("MainVM.ReplaceFilteredMovieRecs("));
        Assert.That(filterAsync, Does.Not.Match(@"(?m)^\s*Refresh\(\);\s*$"));
        Assert.That(mainWindowSource, Does.Not.Contain("private bool TryApplyMovieViewReadModelResultOnUiThread("));
        Assert.That(mainWindowSource, Does.Not.Contain("private async Task<bool> SortDataAsync("));
        Assert.That(mainWindowSource, Does.Not.Contain("private async Task FilterAndSortAsync("));

        Assert.That(applyReadModel, Does.Contain("MovieRecords selectedBeforeCollectionApply = GetSelectedItemByTabIndex();"));
        Assert.That(applyReadModel, Does.Contain("MainVM.ReplaceFilteredMovieRecs("));
        Assert.That(applyReadModel, Does.Contain("RefreshSelectionDetailAfterCollectionApplyIfNeeded("));
        Assert.That(applyReadModel, Does.Contain("!isSortOnly && string.Equals(resolvedSortId, \"28\", StringComparison.Ordinal)"));
        Assert.That(applyReadModel, Does.Contain("readmodel apply end: request_revision="));
        Assert.That(
            applyReadModel,
            Does.Contain("MovieViewDiffApplyPolicy.BuildDiffLogFields(diff)")
        );
        Assert.That(applyReadModel, Does.Not.Match(@"(?m)^\s*Refresh\(\);\s*$"));

        Assert.That(sortAsync, Does.Contain("TryApplyMovieViewReadModelResultOnUiThread("));
        Assert.That(sortAsync, Does.Not.Contain("MainVM.ReplaceFilteredMovieRecs("));
        Assert.That(sortAsync, Does.Not.Contain("RefreshThumbnailErrorRecords(force: true)"));
        Assert.That(sortAsync, Does.Not.Match(@"(?m)^\s*Refresh\(\);\s*$"));

        Assert.That(helper, Does.Contain("ShouldRefreshAfterCollectionApply("));
        Assert.That(helper, Does.Contain("ReferenceEquals(selectedBeforeApply, selectedAfterApply)"));
        Assert.That(helper, Does.Contain("Refresh();"));
    }

    [Test]
    public void MovieViewReadModelBuilder_検索sort計算をMainWindow外へ分離する()
    {
        string mainWindowSource = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string requestSource = GetRepoText("Views", "Main", "MainWindow.MovieViewRequests.cs");
        string builderSource = GetRepoText("Views", "Main", "MovieViewReadModelBuilder.cs");
        string readModelUiSource = GetRepoText("Views", "Main", "MainWindow.MovieViewReadModel.cs");
        string filterAsync = GetMethodBlock(
            requestSource,
            "private async Task FilterAndSortAsync("
        );
        string refreshAsync = GetMethodBlock(
            requestSource,
            "private async Task RefreshMovieViewFromCurrentSourceAsync("
        );
        string applyReadModel = GetMethodBlock(
            readModelUiSource,
            "private bool TryApplyMovieViewReadModelResultOnUiThread("
        );

        Assert.That(builderSource, Does.Contain("internal sealed class MovieViewReadModelRequest"));
        Assert.That(builderSource, Does.Contain("internal sealed class MovieViewReadModelResult"));
        Assert.That(builderSource, Does.Contain("internal static class MovieViewReadModelBuilder"));
        Assert.That(builderSource, Does.Contain("public static MovieViewReadModelResult Build("));
        Assert.That(builderSource, Does.Contain("TryBuildChangedMovieRefreshSourceWithReason("));
        Assert.That(builderSource, Does.Not.Contain("ApplyObservedStateToMovieRecord("));
        Assert.That(filterAsync, Does.Contain("MovieViewReadModelBuilder.Build(readModelRequest)"));
        Assert.That(filterAsync, Does.Contain("snapshot_ms="));
        Assert.That(refreshAsync, Does.Contain("MovieViewReadModelBuilder.Build(readModelRequest)"));
        Assert.That(refreshAsync, Does.Contain("CaptureMovieViewReadModelSnapshotOnUiThreadAsync("));
        Assert.That(refreshAsync, Does.Contain("snapshot_ms="));
        Assert.That(readModelUiSource, Does.Contain("private readonly record struct MovieViewReadModelSnapshot"));
        Assert.That(readModelUiSource, Does.Contain("private async Task<MovieViewReadModelSnapshot> CaptureMovieViewReadModelSnapshotOnUiThreadAsync("));
        Assert.That(readModelUiSource, Does.Contain("private static void ApplyObservedStatesToMovieRecords("));
        Assert.That(readModelUiSource, Does.Contain("private readonly record struct MovieViewReadModelApplyResult"));
        Assert.That(readModelUiSource, Does.Contain("private async Task<bool> SortDataAsync("));
        Assert.That(requestSource, Does.Contain("public void FilterAndSort(string id, bool IsGetNew = false)"));
        Assert.That(requestSource, Does.Contain("private async Task FilterAndSortAsync("));
        Assert.That(requestSource, Does.Contain("private async Task RefreshMovieViewFromCurrentSourceAsync("));
        Assert.That(requestSource, Does.Contain("private Task RefreshMovieViewAfterRenameAsync("));
        Assert.That(requestSource, Does.Contain("private CancellationTokenSource BeginFilterAndSortCancellation("));
        Assert.That(requestSource, Does.Contain("internal static string ResolveFilterSortExecutionRouteLabel("));
        Assert.That(requestSource, Does.Contain("internal static string ResolveFilterSortFullReloadReason("));
        Assert.That(requestSource, Does.Contain("internal static bool DoesSearchDependOnDirtyFields("));
        Assert.That(requestSource, Does.Contain("internal static bool DoesCurrentSortDependOnDirtyFields("));
        Assert.That(requestSource, Does.Contain("internal static bool ShouldRunFilterSortOnBackground("));
        Assert.That(requestSource, Does.Contain("internal static bool ShouldUseFastAsciiSearchProjection("));
        Assert.That(mainWindowSource, Does.Not.Contain("private readonly record struct MovieViewReadModelSnapshot"));
        Assert.That(mainWindowSource, Does.Not.Contain("private bool TryApplyMovieViewReadModelResultOnUiThread("));
        Assert.That(mainWindowSource, Does.Not.Contain("public void FilterAndSort(string id, bool IsGetNew = false)"));
        Assert.That(mainWindowSource, Does.Not.Contain("private async Task FilterAndSortAsync("));
        Assert.That(mainWindowSource, Does.Not.Contain("private async Task RefreshMovieViewFromCurrentSourceAsync("));
        Assert.That(mainWindowSource, Does.Not.Contain("private Task RefreshMovieViewAfterRenameAsync("));
        Assert.That(mainWindowSource, Does.Not.Contain("private CancellationTokenSource BeginFilterAndSortCancellation("));
        Assert.That(applyReadModel, Does.Contain("MainVM.ReplaceFilteredMovieRecs("));
    }

    [Test]
    public void ComboSort_段階ロード中のFilterAndSortTrueは全件順序復旧fallbackとして残す()
    {
        string mainWindowSource = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string inputRoutingSource = GetRepoText("Views", "Main", "MainWindow.InputRouting.cs");
        string comboChanged = GetMethodBlock(
            inputRoutingSource,
            "private async void ComboSort_SelectionChanged("
        );

        Assert.That(comboChanged, Does.Contain("if (plan.ShouldUseStartupFullReload)"));
        Assert.That(comboChanged, Does.Contain("FilterAndSort(plan.SortId, true);"));
        Assert.That(comboChanged, Does.Contain("else"));
        Assert.That(comboChanged, Does.Contain("await SortDataAsync(plan.SortId);"));
        Assert.That(mainWindowSource, Does.Not.Contain("private async void ComboSort_SelectionChanged("));
    }

    [Test]
    public void InputRouting境界はMainWindow本体へ戻さない()
    {
        string mainWindowSource = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string mainWindowXaml = GetRepoText("Views", "Main", "MainWindow.xaml");
        string inputRoutingSource = GetRepoText("Views", "Main", "MainWindow.InputRouting.cs");
        string sortComboPolicySource = GetRepoText("Views", "Main", "SortComboSelectionPolicy.cs");

        string[] inputRoutingSignatures =
        [
            "private void OnPreviewTextInput(",
            "private void OnPreviewTextInputStart(",
            "private void OnPreviewTextInputUpdate(",
            "private void Tab_PreviewKeyDown(",
            "private async void ComboSort_SelectionChanged(",
        ];

        foreach (string signature in inputRoutingSignatures)
        {
            Assert.That(inputRoutingSource, Does.Contain(signature));
            Assert.That(mainWindowSource, Does.Not.Contain(signature));
        }

        Assert.That(inputRoutingSource, Does.Contain("TryHandleUpperTabPageScroll(e)"));
        Assert.That(inputRoutingSource, Does.Contain("TryHandleDeleteShortcut(e)"));
        Assert.That(inputRoutingSource, Does.Contain("SortComboSelectionPolicy.BuildPlan("));
        Assert.That(inputRoutingSource, Does.Contain("BeginUserPriorityWork(\"sort\");"));
        Assert.That(inputRoutingSource, Does.Contain("EndUserPriorityWork(\"sort\");"));
        Assert.That(inputRoutingSource, Does.Contain("FilterAndSort(plan.SortId, true);"));
        Assert.That(inputRoutingSource, Does.Contain("await SortDataAsync(plan.SortId);"));
        Assert.That(inputRoutingSource, Does.Contain("RefreshThumbnailErrorRecords(force: true)"));
        Assert.That(inputRoutingSource, Does.Contain("SelectFirstItem();"));
        Assert.That(sortComboPolicySource, Does.Contain("internal static class SortComboSelectionPolicy"));
        Assert.That(sortComboPolicySource, Does.Not.Contain("System.Windows"));
        Assert.That(sortComboPolicySource, Does.Not.Contain("Dispatcher"));
        Assert.That(sortComboPolicySource, Does.Not.Contain("ComboBox"));
        Assert.That(inputRoutingSource, Does.Not.Contain("File."));
        Assert.That(inputRoutingSource, Does.Not.Contain("Directory."));
        Assert.That(inputRoutingSource, Does.Not.Contain("Path.Exists("));
        Assert.That(inputRoutingSource, Does.Not.Contain("_mainDbMovieReadFacade"));
        Assert.That(inputRoutingSource, Does.Not.Contain("LoadMovieTable"));
        Assert.That(inputRoutingSource, Does.Not.Contain("GetSystemTable"));
        Assert.That(inputRoutingSource, Does.Not.Contain("GetWatchTable"));
        Assert.That(inputRoutingSource, Does.Not.Contain("OpenDatafile"));
        Assert.That(inputRoutingSource, Does.Not.Match(@"(?m)^\s*Refresh\(\);\s*$"));
        Assert.That(inputRoutingSource, Does.Not.Contain("Items.Refresh()"));
        Assert.That(mainWindowSource, Does.Contain("AddPreviewTextInputHandler(SearchBox, OnPreviewTextInput)"));
        Assert.That(mainWindowSource, Does.Contain("OnPreviewTextInputStart"));
        Assert.That(mainWindowSource, Does.Contain("OnPreviewTextInputUpdate"));
        Assert.That(mainWindowXaml, Does.Contain("PreviewKeyDown=\"Tab_PreviewKeyDown\""));
        Assert.That(mainWindowXaml, Does.Contain("SelectionChanged=\"ComboSort_SelectionChanged\""));
        Assert.That(inputRoutingSource, Does.Not.Contain("MenuToggleButton_Checked"));
        Assert.That(inputRoutingSource, Does.Not.Contain("MenuToggleButton_Unchecked"));
        Assert.That(mainWindowXaml, Does.Not.Contain("x:Name=\"MenuToggleButton\""));
        Assert.That(mainWindowXaml, Does.Not.Contain("Checked=\"MenuToggleButton_Checked\""));
        Assert.That(mainWindowXaml, Does.Not.Contain("Unchecked=\"MenuToggleButton_Unchecked\""));
    }

    [Test]
    public void FilterAndSortTrueの許容fallbackは起動fallbackと段階ロード中sortだけに固定する()
    {
        string[] actual = EnumerateFilterAndSortTrueCallSites().ToArray();
        string[] expected =
        [
            "Views/Main/MainWindow.Startup.cs|startup-fallback-full-reload|FilterAndSort(sortId, true);",
            "Views/Main/MainWindow.InputRouting.cs|startup-partial-sort-full-order|FilterAndSort(plan.SortId, true);",
        ];

        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Test]
    public void 直書きRefreshの許容入口は起動初回表示と選択変化互換だけに固定する()
    {
        string[] actual = EnumerateDirectRefreshCallSites().ToArray();
        string[] expected =
        [
            "Views/Main/MainWindow.Startup.cs|startup-first-page-detail-sync|Refresh();",
            "Views/Main/MainWindow.xaml.cs|collection-apply-selection-changed-compat|Refresh();",
        ];

        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Test]
    public void ItemsRefreshは本体コードへ戻さない()
    {
        string[] actual = EnumerateProductionCallLines("Items.Refresh()").ToArray();

        Assert.That(actual, Is.Empty);
    }

    [Test]
    public void FilteredMovieRecsの同一stable_key更新はRemoveInsertへ戻さない()
    {
        string viewModelSource = GetRepoText("ViewModels", "MainWindowViewModel.cs");
        string replaceMethod = GetMethodBlock(
            viewModelSource,
            "public FilteredMovieRecsUpdateResult ReplaceFilteredMovieRecs("
        );
        string inPlaceMethod = GetMethodBlock(
            viewModelSource,
            "private bool TryReplaceStableKeyUpdatesInPlace("
        );

        Assert.That(replaceMethod, Does.Contain("TryReplaceStableKeyUpdatesInPlace("));
        Assert.That(
            replaceMethod.IndexOf(
                "TryReplaceStableKeyUpdatesInPlace(",
                StringComparison.Ordinal
            ),
            Is.LessThan(replaceMethod.IndexOf("FilteredMovieRecs.RemoveAt(", StringComparison.Ordinal))
        );
        Assert.That(inPlaceMethod, Does.Contain("removedCount != insertedCount || removedCount < 1"));
        Assert.That(
            inPlaceMethod,
            Does.Contain("FilteredMovieRecs[startIndex + offset] = nextItems[startIndex + offset];")
        );
        Assert.That(inPlaceMethod, Does.Not.Contain("FilteredMovieRecs.RemoveAt("));
        Assert.That(inPlaceMethod, Does.Not.Contain("FilteredMovieRecs.Insert("));
    }

    [Test]
    public void Debugサムネイル全削除後はDB再読込ではなく表示モデルの局所更新へ寄せる()
    {
        string debugSource = GetRepoText(
            "BottomTabs",
            "DebugTab",
            "MainWindow.BottomTab.Debug.cs"
        );
        string deleteMethod = GetMethodBlock(
            debugSource,
            "private async void DebugDeleteThumbnailDir_Click("
        );
        string refreshMethod = GetMethodBlock(
            debugSource,
            "private async Task RefreshLoadedThumbnailUiAfterDebugDeleteAsync("
        );

        Assert.That(deleteMethod, Does.Contain("await RefreshLoadedThumbnailUiAfterDebugDeleteAsync();"));
        Assert.That(deleteMethod, Does.Contain("await Task.Run(() =>"));
        Assert.That(
            deleteMethod.IndexOf("Directory.Exists(thumbnailRoot)", StringComparison.Ordinal),
            Is.GreaterThan(deleteMethod.IndexOf("await Task.Run(() =>", StringComparison.Ordinal))
        );
        Assert.That(
            deleteMethod.IndexOf("Directory.Delete(thumbnailRoot, true)", StringComparison.Ordinal),
            Is.GreaterThan(deleteMethod.IndexOf("await Task.Run(() =>", StringComparison.Ordinal))
        );
        Assert.That(deleteMethod, Does.Not.Contain("FilterAndSort("));
        Assert.That(refreshMethod, Does.Contain("ClearThumbnailPathsForThumbnailOnlyDelete(record)"));
        Assert.That(refreshMethod, Does.Contain("RequestUpperTabVisibleRangeRefresh(immediate: true, reason: \"debug-thumbnail-delete\");"));
        Assert.That(refreshMethod, Does.Contain("RefreshUpperTabPreferredMoviePathKeysRevision();"));
        Assert.That(refreshMethod, Does.Contain("RequestThumbnailErrorSnapshotRefresh();"));
        Assert.That(refreshMethod, Does.Contain("RequestThumbnailProgressSnapshotRefresh();"));
        Assert.That(refreshMethod, Does.Contain("await SortDataAsync(MainVM.DbInfo.Sort);"));
        Assert.That(refreshMethod, Does.Not.Contain("FilterAndSort("));
    }

    [Test]
    public void DebugExplorer起動前の存在確認は背景helperへ逃がす()
    {
        string debugSource = GetRepoText(
            "BottomTabs",
            "DebugTab",
            "MainWindow.BottomTab.Debug.cs"
        );
        string openMethod = GetMethodBlock(
            debugSource,
            "private async void OpenDebugPathInExplorer("
        );
        string resolveMethod = GetMethodBlock(
            debugSource,
            "private static DebugExplorerOpenPlan ResolveDebugExplorerOpenPlan("
        );

        Assert.That(openMethod, Does.Contain("string pathSnapshot = path?.Trim() ?? \"\";"));
        Assert.That(openMethod, Does.Contain("Interlocked.Increment(ref _debugExplorerOpenRequestRevision);"));
        Assert.That(openMethod, Does.Contain("await Task.Run(() =>"));
        Assert.That(openMethod, Does.Contain("ResolveDebugExplorerOpenPlan(pathSnapshot, preferSelectFileSnapshot)"));
        Assert.That(openMethod, Does.Contain("IsDebugExplorerOpenRequestCurrent(requestRevision)"));
        Assert.That(openMethod, Does.Contain("Process.Start(\"explorer.exe\", plan.ExplorerArguments);"));
        Assert.That(openMethod, Does.Contain("ShowDebugPathMissingMessage(plan.MissingMessage);"));
        Assert.That(openMethod, Does.Contain("debug explorer open failed:"));
        Assert.That(openMethod, Does.Not.Contain("File.Exists("));
        Assert.That(openMethod, Does.Not.Contain("Directory.Exists("));
        Assert.That(resolveMethod, Does.Contain("File.Exists(path)"));
        Assert.That(resolveMethod, Does.Contain("Directory.Exists(path)"));
        Assert.That(resolveMethod, Does.Contain("Directory.Exists(parentDir)"));
    }

    [Test]
    public void Debugタブのレコード件数取得は背景helperへ逃がす()
    {
        string debugSource = GetRepoText(
            "BottomTabs",
            "DebugTab",
            "MainWindow.BottomTab.Debug.cs"
        );
        string mainRefresh = GetMethodBlock(
            debugSource,
            "private async void RefreshDebugCurrentDbRecordCount("
        );
        string queueRefresh = GetMethodBlock(
            debugSource,
            "private async void RefreshDebugCurrentQueueDbRecordCount("
        );
        string failureRefresh = GetMethodBlock(
            debugSource,
            "private async void RefreshDebugCurrentFailureDbRecordCount("
        );
        string mainAsync = GetMethodBlock(
            debugSource,
            "private static Task<string> BuildDebugCurrentDbRecordCountTextAsync("
        );
        string queueAsync = GetMethodBlock(
            debugSource,
            "private static Task<string> BuildDebugCurrentQueueDbRecordCountTextAsync("
        );
        string failureAsync = GetMethodBlock(
            debugSource,
            "private static Task<string> BuildDebugCurrentFailureDbRecordCountTextAsync("
        );

        Assert.That(mainRefresh, Does.Contain("string dbPathSnapshot = dbPath ?? \"\";"));
        Assert.That(queueRefresh, Does.Contain("string queueDbPathSnapshot = queueDbPath ?? \"\";"));
        Assert.That(failureRefresh, Does.Contain("string failureDbPathSnapshot = failureDbPath ?? \"\";"));
        Assert.That(mainRefresh, Does.Contain("Interlocked.Increment(ref _debugCurrentDbRecordCountRevision);"));
        Assert.That(queueRefresh, Does.Contain("Interlocked.Increment(ref _debugCurrentQueueDbRecordCountRevision);"));
        Assert.That(failureRefresh, Does.Contain("Interlocked.Increment(ref _debugCurrentFailureDbRecordCountRevision);"));
        Assert.That(mainRefresh, Does.Contain("await BuildDebugCurrentDbRecordCountTextAsync(dbPathSnapshot)"));
        Assert.That(queueRefresh, Does.Contain("await BuildDebugCurrentQueueDbRecordCountTextAsync(queueDbPathSnapshot)"));
        Assert.That(failureRefresh, Does.Contain("await BuildDebugCurrentFailureDbRecordCountTextAsync(failureDbPathSnapshot)"));
        Assert.That(mainRefresh, Does.Contain("IsDebugCurrentDbRecordCountRequestCurrent(requestRevision, dbPathSnapshot)"));
        Assert.That(queueRefresh, Does.Contain("IsDebugCurrentQueueDbRecordCountRequestCurrent(requestRevision, queueDbPathSnapshot)"));
        Assert.That(failureRefresh, Does.Contain("IsDebugCurrentFailureDbRecordCountRequestCurrent(requestRevision, failureDbPathSnapshot)"));
        Assert.That(debugSource, Does.Contain("private bool IsDebugRecordCountUiAvailable()"));
        Assert.That(debugSource, Does.Contain("Dispatcher.HasShutdownStarted"));
        Assert.That(debugSource, Does.Contain("Dispatcher.HasShutdownFinished"));
        Assert.That(mainRefresh, Does.Not.Contain("File.Exists("));
        Assert.That(queueRefresh, Does.Not.Contain("File.Exists("));
        Assert.That(failureRefresh, Does.Not.Contain("File.Exists("));
        Assert.That(mainRefresh, Does.Not.Contain("CreateReadOnlyConnection("));
        Assert.That(queueRefresh, Does.Not.Contain("new SQLiteConnection("));
        Assert.That(failureRefresh, Does.Not.Contain("new SQLiteConnection("));
        Assert.That(mainAsync, Does.Contain("Task.Run(() => BuildDebugCurrentDbRecordCountText(dbPath))"));
        Assert.That(queueAsync, Does.Contain("Task.Run(() => BuildDebugCurrentQueueDbRecordCountText(queueDbPath))"));
        Assert.That(failureAsync, Does.Contain("Task.Run(() => BuildDebugCurrentFailureDbRecordCountText(failureDbPath))"));
    }

    [Test]
    public void DebugタブのDBファイル削除IOは背景helperへ逃がす()
    {
        string debugSource = GetRepoText(
            "BottomTabs",
            "DebugTab",
            "MainWindow.BottomTab.Debug.cs"
        );
        string mainDelete = GetMethodBlock(
            debugSource,
            "private async void DebugDeleteCurrentDb_Click("
        );
        string failureDelete = GetMethodBlock(
            debugSource,
            "private async void DebugDeleteFailureDb_Click("
        );
        string queueDelete = GetMethodBlock(
            debugSource,
            "private async void DebugDeleteQueueDb_Click("
        );
        string deleteHelper = GetMethodBlock(
            debugSource,
            "private static Task DeleteDebugFileIfExistsAsync("
        );
        string existsHelper = GetMethodBlock(
            debugSource,
            "private static Task<bool> DebugFileExistsAsync("
        );

        Assert.That(mainDelete, Does.Contain("ShutdownCurrentDb();"));
        Assert.That(mainDelete, Does.Contain("await DeleteDebugFileIfExistsAsync(dbPath)"));
        Assert.That(mainDelete, Does.Contain("await DebugFileExistsAsync(dbPath)"));
        Assert.That(mainDelete, Does.Contain("QueueApplicationSettingsSave(\"debug-delete-current-db-last-doc\")"));
        Assert.That(mainDelete, Does.Contain("ResetDebugCurrentDbUiState();"));
        Assert.That(
            mainDelete.IndexOf("ShutdownCurrentDb();", StringComparison.Ordinal),
            Is.LessThan(mainDelete.IndexOf("await DeleteDebugFileIfExistsAsync(dbPath)", StringComparison.Ordinal))
        );
        Assert.That(
            mainDelete.IndexOf("await DeleteDebugFileIfExistsAsync(dbPath)", StringComparison.Ordinal),
            Is.LessThan(mainDelete.IndexOf("ResetDebugCurrentDbUiState();", StringComparison.Ordinal))
        );
        Assert.That(mainDelete, Does.Not.Contain("File.Exists("));
        Assert.That(mainDelete, Does.Not.Contain("File.Delete("));
        Assert.That(mainDelete, Does.Not.Contain("Properties.Settings.Default.Save();"));

        Assert.That(failureDelete, Does.Contain("await DeleteDebugFileIfExistsAsync(failureDbPath)"));
        Assert.That(failureDelete, Does.Not.Contain("File.Exists("));
        Assert.That(failureDelete, Does.Not.Contain("File.Delete("));

        Assert.That(queueDelete, Does.Contain("ClearThumbnailQueue();"));
        Assert.That(queueDelete, Does.Contain("await DeleteDebugFileIfExistsAsync(queueDbPath)"));
        Assert.That(
            queueDelete.IndexOf("ClearThumbnailQueue();", StringComparison.Ordinal),
            Is.LessThan(queueDelete.IndexOf("await DeleteDebugFileIfExistsAsync(queueDbPath)", StringComparison.Ordinal))
        );
        Assert.That(queueDelete, Does.Not.Contain("File.Exists("));
        Assert.That(queueDelete, Does.Not.Contain("File.Delete("));

        Assert.That(deleteHelper, Does.Contain("Task.Run(() =>"));
        Assert.That(deleteHelper, Does.Contain("File.Exists(path)"));
        Assert.That(deleteHelper, Does.Contain("File.Delete(path)"));
        Assert.That(existsHelper, Does.Contain("Task.Run(() => File.Exists(path))"));
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

        Assert.Fail($"Repository file not found: {Path.Combine(relativePathParts)}");
        return "";
    }

    private static IEnumerable<string> EnumerateFilterAndSortTrueCallSites()
    {
        foreach ((string relativePath, string trimmedLine) in EnumerateProductionSourceLines())
        {
            if (
                !trimmedLine.Contains("FilterAndSort(", StringComparison.Ordinal)
                || !trimmedLine.Contains(", true", StringComparison.Ordinal)
            )
            {
                continue;
            }

            // 許容 fallback は分類名込みで固定し、通常検索やサムネ後段への逆流を検出する。
            string classification = ClassifyFilterAndSortTrueFallback(relativePath, trimmedLine);
            yield return $"{relativePath}|{classification}|{trimmedLine}";
        }
    }

    private static string ClassifyFilterAndSortTrueFallback(string relativePath, string trimmedLine)
    {
        if (
            relativePath == "Views/Main/MainWindow.Startup.cs"
            && trimmedLine == "FilterAndSort(sortId, true);"
        )
        {
            return "startup-fallback-full-reload";
        }

        if (
            relativePath == "Views/Main/MainWindow.InputRouting.cs"
            && trimmedLine == "FilterAndSort(plan.SortId, true);"
        )
        {
            return "startup-partial-sort-full-order";
        }

        return "unexpected-filter-and-sort-true";
    }

    private static IEnumerable<string> EnumerateDirectRefreshCallSites()
    {
        foreach ((string relativePath, string trimmedLine) in EnumerateProductionSourceLines())
        {
            if (trimmedLine != "Refresh();")
            {
                continue;
            }

            string classification = ClassifyDirectRefreshCall(relativePath, trimmedLine);
            yield return $"{relativePath}|{classification}|{trimmedLine}";
        }
    }

    private static string ClassifyDirectRefreshCall(string relativePath, string trimmedLine)
    {
        if (
            relativePath == "Views/Main/MainWindow.Startup.cs"
            && trimmedLine == "Refresh();"
        )
        {
            return "startup-first-page-detail-sync";
        }

        if (
            relativePath == "Views/Main/MainWindow.xaml.cs"
            && trimmedLine == "Refresh();"
        )
        {
            return "collection-apply-selection-changed-compat";
        }

        return "unexpected-direct-refresh";
    }

    private static IEnumerable<string> EnumerateProductionCallLines(string needle)
    {
        foreach ((string relativePath, string trimmedLine) in EnumerateProductionSourceLines())
        {
            if (trimmedLine.Contains(needle, StringComparison.Ordinal))
            {
                yield return $"{relativePath}|{trimmedLine}";
            }
        }
    }

    private static IEnumerable<(string RelativePath, string TrimmedLine)> EnumerateProductionSourceLines()
    {
        DirectoryInfo repoRoot = GetRepoRoot();
        foreach (
            string sourceRootName in new[]
            {
                "BottomTabs",
                "Thumbnail",
                "UpperTabs",
                "UserControls",
                "Views",
                "Watcher",
            }
        )
        {
            string sourceRoot = Path.Combine(repoRoot.FullName, sourceRootName);
            if (!Directory.Exists(sourceRoot))
            {
                continue;
            }

            foreach (
                string filePath in Directory.EnumerateFiles(
                    sourceRoot,
                    "*.cs",
                    SearchOption.AllDirectories
                )
            )
            {
                string relativePath = NormalizeRepoRelativePath(repoRoot, filePath);
                foreach (string line in File.ReadLines(filePath))
                {
                    yield return (relativePath, line.Trim());
                }
            }
        }
    }

    private static DirectoryInfo GetRepoRoot()
    {
        foreach (DirectoryInfo searchRoot in EnumerateRepoSearchRoots())
        {
            DirectoryInfo? current = searchRoot;
            while (current != null)
            {
                string candidate = Path.Combine(
                    current.FullName,
                    "Views",
                    "Main",
                    "MainWindow.xaml.cs"
                );
                if (File.Exists(candidate))
                {
                    return current;
                }

                current = current.Parent;
            }
        }

        Assert.Fail("Repository root not found.");
        return new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
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

    private static string NormalizeRepoRelativePath(DirectoryInfo repoRoot, string filePath)
    {
        return Path.GetRelativePath(repoRoot.FullName, filePath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
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
