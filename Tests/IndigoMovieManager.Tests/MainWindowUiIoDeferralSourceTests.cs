using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MainWindowUiIoDeferralSourceTests
{
    [Test]
    public void DbSwitch後処理の旧Pending削除は背景タスクへ逃がす()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.DbSwitch.cs");

        Assert.That(source, Does.Contain("Task.Run("));
        Assert.That(
            source,
            Does.Contain("DiscardPreviousDbPendingThumbnailQueueItemsInBackground(")
        );
    }

    [Test]
    public void Startup軽サービスのサムネ成功索引プリウォームは背景で実行する()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.Startup.cs");

        Assert.That(source, Does.Contain("Task.Run(() => PrewarmThumbnailSuccessIndexCore("));
        Assert.That(source, Does.Contain("private void PrewarmThumbnailSuccessIndexCore("));
    }

    [Test]
    public void ContentRenderedではThumbnailProgressSnapshotを直接更新しない()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string contentRendered = ExtractMethod(source, "private void MainWindow_ContentRendered(");

        Assert.That(contentRendered, Does.Contain("EnsureThumbnailProgressUiTimerRunning();"));
        Assert.That(contentRendered, Does.Not.Contain("RequestThumbnailProgressSnapshotRefresh();"));
        Assert.That(contentRendered, Does.Not.Contain("UpdateThumbnailProgressSnapshotUi();"));
    }

    [Test]
    public void Startup軽サービスでThumbnailProgressSnapshot更新を予約する()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.Startup.cs");
        string deferredServices = ExtractMethod(
            source,
            "private async Task RunStartupDeferredServicesAsync(int revision)"
        );
        string queueMethod = ExtractMethod(
            source,
            "private void QueueStartupThumbnailProgressSnapshotRefresh()"
        );

        Assert.That(
            deferredServices,
            Does.Contain("QueueStartupThumbnailProgressSnapshotRefresh();")
        );
        Assert.That(queueMethod, Does.Contain("RequestThumbnailProgressSnapshotRefresh();"));
        Assert.That(queueMethod, Does.Not.Contain("UpdateThumbnailProgressSnapshotUi();"));
    }

    [Test]
    public void レイアウト復元は検証済みテキストを再利用し二重読込しない()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.xaml.cs");

        Assert.That(source, Does.Contain("using var reader = new StringReader(layoutText);"));
        Assert.That(source, Does.Not.Contain("using var reader = new StreamReader(layoutFilePath);"));
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
