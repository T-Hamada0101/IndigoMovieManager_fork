using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MainWindowMenuActionsPolicyTests
{
    [Test]
    public void MenuScore_Click_DB更新は背景へ逃がす()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.MenuActions.cs");
        string scoreMethod = GetMethodBlock(source, "private void MenuScore_Click(");
        string persistMethod = GetMethodBlock(source, "private void QueueMovieScorePersist(");

        Assert.That(scoreMethod, Does.Contain("QueueMovieScorePersist("));
        Assert.That(scoreMethod, Does.Not.Contain("_mainDbMovieMutationFacade.UpdateScore("));
        Assert.That(persistMethod, Does.Contain("Task.Run("));
        Assert.That(persistMethod, Does.Contain("_mainDbMovieMutationFacade.UpdateScore("));
        Assert.That(persistMethod, Does.Contain("score persist failed"));
    }

    [Test]
    public void FileMove_movie_path更新は背景へ逃がす()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.MenuActions.cs");
        string moveMethod = GetMethodBlock(source, "private void MenuCopyAndMove_Click(");
        string moveQueueMethod = GetMethodBlock(source, "private void QueueMovieFileMove(");
        string moveBackgroundMethod = GetMethodBlock(
            source,
            "private static MovieMoveBackgroundResult MoveMovieFilesInBackground("
        );
        string moveCompletionMethod = GetMethodBlock(
            source,
            "private async Task CompleteMovieFileMoveOnUiAsync("
        );
        string persistMethod = GetMethodBlock(source, "private void QueueMoviePathPersist(");

        Assert.That(moveMethod, Does.Contain("QueueMovieFileMove(mv, destFolder);"));
        Assert.That(moveMethod, Does.Not.Contain("File.Move("));
        Assert.That(moveMethod, Does.Not.Contain("Refresh();"));
        Assert.That(moveQueueMethod, Does.Contain("Task.Run("));
        Assert.That(moveBackgroundMethod, Does.Contain("File.Move("));
        Assert.That(moveCompletionMethod, Does.Contain("QueueMoviePathPersist("));
        Assert.That(moveCompletionMethod, Does.Contain("ReflectMovedMovieRecordsOnUi("));
        Assert.That(moveCompletionMethod, Does.Contain("await SortDataAsync(sortId);"));
        Assert.That(moveMethod, Does.Not.Contain("_mainDbMovieMutationFacade.UpdateMoviePath("));
        Assert.That(persistMethod, Does.Contain("Task.Run("));
        Assert.That(persistMethod, Does.Contain("_mainDbMovieMutationFacade.UpdateMoviePath("));
        Assert.That(persistMethod, Does.Contain("movie path persist failed"));
    }

    [Test]
    public void FileCopy_ファイルI_Oは背景へ逃がす()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.MenuActions.cs");
        string copyMoveMethod = GetMethodBlock(source, "private void MenuCopyAndMove_Click(");
        string copyQueueMethod = GetMethodBlock(source, "private void QueueMovieFileCopy(");

        Assert.That(copyMoveMethod, Does.Contain("QueueMovieFileCopy(mv, destFolder);"));
        Assert.That(copyMoveMethod, Does.Not.Contain("File.Copy("));
        Assert.That(copyQueueMethod, Does.Contain("Task.Run("));
        Assert.That(copyQueueMethod, Does.Contain("File.Copy("));
        Assert.That(copyQueueMethod, Does.Contain("RestoreFileWatchers("));
        Assert.That(copyQueueMethod, Does.Contain("ShowFileCopyFailureSummary("));
    }

    [Test]
    public void 親フォルダを開く存在確認は背景へ逃がす()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.MenuActions.cs");
        string clickMethod = GetMethodBlock(source, "private void OpenParentFolder_Click(");
        string queueMethod = GetMethodBlock(source, "private void QueueOpenParentFolderExplorer(");

        Assert.That(clickMethod, Does.Contain("QueueOpenParentFolderExplorer(mv.Movie_Path, mv.Dir);"));
        Assert.That(clickMethod, Does.Not.Contain("Path.Exists("));
        Assert.That(queueMethod, Does.Contain("Task.Run("));
        Assert.That(queueMethod, Does.Contain("Path.Exists(moviePathSnapshot)"));
        Assert.That(queueMethod, Does.Contain("Path.Exists(dirSnapshot)"));
        Assert.That(queueMethod, Does.Contain("Dispatcher.InvokeAsync("));
        Assert.That(queueMethod, Does.Contain("Process.Start(\"explorer.exe\""));
    }

    [Test]
    public void 新規DB作成ダイアログ後のI_Oは背景へ逃がす()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.MenuActions.cs");
        string clickMethod = GetMethodBlock(source, "private async void BtnNew_Click(");
        string dialogMethod = GetMethodBlock(source, "private async Task<bool> TryCreateMainDbFromDialogAsync(");
        string backgroundMethod = GetMethodBlock(
            source,
            "private static Task<MainDbCreateDialogBackgroundResult> CreateMainDbFromDialogInBackgroundAsync("
        );

        Assert.That(clickMethod, Does.Contain("await TryCreateMainDbFromDialogAsync();"));
        Assert.That(clickMethod, Does.Not.Contain("Path.Exists("));
        Assert.That(clickMethod, Does.Not.Contain("TryCreateDatabase("));
        Assert.That(dialogMethod, Does.Contain("CreateMainDbFromDialogInBackgroundAsync(dbFullPathSnapshot)"));
        Assert.That(dialogMethod, Does.Contain("await TrySwitchMainDb(dbFullPathSnapshot, MainDbSwitchSource.New)"));
        Assert.That(dialogMethod, Does.Contain("RememberMainDbDialogDirectory(dbFullPathSnapshot);"));
        Assert.That(dialogMethod, Does.Contain("AreSameMainDbPath("));
        Assert.That(dialogMethod, Does.Not.Contain("Path.Exists("));
        Assert.That(dialogMethod, Does.Not.Contain("TryCreateDatabase("));
        Assert.That(backgroundMethod, Does.Contain("Task.Run("));
        Assert.That(backgroundMethod, Does.Contain("Path.Exists(dbFullPathSnapshot)"));
        Assert.That(backgroundMethod, Does.Contain("TryCreateDatabase(dbFullPathSnapshot"));
    }

    [Test]
    public void WatchFolderDropの新規DB作成待ちはasync経路へ寄せる()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.WatchFolderDrop.cs");
        string dropMethod = GetMethodBlock(source, "private async void MainWindow_Drop(");
        string ensureMethod = GetMethodBlock(
            source,
            "private async Task<bool> EnsureMainDbReadyForWatchFolderDropAsync("
        );

        Assert.That(dropMethod, Does.Contain("await EnsureMainDbReadyForWatchFolderDropAsync()"));
        Assert.That(dropMethod, Does.Not.Contain("TryCreateMainDbFromDialog"));
        Assert.That(ensureMethod, Does.Contain("await TryCreateMainDbFromDialogAsync();"));
        Assert.That(ensureMethod, Does.Not.Contain("Path.Exists("));
        Assert.That(ensureMethod, Does.Not.Contain("TryCreateDatabase("));
    }

    [Test]
    public void HeaderReload明示FullReloadはruntimeログへ判断材料を出す()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.MenuActions.cs");
        string reloadMethod = GetMethodBlock(source, "internal async Task ExecuteHeaderReloadAsync(");

        Assert.That(reloadMethod, Does.Contain("Stopwatch reloadStopwatch = Stopwatch.StartNew();"));
        Assert.That(reloadMethod, Does.Contain("const string fullReloadReason = \"header-explicit\";"));
        Assert.That(reloadMethod, Does.Contain("string reloadId = CreateHeaderReloadLogCorrelationId();"));
        Assert.That(reloadMethod, Does.Contain("bool externalSkinRefreshQueued = false;"));
        Assert.That(reloadMethod, Does.Contain("bool deferredScanScheduled = false;"));
        Assert.That(reloadMethod, Does.Contain("header reload begin:"));
        Assert.That(reloadMethod, Does.Contain("header reload end:"));
        Assert.That(reloadMethod, Does.Contain("header reload failed:"));
        Assert.That(CountOccurrences(reloadMethod, "reload_id={reloadId}"), Is.EqualTo(3));
        Assert.That(reloadMethod, Does.Contain("full_reload_reason={fullReloadReason}"));
        Assert.That(reloadMethod, Does.Contain("external_skin_refresh_queued={FormatRuntimeLogBool(externalSkinRefreshQueued)}"));
        Assert.That(reloadMethod, Does.Contain("deferred_scan_scheduled={FormatRuntimeLogBool(deferredScanScheduled)}"));
        Assert.That(reloadMethod, Does.Contain("elapsed_ms={reloadStopwatch.ElapsedMilliseconds}"));
        Assert.That(reloadMethod, Does.Contain("type={ex.GetType().Name}"));
        Assert.That(reloadMethod, Does.Contain("ScheduleDeferredManualReloadScan(trigger, reloadId);"));
        Assert.That(reloadMethod, Does.Contain("externalSkinRefreshQueued = QueueExternalSkinHostRefresh(\"header-reload\");"));
        Assert.That(reloadMethod, Does.Not.Contain("externalSkinRefreshQueued = true;"));
        Assert.That(reloadMethod, Does.Contain("deferredScanScheduled = true;"));
        Assert.That(reloadMethod, Does.Contain("throw;"));
        Assert.That(reloadMethod, Does.Contain("finally"));
        Assert.That(reloadMethod, Does.Contain("if (watchUiSuppressionStarted)"));
        Assert.That(
            CountOccurrences(reloadMethod, "EndWatchUiSuppression(\"manual-reload\");"),
            Is.EqualTo(1)
        );
    }

    [Test]
    public void HeaderReload遅延ManualScanログは同じ相関IDを引き継ぐ()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.MenuActions.cs");
        string scheduleMethod = GetMethodBlock(
            source,
            "private void ScheduleDeferredManualReloadScan("
        );
        string runMethod = GetMethodBlock(
            source,
            "private async Task RunDeferredManualReloadScanAsync("
        );
        string skipMethod = GetMethodBlock(
            source,
            "private static void LogDeferredManualReloadScanSkipped("
        );

        Assert.That(scheduleMethod, Does.Contain("string reloadId"));
        Assert.That(scheduleMethod, Does.Contain("RunDeferredManualReloadScanAsync(trigger, reloadId);"));

        Assert.That(runMethod, Does.Contain("string reloadId"));
        Assert.That(runMethod, Does.Contain("LogDeferredManualReloadScanSkipped(trigger, reloadId, skipReason);"));
        Assert.That(runMethod, Does.Contain("manual reload deferred scan scheduled: reload_id={reloadId} trigger={trigger}"));
        Assert.That(runMethod, Does.Contain("manual reload deferred scan failed: reload_id={reloadId} trigger={trigger}"));
        Assert.That(runMethod, Does.Contain("QueueCheckFolderAsync(CheckMode.Manual, $\"{trigger}:deferred\")"));

        Assert.That(skipMethod, Does.Contain("string reloadId"));
        Assert.That(skipMethod, Does.Contain("manual reload deferred scan skipped: reload_id={reloadId} trigger={trigger} reason={reason}"));
    }

    [Test]
    public void RenameFile_watcher抑止はfinallyで復旧する()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.MenuActions.cs");
        string renameMethod = GetMethodBlock(source, "private async void RenameFile_Click(");
        string moveMethod = GetMethodBlock(
            source,
            "private static string TryMoveMovieFileInBackground("
        );

        Assert.That(renameMethod, Does.Contain("SetFileWatchersEnabled(destFolder, enabled: false);"));
        Assert.That(renameMethod, Does.Contain("try"));
        Assert.That(renameMethod, Does.Contain("await Task.Run("));
        Assert.That(renameMethod, Does.Contain("await RenameThumbAsync(destMoveFile, oldMoviePath);"));
        Assert.That(renameMethod, Does.Contain("finally"));
        Assert.That(renameMethod, Does.Contain("RestoreFileWatchers(suppressedWatchers);"));
        Assert.That(moveMethod, Does.Contain("mvFile.MoveTo(destinationPath, true);"));
        Assert.That(renameMethod, Does.Not.Contain("watcher.EnableRaisingEvents = false;"));
        Assert.That(renameMethod, Does.Not.Contain("watcher.EnableRaisingEvents = true;"));
    }

    [Test]
    public void サムネイルのみ削除は背景へ逃がす()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.MenuActions.cs");
        string deleteMethod = GetMethodBlock(source, "private void ExecuteDeleteAction(");
        string queueMethod = GetMethodBlock(source, "private void QueueThumbnailOnlyDelete(");
        string coreMethod = GetMethodBlock(source, "private void DeleteThumbnailsForMovieCore(");

        Assert.That(deleteMethod, Does.Contain("QueueThumbnailOnlyDelete(mv);"));
        Assert.That(queueMethod, Does.Contain("Task.Run("));
        Assert.That(queueMethod, Does.Contain("DeleteThumbnailsForMovieCore("));
        Assert.That(queueMethod, Does.Contain("Dispatcher.InvokeAsync("));
        Assert.That(queueMethod, Does.Contain("RefreshVisibleThumbnailUiAfterThumbnailOnlyDelete("));
        Assert.That(queueMethod, Does.Not.Contain("FilterAndSort("));
        Assert.That(coreMethod, Does.Contain("TryDeleteThumbnailFile("));
        Assert.That(coreMethod, Does.Contain("TryDeleteThumbnailErrorMarker("));
    }

    [Test]
    public void サムネイルのみ削除後は表示パスとERROR件数を局所更新する()
    {
        MovieRecords record = new()
        {
            ThumbPathSmall = "thumb-small.jpg",
            ThumbPathBig = "thumb-big.jpg",
            ThumbPathGrid = "thumb-grid.jpg",
            ThumbPathList = "thumb-list.jpg",
            ThumbPathBig10 = "thumb-big10.jpg",
            ThumbDetail = "thumb-detail.jpg",
            ThumbnailErrorMarkerCount = 2,
        };
        List<string> changedProperties = [];
        record.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName ?? "");

        bool changed = MainWindow.ClearThumbnailPathsForThumbnailOnlyDelete(record);

        Assert.That(changed, Is.True);
        Assert.That(record.ThumbPathSmall, Is.Empty);
        Assert.That(record.ThumbPathBig, Is.Empty);
        Assert.That(record.ThumbPathGrid, Is.Empty);
        Assert.That(record.ThumbPathList, Is.Empty);
        Assert.That(record.ThumbPathBig10, Is.Empty);
        Assert.That(record.ThumbDetail, Is.Empty);
        Assert.That(record.ThumbnailErrorMarkerCount, Is.Zero);
        Assert.That(changedProperties, Does.Contain(nameof(MovieRecords.ThumbPathSmall)));
        Assert.That(changedProperties, Does.Contain(nameof(MovieRecords.ThumbPathBig)));
        Assert.That(changedProperties, Does.Contain(nameof(MovieRecords.ThumbPathGrid)));
        Assert.That(changedProperties, Does.Contain(nameof(MovieRecords.ThumbPathList)));
        Assert.That(changedProperties, Does.Contain(nameof(MovieRecords.ThumbPathBig10)));
        Assert.That(changedProperties, Does.Contain(nameof(MovieRecords.ThumbDetail)));
        Assert.That(changedProperties, Does.Contain(nameof(MovieRecords.ThumbnailErrorMarkerCount)));
    }

    [Test]
    public void サムネイルのみ削除後のUI反映は既存の軽量更新へ寄せる()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.MenuActions.cs");
        string refreshMethod = GetMethodBlock(
            source,
            "private void RefreshVisibleThumbnailUiAfterThumbnailOnlyDelete("
        );

        Assert.That(refreshMethod, Does.Contain("InvalidateThumbnailErrorRecords(refreshIfVisible: true);"));
        Assert.That(
            refreshMethod,
            Does.Contain(
                "RequestUpperTabVisibleRangeRefresh(immediate: true, reason: \"thumbnail-only-delete\");"
            )
        );
        Assert.That(refreshMethod, Does.Contain("RefreshUpperTabPreferredMoviePathKeysRevision();"));
        Assert.That(refreshMethod, Does.Contain("RequestThumbnailErrorSnapshotRefresh();"));
        Assert.That(refreshMethod, Does.Contain("RequestThumbnailProgressSnapshotRefresh();"));
        Assert.That(refreshMethod, Does.Not.Contain("FilterAndSort("));
    }

    [Test]
    public void 手動救済とIndexRepair後は受付時だけ局所更新へ寄せる()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.MenuActions.cs");
        string rescueMethod = GetMethodBlock(source, "private void RunThumbnailRescueMenuAction(");
        string repairMethod = GetMethodBlock(
            source,
            "private void RunThumbnailIndexRepairMenuActionCore("
        );
        string refreshMethod = GetMethodBlock(
            source,
            "private void RefreshThumbnailManualUserActionUiIfAccepted("
        );

        Assert.That(
            CountOccurrences(rescueMethod, "RefreshThumbnailManualUserActionUiIfAccepted("),
            Is.EqualTo(2)
        );
        Assert.That(rescueMethod, Does.Contain("upperDispatchResult.AcceptedCount"));
        Assert.That(rescueMethod, Does.Contain("normalDispatchResult.AcceptedCount"));
        Assert.That(rescueMethod, Does.Not.Contain("Refresh();"));
        Assert.That(repairMethod, Does.Contain("dispatchResult.StartedCount"));
        Assert.That(repairMethod, Does.Contain("RefreshThumbnailManualUserActionUiIfAccepted("));
        Assert.That(repairMethod, Does.Not.Contain("Refresh();"));
        Assert.That(refreshMethod, Does.Contain("if (acceptedOrStartedCount <= 0)"));
        Assert.That(refreshMethod, Does.Contain("InvalidateThumbnailErrorRecords(refreshIfVisible: true);"));
        Assert.That(
            refreshMethod,
            Does.Contain("RequestUpperTabVisibleRangeRefresh(immediate: true, reason: reason);")
        );
        Assert.That(refreshMethod, Does.Contain("RefreshUpperTabPreferredMoviePathKeysRevision();"));
        Assert.That(refreshMethod, Does.Contain("RequestThumbnailErrorSnapshotRefresh();"));
        Assert.That(refreshMethod, Does.Contain("RequestThumbnailProgressSnapshotRefresh();"));
        Assert.That(refreshMethod, Does.Not.Contain("FilterAndSort("));
    }

    [Test]
    public void 動画削除後はDB再読込ではなく局所削除反映へ寄せる()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.MenuActions.cs");
        string deleteMethod = GetMethodBlock(source, "private void ExecuteDeleteAction(");
        string queueMethod = GetMethodBlock(source, "private void QueueConfirmedMovieDelete(");
        string backgroundMethod = GetMethodBlock(source, "private MovieDeleteBackgroundResult DeleteMoviesInBackground(");
        string dbDeleteMethod = GetMethodBlock(source, "private int TryDeleteMovieTableInBackground(");
        string fileDeleteMethod = GetMethodBlock(source, "private static void DeletePhysicalMovieFileInBackground(");
        string refreshMethod = GetMethodBlock(
            source,
            "private void RefreshVisibleMovieUiAfterMovieDelete("
        );

        Assert.That(deleteMethod, Does.Contain("QueueConfirmedMovieDelete("));
        Assert.That(deleteMethod, Does.Not.Contain("DeleteMovieTable("));
        Assert.That(deleteMethod, Does.Not.Contain("TryDeletePhysicalFile("));
        Assert.That(deleteMethod, Does.Not.Contain("FilterAndSort(MainVM.DbInfo.Sort, true);"));
        Assert.That(queueMethod, Does.Contain("Task.Run("));
        Assert.That(queueMethod, Does.Contain("DeleteMoviesInBackground("));
        Assert.That(queueMethod, Does.Contain("Dispatcher.InvokeAsync("));
        Assert.That(queueMethod, Does.Contain("ShowDeleteFailureSummary("));
        Assert.That(queueMethod, Does.Contain("AreSameMainDbPath("));
        Assert.That(queueMethod, Does.Contain("RefreshVisibleMovieUiAfterMovieDelete(result.DeletedRecords);"));
        Assert.That(backgroundMethod, Does.Contain("TryDeleteMovieTableInBackground("));
        Assert.That(backgroundMethod, Does.Contain("TryAdjustRegisteredMovieCount("));
        Assert.That(backgroundMethod, Does.Contain("DeletePhysicalMovieFileInBackground("));
        Assert.That(dbDeleteMethod, Does.Contain("DeleteMovieTable("));
        Assert.That(fileDeleteMethod, Does.Contain("TryDeletePhysicalFile("));
        Assert.That(refreshMethod, Does.Contain("RemoveDeletedMovieRecordsById(MainVM.MovieRecs, deletedMovieIds);"));
        Assert.That(refreshMethod, Does.Contain("MainVM.ReplaceFilteredMovieRecs("));
        Assert.That(refreshMethod, Does.Contain("MainVM.DbInfo.SearchCount = nextFilteredMovies.Length;"));
        Assert.That(refreshMethod, Does.Contain("RequestUpperTabVisibleRangeRefresh(immediate: true, reason: \"movie-delete\");"));
        Assert.That(refreshMethod, Does.Contain("RequestThumbnailErrorSnapshotRefresh();"));
        Assert.That(refreshMethod, Does.Contain("RequestThumbnailProgressSnapshotRefresh();"));
        Assert.That(refreshMethod, Does.Not.Contain("FilterAndSort("));
    }

    [Test]
    public void 動画削除の局所反映は削除成功IDだけ抜く()
    {
        MovieRecords keep = new() { Movie_Id = 1, Movie_Name = "keep" };
        MovieRecords deletedA = new() { Movie_Id = 2, Movie_Name = "delete-a" };
        MovieRecords deletedB = new() { Movie_Id = 3, Movie_Name = "delete-b" };
        List<MovieRecords> records = [keep, deletedA, deletedB];

        int removedCount = MainWindow.RemoveDeletedMovieRecordsById(
            records,
            new HashSet<long> { 2, 3 }
        );

        Assert.That(removedCount, Is.EqualTo(2));
        Assert.That(records.Select(record => record.Movie_Id), Is.EqualTo(new[] { 1L }));
    }

    [Test]
    public void タグ操作_DB更新は背景へ逃がす()
    {
        string tagSource = GetRepoText("Views", "Main", "MainWindow.Tag.cs");
        string bottomTagEditorSource = GetRepoText(
            "BottomTabs",
            "TagEditor",
            "MainWindow.BottomTab.TagEditor.cs"
        );
        string tagControlSource = GetRepoText("UserControls", "TagControl.xaml.cs");
        string persistMethod = GetMethodBlock(
            tagSource,
            "internal void QueueMovieTagPersist("
        );

        Assert.That(tagSource, Does.Contain("QueueMovieTagPersist("));
        Assert.That(bottomTagEditorSource, Does.Contain("QueueMovieTagPersist("));
        Assert.That(tagControlSource, Does.Contain("QueueMovieTagPersist("));
        Assert.That(CountOccurrences(tagSource, "_mainDbMovieMutationFacade.UpdateTag("), Is.EqualTo(1));
        Assert.That(bottomTagEditorSource, Does.Not.Contain("_mainDbMovieMutationFacade.UpdateTag("));
        Assert.That(tagControlSource, Does.Not.Contain("MainDbMovieMutationFacade.UpdateTag("));
        Assert.That(persistMethod, Does.Contain("Task.Run("));
        Assert.That(persistMethod, Does.Contain("_mainDbMovieMutationFacade.UpdateTag("));
        Assert.That(persistMethod, Does.Contain("tag persist failed"));
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

    private static int CountOccurrences(string source, string text)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(text, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += text.Length;
        }

        return count;
    }
}
