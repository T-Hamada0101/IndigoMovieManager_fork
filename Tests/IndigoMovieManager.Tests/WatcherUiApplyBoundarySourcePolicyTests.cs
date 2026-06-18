using System.Runtime.CompilerServices;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WatcherUiApplyBoundarySourcePolicyTests
{
    private static readonly string[] WatcherChangeSetBoundaryFiles =
    [
        "MainWindow.WatcherUiBridge.cs",
        "MainWindow.WatchUiReloadPolicy.cs",
        "MainWindow.WatchMovieViewConsistencyPolicy.cs",
        "MainWindow.WatcherRenameBridge.cs",
        "MainWindow.WatchScanCoordinator.cs",
        "MainWindow.Watcher.cs",
    ];

    [Test]
    public void WatcherChangeSet_UI再適用入口はreload_policyへ集約する()
    {
        string[] filterAndSortCalls = EnumerateWatcherBoundaryCallLines("FilterAndSort(")
            .ToArray();
        string[] refreshMovieViewCalls = EnumerateWatcherBoundaryCallLines(
                "RefreshMovieViewFromCurrentSourceAsync("
            )
            .ToArray();
        string[] directRefreshCalls = EnumerateWatcherBoundaryLines()
            .Where(x => x.TrimmedLine == "Refresh();")
            .Select(x => $"{x.RelativePath}|{x.TrimmedLine}")
            .ToArray();
        string[] itemsRefreshCalls = EnumerateWatcherBoundaryCallLines("Items.Refresh()")
            .ToArray();

        Assert.That(
            filterAndSortCalls,
            Is.EqualTo(
                [
                    "Watcher/MainWindow.WatchUiReloadPolicy.cs|FilterAndSort(sort, isGetNew);",
                ]
            )
        );
        Assert.That(
            refreshMovieViewCalls,
            Is.EqualTo(
                [
                    "Watcher/MainWindow.WatchUiReloadPolicy.cs|_ = RefreshMovieViewFromCurrentSourceAsync(",
                ]
            )
        );
        Assert.That(directRefreshCalls, Is.Empty);
        Assert.That(itemsRefreshCalls, Is.Empty);
    }

    [Test]
    public void WatcherUiBridge_変更setを表示collectionへ直接applyしない()
    {
        string source = GetRepoText("Watcher", "MainWindow.WatcherUiBridge.cs");

        Assert.That(source, Does.Not.Contain("WatchChangedMovie"));
        Assert.That(source, Does.Not.Contain("ReplaceFilteredMovieRecs("));
        Assert.That(source, Does.Not.Contain("SortData("));
        Assert.That(source, Does.Not.Match(@"MainVM\??\.FilteredMovieRecs\.(Add|Clear|Insert|Remove|RemoveAt)\("));
        Assert.That(source, Does.Not.Match(@"MainVM\.FilteredMovieRecs\.(Add|Clear|Insert|Remove|RemoveAt)\("));
    }

    [Test]
    public void WatchMovieViewConsistencyPolicy_判断だけを返しUI操作へ進まない()
    {
        string source = GetRepoText(
            "Watcher",
            "MainWindow.WatchMovieViewConsistencyPolicy.cs"
        );

        Assert.That(source, Does.Contain("EvaluateMovieViewConsistency("));
        Assert.That(source, Does.Not.Contain("Dispatcher"));
        Assert.That(source, Does.Not.Contain("MainVM"));
        Assert.That(source, Does.Not.Contain("FilterAndSort("));
        Assert.That(source, Does.Not.Contain("RefreshMovieViewFromCurrentSourceAsync("));
        Assert.That(source, Does.Not.Contain("Refresh();"));
        Assert.That(source, Does.Not.Contain("Items.Refresh()"));
    }

    [Test]
    public void WatcherRenameBridge_rename後はchange_setをReadModel再計算へ渡す()
    {
        string source = GetRepoText("Watcher", "MainWindow.WatcherRenameBridge.cs");
        string renameMethod = GetMethodBlock(source, "private async Task RenameThumbAsync(");

        Assert.That(renameMethod, Does.Contain("await RefreshMovieViewAfterRenameAsync(snapshot.CurrentSort, snapshot.ChangedMovies);"));
        Assert.That(renameMethod, Does.Not.Contain("FilterAndSort("));
        Assert.That(renameMethod, Does.Not.Contain("Refresh();"));
        Assert.That(renameMethod, Does.Not.Contain("Items.Refresh()"));
    }

    [Test]
    public void WatchUiReloadPolicy_fullFallbackとqueryOnlyの実行入口を1か所に保つ()
    {
        string source = GetRepoText("Watcher", "MainWindow.WatchUiReloadPolicy.cs");
        string invokeMethod = GetMethodBlock(source, "private void InvokeWatchUiReload(");
        string adapterMethod = GetMethodBlock(source, "private void ApplyWatchUiApplyRequest(");
        string filterMethod = GetMethodBlock(source, "private void InvokeFilterAndSortForWatch(");

        Assert.That(invokeMethod, Does.Contain("WatchUiApplyRequest request = BuildWatchUiApplyRequest("));
        Assert.That(invokeMethod, Does.Contain("ApplyWatchUiApplyRequest(request);"));
        Assert.That(invokeMethod, Does.Not.Contain("InvokeFilterAndSortForWatch("));
        Assert.That(invokeMethod, Does.Not.Contain("RefreshMovieViewFromCurrentSourceAsync("));
        Assert.That(adapterMethod, Does.Contain("InvokeFilterAndSortForWatch(request.Sort, true);"));
        Assert.That(adapterMethod, Does.Contain("RefreshMovieViewFromCurrentSourceAsync("));
        Assert.That(filterMethod, Does.Contain("FilterAndSort(sort, isGetNew);"));
        Assert.That(source.Split("InvokeFilterAndSortForWatch(request.Sort, true);").Length - 1, Is.EqualTo(1));
        Assert.That(source.Split("RefreshMovieViewFromCurrentSourceAsync(").Length - 1, Is.EqualTo(1));
    }

    [Test]
    public void WatchUiReloadPolicy_changeSetをrequest化してからadapterで実行する()
    {
        string source = GetRepoText("Watcher", "MainWindow.WatchUiReloadPolicy.cs");
        string buildMethod = GetMethodBlock(
            source,
            "internal static WatchUiApplyRequest BuildWatchUiApplyRequest("
        );

        Assert.That(source, Does.Contain("internal readonly record struct WatchUiApplyRequest("));
        Assert.That(source, Does.Contain("internal enum WatchUiApplyRequestKind"));
        Assert.That(buildMethod, Does.Contain("WatchUiApplyRequestKind.InMemoryReadModelRefresh"));
        Assert.That(buildMethod, Does.Contain("WatchUiApplyRequestKind.FullFallbackReload"));
        Assert.That(buildMethod, Does.Contain("useQueryOnlyReload ? (changedMovies ?? []) : []"));
    }

    private static IEnumerable<string> EnumerateWatcherBoundaryCallLines(string needle)
    {
        return EnumerateWatcherBoundaryLines()
            .Where(x => x.TrimmedLine.Contains(needle, StringComparison.Ordinal))
            .Select(x => $"{x.RelativePath}|{x.TrimmedLine}");
    }

    private static IEnumerable<(string RelativePath, string TrimmedLine)> EnumerateWatcherBoundaryLines()
    {
        DirectoryInfo repoRoot = GetRepoRoot();
        foreach (string fileName in WatcherChangeSetBoundaryFiles)
        {
            string filePath = Path.Combine(repoRoot.FullName, "Watcher", fileName);
            Assert.That(File.Exists(filePath), Is.True, filePath);
            string relativePath = NormalizeRepoRelativePath(repoRoot, filePath);
            foreach (string line in File.ReadLines(filePath))
            {
                yield return (relativePath, line.Trim());
            }
        }
    }

    private static string GetRepoText(params string[] relativePathParts)
    {
        DirectoryInfo repoRoot = GetRepoRoot();
        string candidate = Path.Combine([repoRoot.FullName, .. relativePathParts]);
        Assert.That(File.Exists(candidate), Is.True, candidate);
        return File.ReadAllText(candidate);
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
                    "Watcher",
                    "MainWindow.WatchUiReloadPolicy.cs"
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
