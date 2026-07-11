using System.Runtime.CompilerServices;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class StartupPartialSearchReconcileSourcePolicyTests
{
    [Test]
    public void Partial中はメモリ上の検索結果をawaitしてからfull再取得を後続起動する()
    {
        string method = GetMethodBlock(
            GetSearchSource(),
            "private async Task RefreshSearchResultsAsync("
        );
        int partialRefresh = method.IndexOf(
            "await RefreshMovieViewFromCurrentSourceAsync(",
            StringComparison.Ordinal
        );
        int staleGuard = method.IndexOf(
            "searchRefreshRevision != Volatile.Read(ref _searchRefreshRequestRevision)",
            StringComparison.Ordinal
        );
        int fullReconcile = method.IndexOf(
            "_ = CompletePartialSearchFromFullSourceAsync(sortId, searchRefreshRevision);",
            StringComparison.Ordinal
        );

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("if (!IsStartupFeedPartialActive)"));
            Assert.That(method, Does.Contain("\"search-partial-first\""));
            Assert.That(method, Does.Contain("UiHangActivityKind.Database"));
            Assert.That(method, Does.Contain("forceBackgroundCompute: true"));
            Assert.That(partialRefresh, Is.GreaterThanOrEqualTo(0));
            Assert.That(staleGuard, Is.GreaterThan(partialRefresh));
            Assert.That(fullReconcile, Is.GreaterThan(staleGuard));
            Assert.That(
                method,
                Does.Not.Contain("await CompletePartialSearchFromFullSourceAsync("),
                "partial結果を返す入口でfull reload完了を待たない"
            );
        });
    }

    [Test]
    public void PartialFirstだけbackground計算を強制し通常経路は既存件数判定を使う()
    {
        string searchMethod = GetMethodBlock(
            GetSearchSource(),
            "private async Task RefreshSearchResultsAsync("
        );
        string refreshMethod = GetMethodBlock(
            GetMovieViewRequestsSource(),
            "private async Task RefreshMovieViewFromCurrentSourceAsync("
        );

        Assert.Multiple(() =>
        {
            Assert.That(refreshMethod, Does.Contain("bool forceBackgroundCompute = false"));
            Assert.That(
                refreshMethod,
                Does.Contain(
                    "forceBackgroundCompute\n                || MainWindow.ShouldRunFilterSortOnBackground(sourceMovies.Length)"
                )
            );
            Assert.That(refreshMethod, Does.Contain("readModelResult = runOnBackground"));
            Assert.That(refreshMethod, Does.Contain("background={runOnBackground}"));
            Assert.That(searchMethod, Does.Contain("forceBackgroundCompute: true"));
            Assert.That(
                searchMethod.Split("forceBackgroundCompute: true").Length - 1,
                Is.EqualTo(1)
            );
        });
    }

    [Test]
    public void 通常時は従来どおりqueryOnlyでDBを再取得しない()
    {
        string method = GetMethodBlock(
            GetSearchSource(),
            "private async Task RefreshSearchResultsAsync("
        );
        int normalBranch = method.IndexOf(
            "if (!IsStartupFeedPartialActive)",
            StringComparison.Ordinal
        );
        int queryOnly = method.IndexOf(
            "await FilterAndSortAsync(sortId, false);",
            normalBranch,
            StringComparison.Ordinal
        );
        int returnAfterQuery = method.IndexOf("return;", queryOnly, StringComparison.Ordinal);
        int partialRefresh = method.IndexOf(
            "await RefreshMovieViewFromCurrentSourceAsync(",
            StringComparison.Ordinal
        );

        Assert.Multiple(() =>
        {
            Assert.That(normalBranch, Is.GreaterThanOrEqualTo(0));
            Assert.That(queryOnly, Is.GreaterThan(normalBranch));
            Assert.That(returnAfterQuery, Is.GreaterThan(queryOnly));
            Assert.That(partialRefresh, Is.GreaterThan(returnAfterQuery));
        });
    }

    [Test]
    public void 新検索revisionと一覧要求cancellationで旧full結果を反映しない()
    {
        string searchSource = GetSearchSource();
        string refreshMethod = GetMethodBlock(
            searchSource,
            "private async Task RefreshSearchResultsAsync("
        );
        string reconcileMethod = GetMethodBlock(
            searchSource,
            "private async Task CompletePartialSearchFromFullSourceAsync("
        );
        string movieViewSource = GetMovieViewRequestsSource();
        string fullReloadMethod = GetMethodBlock(
            movieViewSource,
            "private async Task FilterAndSortAsync("
        );

        Assert.Multiple(() =>
        {
            Assert.That(searchSource, Does.Contain("private int _searchRefreshRequestRevision;"));
            Assert.That(
                refreshMethod,
                Does.Contain("Interlocked.Increment(ref _searchRefreshRequestRevision)")
            );
            Assert.That(
                reconcileMethod,
                Does.Contain("Volatile.Read(ref _searchRefreshRequestRevision)")
            );
            Assert.That(reconcileMethod, Does.Contain("await FilterAndSortAsync(sortId, true);"));
            Assert.That(fullReloadMethod, Does.Contain("BeginFilterAndSortCancellation()"));
            Assert.That(fullReloadMethod, Does.Contain("filterAndSortCancellationToken"));
            Assert.That(
                fullReloadMethod,
                Does.Contain("requestRevision != _filterAndSortRequestRevision")
            );
        });
    }

    [Test]
    public void 後続fullの例外は必ず観測してログへ閉じる()
    {
        string method = GetMethodBlock(
            GetSearchSource(),
            "private async Task CompletePartialSearchFromFullSourceAsync("
        );

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("try"));
            Assert.That(method, Does.Contain("catch (Exception ex)"));
            Assert.That(method, Does.Contain("DebugRuntimeLog.Write("));
            Assert.That(method, Does.Contain("search partial full reload failed"));
            Assert.That(method, Does.Contain("error_type={ex.GetType().Name}"));
        });
    }

    [Test]
    public void PartialFirst後も検索正本と履歴と選択保持を変えずDB書込を混ぜない()
    {
        string source = GetSearchSource();
        string refreshMethod = GetMethodBlock(
            source,
            "private async Task RefreshSearchResultsAsync("
        );
        string reconcileMethod = GetMethodBlock(
            source,
            "private async Task CompletePartialSearchFromFullSourceAsync("
        );
        string partialFlow = refreshMethod + Environment.NewLine + reconcileMethod;

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("setSearchKeyword: keyword => MainVM.DbInfo.SearchKeyword = keyword"));
            Assert.That(source, Does.Contain("refreshSearchResultsAsync: RefreshSearchResultsAsync"));
            Assert.That(source, Does.Contain("selectFirstItem: SelectFirstSearchResultIfNeeded"));
            Assert.That(source, Does.Contain("PersistSearchHistoryAfterSearch"));
            Assert.That(partialFlow, Does.Not.Contain("PersistSearchHistory"));
            Assert.That(partialFlow, Does.Not.Contain("SearchHistoryService"));
            Assert.That(partialFlow, Does.Not.Contain("ExecuteNonQuery"));
            Assert.That(partialFlow, Does.Not.Contain("UPDATE "));
            Assert.That(partialFlow, Does.Not.Contain("INSERT "));
            Assert.That(partialFlow, Does.Not.Contain("DELETE "));
            Assert.That(partialFlow, Does.Not.Contain("SelectFirstItem"));
        });
    }

    private static string GetSearchSource()
    {
        return GetRepoText("Views", "Main", "MainWindow.Search.cs");
    }

    private static string GetMovieViewRequestsSource()
    {
        return GetRepoText("Views", "Main", "MainWindow.MovieViewRequests.cs");
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

    private static string GetRepoText(
        string firstPathPart,
        string secondPathPart,
        string fileName,
        [CallerFilePath] string callerFilePath = ""
    )
    {
        DirectoryInfo? current = new(Path.GetDirectoryName(callerFilePath) ?? "");
        while (current != null)
        {
            string candidate = Path.Combine(
                current.FullName,
                firstPathPart,
                secondPathPart,
                fileName
            );
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        Assert.Fail($"{Path.Combine(firstPathPart, secondPathPart, fileName)} が見つかりません。");
        return "";
    }
}
