namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WatcherRenameBridgePolicyTests
{
    [Test]
    public void RenameThumbAsync_重いI_OとDB更新は背景へ逃がし後着guard後に反映する()
    {
        string source = GetRepoText("Watcher", "MainWindow.WatcherRenameBridge.cs");
        string renameMethod = GetMethodBlock(source, "private async Task RenameThumbAsync(");
        string snapshotMethod = GetMethodBlock(
            source,
            "private RenameBridgeUiSnapshot CreateRenameBridgeUiSnapshotOnUiThread("
        );
        string backgroundMethod = GetMethodBlock(
            source,
            "private RenameBridgeBackgroundResult RunRenameBridgeBackgroundWork("
        );
        string guardMethod = GetMethodBlock(
            source,
            "private bool CanApplyRenameBridgeResult("
        );

        Assert.That(renameMethod, Does.Contain("CreateRenameBridgeUiSnapshotAsync("));
        Assert.That(renameMethod, Does.Contain("await Task.Run("));
        Assert.That(renameMethod, Does.Contain("RunRenameBridgeBackgroundWork("));
        Assert.That(renameMethod, Does.Contain("CanApplyRenameBridgeResult(snapshot)"));
        Assert.That(renameMethod, Does.Contain("await RefreshMovieViewAfterRenameAsync("));
        Assert.That(snapshotMethod, Does.Contain("item.Movie_Path = eFullPath;"));
        Assert.That(snapshotMethod, Does.Contain("ThumbnailRenameAssetTransferHelper.CreatePathSnapshot(item)"));
        Assert.That(snapshotMethod, Does.Not.Contain("_mainDbMovieMutationFacade.UpdateMoviePath("));
        Assert.That(backgroundMethod, Does.Contain("PersistRenameBridgeMovieSnapshot("));
        Assert.That(backgroundMethod, Does.Contain("ThumbnailRenameAssetTransferHelper.RenameThumbnailFiles("));
        Assert.That(backgroundMethod, Does.Contain("Directory.Exists(snapshot.BookmarkFolder)"));
        Assert.That(guardMethod, Does.Contain("Dispatcher.HasShutdownStarted"));
        Assert.That(guardMethod, Does.Contain("Dispatcher.HasShutdownFinished"));
        Assert.That(guardMethod, Does.Contain("AreSameMainDbPath("));
    }

    [Test]
    public void RenameThumbAsync_未登録renameの存在確認も背景へ逃がす()
    {
        string source = GetRepoText("Watcher", "MainWindow.WatcherRenameBridge.cs");
        string queueMethod = GetMethodBlock(
            source,
            "private async Task TryQueueWatchScanForUntrackedRenameAsync("
        );

        Assert.That(queueMethod, Does.Contain("await Task.Run("));
        Assert.That(queueMethod, Does.Contain("ShouldQueueWatchScanForUntrackedRename("));
        Assert.That(queueMethod, Does.Contain("QueueCheckFolderAsync(CheckMode.Watch"));
    }

    [Test]
    public void IsMoviePathMatchForRename_case違いでも一致として扱う()
    {
        bool actual = MainWindow.IsMoviePathMatchForRename(
            @"C:\Movies\Sample.MP4",
            @"c:\movies\sample.mp4"
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void BuildBookmarkRenameDestinationPath_親フォルダは変えずにファイル名だけ差し替える()
    {
        string actual = MainWindow.BuildBookmarkRenameDestinationPath(
            @"C:\Bookmarks\OldName\Collection\clip_OldName_scene.jpg",
            "OldName",
            "NewName"
        );

        Assert.That(
            actual,
            Is.EqualTo(@"C:\Bookmarks\OldName\Collection\clip_NewName_scene.jpg")
        );
    }

    [Test]
    public void ShouldQueueWatchScanForUntrackedRename_同一パスや未存在ファイルは除外する()
    {
        Assert.That(
            MainWindow.ShouldQueueWatchScanForUntrackedRename(
                @"C:\Movies\same.mp4",
                @"C:\Movies\same.mp4"
            ),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldQueueWatchScanForUntrackedRename(
                @"C:\Movies\missing.mp4",
                @"C:\Movies\before.mp4"
            ),
            Is.False
        );
    }

    [Test]
    public async Task ShouldQueueWatchScanForUntrackedRename_新パスが存在すればtrueを返す()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string newMoviePath = Path.Combine(tempRoot, "after.mp4");
        await File.WriteAllBytesAsync(newMoviePath, [0x1]);

        try
        {
            Assert.That(
                MainWindow.ShouldQueueWatchScanForUntrackedRename(
                    newMoviePath,
                    Path.Combine(tempRoot, "before.mp4")
                ),
                Is.True
            );
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
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
