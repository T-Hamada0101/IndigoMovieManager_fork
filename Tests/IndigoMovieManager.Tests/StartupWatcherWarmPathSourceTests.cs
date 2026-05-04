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
