using System;
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
        Assert.That(coreMethod, Does.Contain("TryDeleteThumbnailFile("));
        Assert.That(coreMethod, Does.Contain("TryDeleteThumbnailErrorMarker("));
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
