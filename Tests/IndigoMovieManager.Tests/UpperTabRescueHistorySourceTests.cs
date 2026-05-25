using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UpperTabRescueHistorySourceTests
{
    [Test]
    public void Rescue履歴の選択変更はFailureDb読み取りを背景へ逃がす()
    {
        string source = GetRepoText(
            "UpperTabs",
            "Rescue",
            "MainWindow.UpperTabs.RescueTab.History.cs"
        );
        string refreshMethod = GetMethodBlock(
            source,
            "private void RefreshUpperTabRescueHistoryPanel("
        );
        string refreshAsyncMethod = GetMethodBlock(
            source,
            "private async Task RefreshUpperTabRescueHistoryPanelAsync("
        );
        string loadMethod = GetMethodBlock(
            source,
            "private UpperTabRescueHistoryItemViewModel[] LoadUpperTabRescueHistoryItems("
        );

        Assert.That(refreshMethod, Does.Contain("_ = RefreshUpperTabRescueHistoryPanelAsync("));
        Assert.That(refreshAsyncMethod, Does.Contain("Task.Run("));
        Assert.That(refreshAsyncMethod, Does.Contain("Dispatcher"));
        Assert.That(refreshAsyncMethod, Does.Contain(".InvokeAsync("));
        Assert.That(source, Does.Not.Contain(".ContinueWith("));
        Assert.That(source, Does.Not.Contain("task.Result"));
        Assert.That(refreshMethod, Does.Contain("_upperTabRescueHistoryRefreshStamp"));
        Assert.That(refreshAsyncMethod, Does.Contain("_upperTabRescueHistoryRefreshStamp"));
        Assert.That(refreshMethod, Does.Not.Contain(".GetFailureRecords(limit: 400)"));
        Assert.That(refreshAsyncMethod, Does.Not.Contain(".GetFailureRecords(limit: 400)"));
        Assert.That(loadMethod, Does.Contain(".GetFailureRecords(limit: 400)"));
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

        Assert.Fail($"{signature} の終端を解決できませんでした。");
        return string.Empty;
    }
}
