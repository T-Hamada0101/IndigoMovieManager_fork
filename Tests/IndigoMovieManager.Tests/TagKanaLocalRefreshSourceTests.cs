using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class TagKanaLocalRefreshSourceTests
{
    [Test]
    public void タグ操作はRefreshではなく局所反映入口へ寄せる()
    {
        string source = ReadRepoText("Views", "Main", "MainWindow.Tag.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("RefreshViewsAfterTagRecordsChanged(mv, \"paste\")"));
            Assert.That(source, Does.Contain("RefreshViewsAfterTagRecordsChanged(mv, \"delete\")"));
            Assert.That(source, Does.Contain("RefreshViewsAfterTagRecordsChanged(mv, \"edit\")"));
            Assert.That(source, Does.Contain("RefreshViewsAfterTagRecordsChanged(records, \"add\")"));
            Assert.That(source, Does.Contain("RefreshViewsAfterTagRecordsChanged(records, \"remove\")"));
            Assert.That(source, Does.Contain("tag local refresh fallback:"));
            Assert.That(source, Does.Not.Match(@"(?m)^\s*Refresh\(\);"));
        });
    }

    [Test]
    public void タグチップ削除はItemsRefreshへ戻らない()
    {
        string source = ReadRepoText("UserControls", "TagControl.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("RefreshViewsAfterTagRecordsChanged(mv, \"chip-remove\")"));
            Assert.That(source, Does.Not.Contain("Items.Refresh()"));
        });
    }

    [Test]
    public void かな補完はFilterAndSortTrueではなくMemoryRefreshへ寄せる()
    {
        string source = ReadRepoText("Views", "Main", "MainWindow.KanaBackfill.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("QueueKanaBackfillMovieViewRefresh("));
            Assert.That(source, Does.Contain("RefreshMovieViewFromCurrentSourceAsync("));
            Assert.That(source, Does.Contain("kana backfill local refresh fallback:"));
            Assert.That(source, Does.Not.Contain("FilterAndSort(MainVM.DbInfo.Sort, true)"));
        });
    }

    private static string ReadRepoText(params string[] parts)
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "IndigoMovieManager.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "repo root が見つかりません。");
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
