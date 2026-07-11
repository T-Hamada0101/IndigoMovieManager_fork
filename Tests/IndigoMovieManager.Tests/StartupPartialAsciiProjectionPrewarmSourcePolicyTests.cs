using System.Runtime.CompilerServices;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class StartupPartialAsciiProjectionPrewarmSourcePolicyTests
{
    [Test]
    public void 予約はpartial時の現在MovieRecsだけを最大200件snapshot化する()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.StartupAsciiSearchPrewarm.cs");
        string method = GetMethodBlock(source, "private void QueueStartupAsciiSearchPrewarm(");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("StartupAsciiSearchPrewarmLimit = 200"));
            Assert.That(method, Does.Contain("IsStartupFeedPartialActive"));
            Assert.That(method, Does.Contain("MainVM.MovieRecs"));
            Assert.That(method, Does.Contain("StartupAsciiSearchPrewarmLimit"));
            Assert.That(method, Does.Contain("snapshot"));
            Assert.That(method, Does.Not.Contain("FilteredMovieRecs"));
            Assert.That(method, Does.Not.Contain("LoadMovie"));
            Assert.That(method, Does.Not.Contain("DataTable"));
            Assert.That(method, Does.Not.Contain("SQLite"));
        });
    }

    [Test]
    public void 実行はASCII軽量投影だけを使い日本語fallbackと全件走査へ進まない()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.StartupAsciiSearchPrewarm.cs");
        string method = GetMethodBlock(source, "private void RunStartupAsciiSearchPrewarm(");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("GetAsciiSearchFieldsForFilter("));
            Assert.That(method, Does.Contain("allowExpensivePhoneticFallback: false"));
            Assert.That(method, Does.Not.Contain("GetSearchFieldsForFilter("));
            Assert.That(method, Does.Not.Contain("BuildSearchFields("));
            Assert.That(method, Does.Not.Contain("BuildAsciiSearchFields("));
            Assert.That(method, Does.Not.Contain("MainVM.MovieRecs"));
            Assert.That(method, Does.Not.Contain("FilteredMovieRecs"));
            Assert.That(method, Does.Not.Contain("LoadMovie"));
        });
    }

    [Test]
    public void WorkerはlatestOnly単一taskで小batchごとに操作とstaleを中断する()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.StartupAsciiSearchPrewarm.cs");
        string queueMethod = GetMethodBlock(source, "private void QueueStartupAsciiSearchPrewarm(");
        string runMethod = GetMethodBlock(source, "private void RunStartupAsciiSearchPrewarm(");
        string guardMethod = GetMethodBlock(
            source,
            "private string ResolveStartupAsciiSearchPrewarmInterruption("
        );

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("_startupAsciiSearchPrewarmPending"));
            Assert.That(source, Does.Contain("_startupAsciiSearchPrewarmTask"));
            Assert.That(queueMethod, Does.Contain("IsCompleted: false"));
            Assert.That(queueMethod, Does.Contain("Interlocked.Increment"));
            Assert.That(runMethod, Does.Contain("StartupAsciiSearchPrewarmBatchSize"));
            Assert.That(runMethod, Does.Contain("ResolveStartupAsciiSearchPrewarmInterruption"));
            Assert.That(guardMethod, Does.Contain("Volatile.Read"));
            Assert.That(guardMethod, Does.Contain("_startupLoadCoordinator.IsCurrent"));
            Assert.That(guardMethod, Does.Contain("AreSameMainDbPath"));
            Assert.That(guardMethod, Does.Contain("Dispatcher.HasShutdownStarted"));
            Assert.That(guardMethod, Does.Contain("Dispatcher.HasShutdownFinished"));
            Assert.That(guardMethod, Does.Contain("IsUserPriorityWorkActive()"));
            Assert.That(source, Does.Not.Contain("Task.WhenAll"));
            Assert.That(source, Does.Not.Contain("Parallel."));
        });
    }

    [Test]
    public void 通常検索仕様とASCIIキャッシュ無効化を維持する()
    {
        string searchSource = GetRepoText("Views", "Main", "MainWindow.Search.cs");
        string movieRecordsSource = GetRepoText("Models", null, "MovieRecords.cs");
        string invalidateMethod = GetMethodBlock(
            movieRecordsSource,
            "private void InvalidateSearchFieldCache("
        );

        Assert.Multiple(() =>
        {
            Assert.That(searchSource, Does.Contain("await FilterAndSortAsync(sortId, false);"));
            Assert.That(searchSource, Does.Contain("await FilterAndSortAsync(sortId, true);"));
            Assert.That(invalidateMethod, Does.Contain("searchFieldCache = null;"));
            Assert.That(invalidateMethod, Does.Contain("asciiSearchFieldCache = null;"));
            Assert.That(invalidateMethod, Does.Contain("asciiFastSearchFieldCache = null;"));
        });
    }

    private static string GetRepoText(
        string firstPathPart,
        string? secondPathPart,
        string fileName,
        [CallerFilePath] string callerFilePath = ""
    )
    {
        DirectoryInfo repoRoot = FindRepoRoot(callerFilePath);
        string candidate = secondPathPart == null
            ? Path.Combine(repoRoot.FullName, firstPathPart, fileName)
            : Path.Combine(repoRoot.FullName, firstPathPart, secondPathPart, fileName);
        Assert.That(File.Exists(candidate), Is.True, $"{candidate} が見つかりません。");
        return File.ReadAllText(candidate);
    }

    private static DirectoryInfo FindRepoRoot(string callerFilePath)
    {
        DirectoryInfo? current = new(Path.GetDirectoryName(callerFilePath) ?? "");
        while (current != null && !File.Exists(Path.Combine(current.FullName, "IndigoMovieManager.sln")))
        {
            current = current.Parent;
        }

        Assert.That(current, Is.Not.Null, "リポジトリルートが見つかりません。");
        return current!;
    }

    private static string GetMethodBlock(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), signature);
        int openingBrace = source.IndexOf('{', start);
        Assert.That(openingBrace, Is.GreaterThan(start), signature);

        int depth = 0;
        for (int index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source[start..(index + 1)];
            }
        }

        throw new AssertionException($"メソッド終端が見つかりません: {signature}");
    }
}
