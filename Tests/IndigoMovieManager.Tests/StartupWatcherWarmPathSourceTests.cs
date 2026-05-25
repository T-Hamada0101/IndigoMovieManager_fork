using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class StartupWatcherWarmPathSourceTests
{
    [Test]
    public void LightServicesではWatcher起動を直接呼ばない()
    {
        string source = File.ReadAllText(GetMainWindowStartupSourcePath());

        Assert.That(source, Does.Not.Contain("QueueStartupWatcherCreation(revision);"));
    }

    [Test]
    public void HeavyServices開始時にWatcher起動を後ろ倒しで呼ぶ()
    {
        string source = File.ReadAllText(GetMainWindowStartupSourcePath());

        Assert.That(source, Does.Contain("QueueStartupWatcherCreation(_startupSessionRevision);"));
    }

    [Test]
    public void Watcher起動はStartupFeedキャンセル後もHeavyServicesから実行できる()
    {
        string source = File.ReadAllText(GetMainWindowStartupSourcePath());
        string queueMethod = ExtractMethod(source, "private void QueueStartupWatcherCreation(int revision)");

        Assert.That(queueMethod, Does.Contain("if (revision > 0 && !_startupLoadCoordinator.IsCurrent(revision))"));
        Assert.That(queueMethod, Does.Contain("if (!_startupLightServicesStarted && !_startupHeavyServicesStarted)"));
        Assert.That(queueMethod, Does.Contain("CreateWatcher();"));
    }

    [Test]
    public void WarmPath三段ログは同じRevisionTriggerElapsedで追える()
    {
        string source = File.ReadAllText(GetMainWindowStartupSourcePath());
        string firstPageMethod = ExtractMethod(source, "private void ApplyStartupFirstPage(");
        string heavyMethod = ExtractMethod(
            source,
            "private void StartStartupHeavyServicesIfNeeded("
        );

        Assert.That(source, Does.Contain("startup warm path {milestone}:"));
        Assert.That(source, Does.Contain("\"first-page shown\""));
        Assert.That(source, Does.Contain("\"input ready\""));
        Assert.That(source, Does.Contain("\"heavy services started\""));

        Assert.That(firstPageMethod, Does.Contain("ResolveStartupWarmPathTrigger()"));
        Assert.That(firstPageMethod, Does.Contain("ResolveStartupFeedState(page.HasMore)"));
        Assert.That(heavyMethod, Does.Contain("ResolveStartupWarmPathRevision(revisionForLog)"));
        Assert.That(heavyMethod, Does.Contain("ResolveStartupWarmPathTrigger()"));
        Assert.That(heavyMethod, Does.Contain("start_reason={startReason}"));
        Assert.That(
            source,
            Does.Contain(
                "revision={revision} trigger={trigger} source={sourceKind} feed_state={feedState}"
            )
        );
        Assert.That(source, Does.Contain("elapsed_ms={_startupUiStopwatch.ElapsedMilliseconds}"));
    }

    [Test]
    public void Startup分岐ログはPartialFallbackCompleteを区別できる()
    {
        string source = File.ReadAllText(GetMainWindowStartupSourcePath());
        string fallbackMethod = ExtractMethod(
            source,
            "private void FallbackToLegacyStartupLoad(string sortId, int revision)"
        );
        string completeMethod = ExtractMethod(
            source,
            "private void FinishStartupFeedIfCurrent(int revision)"
        );
        string parkedMethod = ExtractMethod(
            source,
            "private void RememberStartupContinuationState("
        );

        Assert.That(parkedMethod, Does.Contain("feed_state=partial-feed"));
        Assert.That(fallbackMethod, Does.Contain("StartupSourceLegacyFallback"));
        Assert.That(fallbackMethod, Does.Contain("\"fallback begin\""));
        Assert.That(fallbackMethod, Does.Contain("\"fallback complete\""));
        Assert.That(completeMethod, Does.Contain("\"StartupFeedComplete\""));
        Assert.That(completeMethod, Does.Contain("\"complete\""));
    }

    private static string GetMainWindowStartupSourcePath()
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, "Views", "Main", "MainWindow.Startup.cs");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        Assert.Fail("MainWindow.Startup.cs の位置を repo root から解決できませんでした。");
        return string.Empty;
    }

    private static string ExtractMethod(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{signature} が見つかりません。");

        int braceStart = source.IndexOf('{', start);
        Assert.That(braceStart, Is.GreaterThanOrEqualTo(0));

        int depth = 0;
        for (int i = braceStart; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(start, i - start + 1);
                }
            }
        }

        Assert.Fail($"{signature} の終端を解決できませんでした。");
        return string.Empty;
    }
}
