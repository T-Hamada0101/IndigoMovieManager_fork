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
}
