using System.IO;
using System.Runtime.CompilerServices;

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
    public void 詳細タグ更新は表示中のViewLocal更新として分類する()
    {
        string extDetailSource = ReadRepoText("UserControls", "ExtDetail.xaml.cs");
        string extensionTabSource = ReadRepoText(
            "BottomTabs",
            "Extension",
            "ExtensionTabView.xaml.cs"
        );

        Assert.Multiple(() =>
        {
            Assert.That(extDetailSource, Does.Contain("CollectionViewSource.GetDefaultView"));
            Assert.That(extDetailSource, Does.Contain("表示中の軽い view-local 更新"));
            Assert.That(extDetailSource, Does.Not.Contain("ExtDetailTags.Items.Refresh()"));
            Assert.That(extensionTabSource, Does.Contain("Visibility != Visibility.Visible"));
            Assert.That(
                extensionTabSource.IndexOf("Visibility != Visibility.Visible", StringComparison.Ordinal),
                Is.LessThan(
                    extensionTabSource.IndexOf("ExtensionDetailView.Refresh();", StringComparison.Ordinal)
                )
            );
        });
    }

    [Test]
    public void かな補完はFilterAndSortTrueではなくMemoryRefreshへ寄せる()
    {
        string source = ReadRepoText("Views", "Main", "MainWindow.KanaBackfill.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("QueueKanaBackfillMovieViewRefresh("));
            Assert.That(
                source,
                Does.Contain("UiWorkRequestPolicy.CreateKanaBackfillMovieViewRefreshRequest()")
            );
            Assert.That(source, Does.Contain("TryAdmitKanaBackfillMovieViewRefresh("));
            Assert.That(source, Does.Contain("lock (_uiWorkSchedulerRuntimeSyncRoot)"));
            Assert.That(source, Does.Contain("_uiWorkSchedulerRuntime.Queue(request)"));
            Assert.That(source, Does.Contain("_uiWorkSchedulerRuntime.TryTakeNext()"));
            Assert.That(source, Does.Contain("kana backfill scheduler rejected:"));
            Assert.That(source, Does.Contain("kana backfill scheduler empty:"));
            Assert.That(source, Does.Contain("kana backfill scheduler admitted:"));
            Assert.That(source, Does.Contain("kana backfill scheduler released:"));
            Assert.That(source, Does.Contain("UiWorkSchedulerPolicy.BuildAdmissionLogFields("));
            Assert.That(source, Does.Contain("UiWorkSchedulerPolicy.BuildTakeLogFields("));
            Assert.That(source, Does.Contain("UiWorkRequestPolicy.BuildRequestAdmissionLogFields("));
            Assert.That(source, Does.Contain("UiWorkRequestPolicy.ReleaseReasonReleased"));
            Assert.That(source, Does.Contain("pending_count="));
            Assert.That(source, Does.Contain("RefreshMovieViewFromCurrentSourceAsync("));
            Assert.That(source, Does.Contain("kana backfill local refresh fallback:"));
            Assert.That(source, Does.Not.Contain("FilterAndSort(MainVM.DbInfo.Sort, true)"));
        });
    }

    private static string ReadRepoText(
        string firstPart,
        string secondPart = "",
        string thirdPart = "",
        [CallerFilePath] string callerFilePath = ""
    )
    {
        DirectoryInfo? directory = FindRepoRoot(new DirectoryInfo(TestContext.CurrentContext.TestDirectory));
        directory ??= FindRepoRoot(new DirectoryInfo(Directory.GetCurrentDirectory()));
        if (!string.IsNullOrWhiteSpace(callerFilePath))
        {
            directory ??= FindRepoRoot(new FileInfo(callerFilePath).Directory);
        }

        Assert.That(directory, Is.Not.Null, "repo root が見つかりません。");
        string[] parts = string.IsNullOrEmpty(thirdPart)
            ? string.IsNullOrEmpty(secondPart)
                ? [firstPart]
                : [firstPart, secondPart]
            : [firstPart, secondPart, thirdPart];
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }

    private static DirectoryInfo? FindRepoRoot(DirectoryInfo? directory)
    {
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "IndigoMovieManager.sln")))
        {
            directory = directory.Parent;
        }

        return directory;
    }
}
