using System;
using System.Collections.Generic;
using System.IO;
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
        string persistMethod = GetMethodBlock(source, "private void QueueMoviePathPersist(");

        Assert.That(moveMethod, Does.Contain("QueueMoviePathPersist("));
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
        Assert.That(copyMoveMethod, Does.Contain("try"));
        Assert.That(copyMoveMethod, Does.Contain("finally"));
        Assert.That(copyQueueMethod, Does.Contain("Task.Run("));
        Assert.That(copyQueueMethod, Does.Contain("File.Copy("));
        Assert.That(copyQueueMethod, Does.Contain("RestoreFileWatchers("));
        Assert.That(copyQueueMethod, Does.Contain("ShowFileCopyFailureSummary("));
    }

    [Test]
    public void RenameFile_watcher抑止はfinallyで復旧する()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.MenuActions.cs");
        string renameMethod = GetMethodBlock(source, "private void RenameFile_Click(");

        Assert.That(renameMethod, Does.Contain("SetFileWatchersEnabled(destFolder, enabled: false);"));
        Assert.That(renameMethod, Does.Contain("try"));
        Assert.That(renameMethod, Does.Contain("RenameThumb(destMoveFile, mv.Movie_Path);"));
        Assert.That(renameMethod, Does.Contain("finally"));
        Assert.That(renameMethod, Does.Contain("RestoreFileWatchers(suppressedWatchers);"));
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
