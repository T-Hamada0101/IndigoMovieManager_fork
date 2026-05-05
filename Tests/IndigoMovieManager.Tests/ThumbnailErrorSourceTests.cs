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
