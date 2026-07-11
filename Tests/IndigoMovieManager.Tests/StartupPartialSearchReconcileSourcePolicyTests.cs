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
            "QueuePartialSearchFullCompletion(sortId, searchRefreshRevision);",
            StringComparison.Ordinal
        );

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("if (!IsStartupFeedPartialActive)"));
            Assert.That(method, Does.Contain("\"search-partial-first\""));
            Assert.That(method, Does.Contain("UiHangActivityKind.Database"));
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
            Assert.That(reconcileMethod, Does.Contain("await FilterAndSortAsync(sortId, true,"));
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
    public void UserPriority中はfull整合を開始も適用もせず解除後に最新要求だけ1回再開する()
    {
        string source = GetSearchSource();
        string queueMethod = GetMethodBlock(
            source,
            "private void TryQueuePartialSearchFullCompletionAfterUserPriority("
        );
        string startMethod = GetMethodBlock(
            source,
            "private void StartPendingPartialSearchFullCompletion("
        );

        Assert.Multiple(() =>
        {
            // Player scroll を含む user-priority の間は、full DB 読取り自体を始めない。
            Assert.That(queueMethod, Does.Contain("IsUserPriorityWorkActive()"));
            Assert.That(startMethod, Does.Contain("IsUserPriorityWorkActive()"));
            Assert.That(source, Does.Contain("DeferPartialSearchFullCompletionForUserPriority"));
            Assert.That(source, Does.Contain("_partialSearchFullCompletionCancellation?.Cancel()"));

            // pending revision を上書きし、queued guard で解除後の起動を1回に畳む。
            Assert.That(source, Does.Contain("_pendingPartialSearchFullCompletionRevision = searchRefreshRevision"));
            Assert.That(source, Does.Contain("_partialSearchFullCompletionQueued"));
            Assert.That(
                queueMethod,
                Does.Contain("_partialSearchFullCompletionCancellation != null"),
                "active処理のfinallyだけがCTS解放後に再queueし、EndUserとの二重起動を防ぐ"
            );
            Assert.That(source, Does.Contain("TryQueuePartialSearchFullCompletionAfterUserPriority();"));
        });
    }

    [Test]
    public void Pendingがある操作後だけ1500msのquietWindowを適用する()
    {
        string source = GetSearchSource();
        string deferMethod = GetMethodBlock(
            source,
            "private void DeferPartialSearchFullCompletionForUserPriority("
        );
        int pendingGuard = deferMethod.IndexOf(
            "_pendingPartialSearchFullCompletionRevision == 0",
            StringComparison.Ordinal
        );
        int armQuietWindow = deferMethod.IndexOf(
            "_partialSearchFullCompletionQuietWindowArmed = true",
            StringComparison.Ordinal
        );

        Assert.Multiple(() =>
        {
            Assert.That(
                source,
                Does.Contain(
                    "PartialSearchFullCompletionQuietWindow =\n            TimeSpan.FromMilliseconds(1500)"
                )
            );
            Assert.That(pendingGuard, Is.GreaterThanOrEqualTo(0));
            Assert.That(armQuietWindow, Is.GreaterThan(pendingGuard));
            Assert.That(
                deferMethod.IndexOf("return;", pendingGuard, StringComparison.Ordinal),
                Is.LessThan(armQuietWindow),
                "pendingがない通常操作でquiet windowを起動しない"
            );
        });
    }

    [Test]
    public void 操作なしの初回fullはDelayを経由せずApplicationIdleへ送る()
    {
        string method = GetMethodBlock(
            GetSearchSource(),
            "private void TryQueuePartialSearchFullCompletionAfterUserPriority("
        );
        int quietBranch = method.IndexOf("if (waitForQuietWindow)", StringComparison.Ordinal);
        int quietWait = method.IndexOf(
            "WaitForPartialSearchFullCompletionQuietWindowAsync()",
            quietBranch,
            StringComparison.Ordinal
        );
        int applicationIdle = method.IndexOf(
            "DispatcherPriority.ApplicationIdle",
            quietWait,
            StringComparison.Ordinal
        );

        Assert.Multiple(() =>
        {
            Assert.That(quietBranch, Is.GreaterThanOrEqualTo(0));
            Assert.That(quietWait, Is.GreaterThan(quietBranch));
            Assert.That(applicationIdle, Is.GreaterThan(quietWait));
            Assert.That(method, Does.Not.Contain("Task.Delay("));
        });
    }

    [Test]
    public void ApplicationIdle待ち中に操作開始した要求はCTS生成前に単一quietDelayへ戻す()
    {
        string method = GetMethodBlock(
            GetSearchSource(),
            "private void StartPendingPartialSearchFullCompletion("
        );
        int clearQueued = method.IndexOf(
            "_partialSearchFullCompletionQueued = false",
            StringComparison.Ordinal
        );
        int userPriorityGuard = method.IndexOf(
            "IsUserPriorityWorkActive()",
            clearQueued,
            StringComparison.Ordinal
        );
        int quietArmedGuard = method.IndexOf(
            "_partialSearchFullCompletionQuietWindowArmed",
            userPriorityGuard,
            StringComparison.Ordinal
        );
        int quietRemainingGuard = method.IndexOf(
            "GetPartialSearchFullCompletionQuietWindowRemainingUnsafe()",
            quietArmedGuard,
            StringComparison.Ordinal
        );
        int releasePendingGuard = method.IndexOf(
            "_partialSearchFullCompletionReleasePending",
            quietArmedGuard,
            StringComparison.Ordinal
        );
        int requeue = method.IndexOf(
            "TryQueuePartialSearchFullCompletionAfterUserPriority();",
            quietRemainingGuard,
            StringComparison.Ordinal
        );
        int readPending = method.IndexOf(
            "sortId = _pendingPartialSearchFullCompletionSortId",
            requeue,
            StringComparison.Ordinal
        );
        int createCts = method.IndexOf(
            "_partialSearchFullCompletionCancellation = new CancellationTokenSource()",
            readPending,
            StringComparison.Ordinal
        );

        Assert.Multiple(() =>
        {
            Assert.That(clearQueued, Is.GreaterThanOrEqualTo(0));
            Assert.That(userPriorityGuard, Is.GreaterThan(clearQueued));
            Assert.That(quietArmedGuard, Is.GreaterThan(userPriorityGuard));
            Assert.That(releasePendingGuard, Is.GreaterThan(quietArmedGuard));
            Assert.That(releasePendingGuard, Is.LessThan(quietRemainingGuard));
            Assert.That(quietRemainingGuard, Is.GreaterThan(quietArmedGuard));
            Assert.That(requeue, Is.GreaterThan(quietRemainingGuard));
            Assert.That(readPending, Is.GreaterThan(requeue));
            Assert.That(createCts, Is.GreaterThan(readPending));
            Assert.That(
                method.IndexOf("return;", requeue, StringComparison.Ordinal),
                Is.LessThan(readPending),
                "quiet armedのApplicationIdle要求はactive CTSを作らずdelayへ合流する"
            );
        });
    }

    [Test]
    public void ApplicationIdle到達時にreleasePendingなら最終release基準1500msへ延長して再queueする()
    {
        string source = GetSearchSource();
        string startMethod = GetMethodBlock(
            source,
            "private void StartPendingPartialSearchFullCompletion("
        );
        string queueMethod = GetMethodBlock(
            source,
            "private void TryQueuePartialSearchFullCompletionAfterUserPriority("
        );
        int releasePendingGuard = startMethod.IndexOf(
            "_partialSearchFullCompletionReleasePending",
            StringComparison.Ordinal
        );
        int requeue = startMethod.IndexOf(
            "TryQueuePartialSearchFullCompletionAfterUserPriority();",
            releasePendingGuard,
            StringComparison.Ordinal
        );
        int createCts = startMethod.IndexOf(
            "_partialSearchFullCompletionCancellation = new CancellationTokenSource()",
            requeue,
            StringComparison.Ordinal
        );
        int queueReleasePendingGuard = queueMethod.IndexOf(
            "_partialSearchFullCompletionReleasePending",
            StringComparison.Ordinal
        );
        int extendFromRelease = queueMethod.IndexOf(
            "ExtendPartialSearchFullCompletionQuietWindowUnsafe();",
            queueReleasePendingGuard,
            StringComparison.Ordinal
        );
        int clearReleasePending = queueMethod.IndexOf(
            "_partialSearchFullCompletionReleasePending = false",
            extendFromRelease,
            StringComparison.Ordinal
        );

        Assert.Multiple(() =>
        {
            Assert.That(
                source,
                Does.Contain(
                    "PartialSearchFullCompletionQuietWindow =\n            TimeSpan.FromMilliseconds(1500)"
                )
            );
            Assert.That(releasePendingGuard, Is.GreaterThanOrEqualTo(0));
            Assert.That(requeue, Is.GreaterThan(releasePendingGuard));
            Assert.That(createCts, Is.GreaterThan(requeue));
            Assert.That(
                startMethod.IndexOf("return;", requeue, StringComparison.Ordinal),
                Is.LessThan(createCts),
                "begin基準の期限切れ後でもreleasePendingならfullを開始しない"
            );
            Assert.That(queueReleasePendingGuard, Is.GreaterThanOrEqualTo(0));
            Assert.That(extendFromRelease, Is.GreaterThan(queueReleasePendingGuard));
            Assert.That(clearReleasePending, Is.GreaterThan(extendFromRelease));
        });
    }

    [Test]
    public void 連続操作は期限を延長しDelayTaskはqueuedGuardで1本に畳む()
    {
        string source = GetSearchSource();
        string queueMethod = GetMethodBlock(
            source,
            "private void TryQueuePartialSearchFullCompletionAfterUserPriority("
        );
        string waitMethod = GetMethodBlock(
            source,
            "private async Task WaitForPartialSearchFullCompletionQuietWindowAsync("
        );
        int queuedGuard = queueMethod.IndexOf(
            "_partialSearchFullCompletionQueued",
            StringComparison.Ordinal
        );
        int markQueued = queueMethod.IndexOf(
            "_partialSearchFullCompletionQueued = true",
            queuedGuard + 1,
            StringComparison.Ordinal
        );
        int startDelayTask = queueMethod.IndexOf(
            "WaitForPartialSearchFullCompletionQuietWindowAsync()",
            markQueued,
            StringComparison.Ordinal
        );

        Assert.Multiple(() =>
        {
            Assert.That(queueMethod, Does.Contain("ExtendPartialSearchFullCompletionQuietWindowUnsafe()"));
            Assert.That(waitMethod, Does.Contain("while (true)"));
            Assert.That(waitMethod, Does.Contain("GetPartialSearchFullCompletionQuietWindowRemainingUnsafe()"));
            Assert.That(CountOccurrences(waitMethod, "Task.Delay("), Is.EqualTo(1));
            Assert.That(queuedGuard, Is.GreaterThanOrEqualTo(0));
            Assert.That(markQueued, Is.GreaterThan(queuedGuard));
            Assert.That(startDelayTask, Is.GreaterThan(markQueued));
        });
    }

    [Test]
    public void Activeなfull整合CTSがある間は再queueせずsingleFlightを守る()
    {
        string method = GetMethodBlock(
            GetSearchSource(),
            "private void TryQueuePartialSearchFullCompletionAfterUserPriority("
        );
        int lockStart = method.IndexOf(
            "lock (_partialSearchFullCompletionSync)",
            StringComparison.Ordinal
        );
        int activeCancellationGuard = method.IndexOf(
            "_partialSearchFullCompletionCancellation != null",
            lockStart,
            StringComparison.Ordinal
        );
        int markQueued = method.IndexOf(
            "_partialSearchFullCompletionQueued = true",
            StringComparison.Ordinal
        );

        Assert.Multiple(() =>
        {
            // Cancel要求後もDB読込が残るため、CTSの寿命中は次のfullを起動キューへ積まない。
            Assert.That(lockStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(activeCancellationGuard, Is.GreaterThan(lockStart));
            Assert.That(activeCancellationGuard, Is.LessThan(markQueued));
        });
    }

    [Test]
    public void Cancelされたfull整合はfinallyでCTSを閉じた後だけ1回再queueする()
    {
        string method = GetMethodBlock(
            GetSearchSource(),
            "private async Task CompletePartialSearchFromFullSourceAsync("
        );
        int finallyStart = method.IndexOf("finally", StringComparison.Ordinal);
        int dispose = method.IndexOf(
            "_partialSearchFullCompletionCancellation.Dispose()",
            finallyStart,
            StringComparison.Ordinal
        );
        int clearActiveCancellation = method.IndexOf(
            "_partialSearchFullCompletionCancellation = null",
            dispose,
            StringComparison.Ordinal
        );
        int canceledGuard = method.IndexOf(
            "if (cancellationToken.IsCancellationRequested)",
            clearActiveCancellation,
            StringComparison.Ordinal
        );
        int requeue = method.IndexOf(
            "TryQueuePartialSearchFullCompletionAfterUserPriority();",
            canceledGuard,
            StringComparison.Ordinal
        );

        Assert.Multiple(() =>
        {
            // active CTSを先に外し、その完了を境界として最新pendingだけを再開可能にする。
            Assert.That(finallyStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(dispose, Is.GreaterThan(finallyStart));
            Assert.That(clearActiveCancellation, Is.GreaterThan(dispose));
            Assert.That(canceledGuard, Is.GreaterThan(clearActiveCancellation));
            Assert.That(requeue, Is.GreaterThan(canceledGuard));
            Assert.That(
                CountOccurrences(
                    method[finallyStart..],
                    "TryQueuePartialSearchFullCompletionAfterUserPriority();"
                ),
                Is.EqualTo(1)
            );
        });
    }

    [Test]
    public void Partial整合lock内からUserPriority判定を呼ばずlock順逆転を防ぐ()
    {
        string source = GetSearchSource();
        IReadOnlyList<string> lockBlocks = GetBlocks(source, "lock (_partialSearchFullCompletionSync)");

        Assert.Multiple(() =>
        {
            Assert.That(lockBlocks, Is.Not.Empty);
            Assert.That(lockBlocks, Has.None.Contains("IsUserPriorityWorkActive()"));
        });
    }

    [Test]
    public void Full整合はDB切替shutdown検索revisionの古い結果を反映しない()
    {
        string method = GetMethodBlock(
            GetSearchSource(),
            "private async Task CompletePartialSearchFromFullSourceAsync("
        );

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("dbFullPath"));
            Assert.That(method, Does.Contain("AreSameMainDbPath("));
            Assert.That(method, Does.Contain("Dispatcher.HasShutdownStarted"));
            Assert.That(method, Does.Contain("Dispatcher.HasShutdownFinished"));
            Assert.That(
                method,
                Does.Contain("searchRefreshRevision != Volatile.Read(ref _searchRefreshRequestRevision)")
            );
            Assert.That(method, Does.Contain("cancellationToken.IsCancellationRequested"));
        });
    }

    [Test]
    public void ExternalCancellationは全件変換ループとReplaceMovieRecs直前まで届きキャンセルをfailed扱いしない()
    {
        string movieViewMethod = GetMethodBlock(
            GetMovieViewRequestsSource(),
            "private async Task FilterAndSortAsync("
        );
        string sourceApplyMethod = GetMethodBlock(
            GetMovieRecordFactorySource(),
            "private async Task<MovieRecordSourceApplyResult> SetRecordsToSource("
        );
        string reconcileMethod = GetMethodBlock(
            GetSearchSource(),
            "private async Task CompletePartialSearchFromFullSourceAsync("
        );
        int rowLoop = sourceApplyMethod.IndexOf("for (int index = 0; index < rowCount; index++)");
        int replace = sourceApplyMethod.IndexOf("MainVM.ReplaceMovieRecs(result.Items)");
        int sourceYield = sourceApplyMethod.IndexOf("Dispatcher.InvokeAsync(", rowLoop);
        int finalApplyYield = movieViewMethod.LastIndexOf("Dispatcher.InvokeAsync(", StringComparison.Ordinal);
        int finalApply = movieViewMethod.IndexOf(
            "TryApplyMovieViewReadModelResultOnUiThread(",
            finalApplyYield,
            StringComparison.Ordinal
        );

        Assert.Multiple(() =>
        {
            Assert.That(movieViewMethod, Does.Contain("externalCancellationToken"));
            Assert.That(movieViewMethod, Does.Contain("CreateLinkedTokenSource("));
            Assert.That(movieViewMethod, Does.Contain("SetRecordsToSource("));
            Assert.That(sourceApplyMethod, Does.Contain("CancellationToken"));
            Assert.That(rowLoop, Is.GreaterThanOrEqualTo(0));
            Assert.That(replace, Is.GreaterThan(rowLoop));
            Assert.That(
                sourceApplyMethod.IndexOf("ThrowIfCancellationRequested()", rowLoop, StringComparison.Ordinal),
                Is.GreaterThan(rowLoop)
            );
            Assert.That(
                sourceApplyMethod.LastIndexOf("ThrowIfCancellationRequested()", replace, StringComparison.Ordinal),
                Is.GreaterThan(rowLoop)
            );
            Assert.That(
                movieViewMethod,
                Does.Contain(
                    "deferUiApplyForExternalCancellation =\n                externalCancellationToken.CanBeCanceled"
                )
            );
            Assert.That(
                sourceApplyMethod,
                Does.Contain("bool deferUiApplyForExternalCancellation = false")
            );
            Assert.That(
                sourceApplyMethod,
                Does.Contain("deferUiApplyForExternalCancellation")
            );
            Assert.That(sourceApplyMethod, Does.Not.Contain("cancellationToken.CanBeCanceled"));
            Assert.That(sourceYield, Is.GreaterThan(rowLoop));
            Assert.That(sourceYield, Is.LessThan(replace));
            Assert.That(
                sourceApplyMethod.IndexOf("DispatcherPriority.Background", sourceYield, StringComparison.Ordinal),
                Is.LessThan(replace)
            );
            Assert.That(movieViewMethod, Does.Contain("externalCancellationToken.CanBeCanceled"));
            Assert.That(finalApplyYield, Is.GreaterThanOrEqualTo(0));
            Assert.That(finalApply, Is.GreaterThan(finalApplyYield));
            Assert.That(
                movieViewMethod.IndexOf(
                    "filterAndSortCancellationToken.IsCancellationRequested",
                    finalApplyYield,
                    StringComparison.Ordinal
                ),
                Is.LessThan(finalApply)
            );
            Assert.That(movieViewMethod, Does.Contain("catch (OperationCanceledException)"));
            Assert.That(reconcileMethod, Does.Contain("catch (OperationCanceledException)"));
            Assert.That(
                reconcileMethod.IndexOf("catch (OperationCanceledException)", StringComparison.Ordinal),
                Is.LessThan(reconcileMethod.IndexOf("catch (Exception ex)", StringComparison.Ordinal))
            );
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

    private static string GetMovieRecordFactorySource()
    {
        return GetRepoText("Views", "Main", "MainWindow.MovieRecordFactory.cs");
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

    private static IReadOnlyList<string> GetBlocks(string source, string signature)
    {
        List<string> blocks = [];
        int searchStart = 0;
        while ((searchStart = source.IndexOf(signature, searchStart, StringComparison.Ordinal)) >= 0)
        {
            string remaining = source[searchStart..];
            blocks.Add(GetMethodBlock(remaining, signature));
            searchStart += signature.Length;
        }

        return blocks;
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
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
