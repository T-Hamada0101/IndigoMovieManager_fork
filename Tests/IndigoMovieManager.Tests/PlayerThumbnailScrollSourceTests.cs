using NUnit.Framework;

namespace IndigoMovieManager.Tests;

public sealed class PlayerThumbnailScrollSourceTests
{
    [Test]
    public void Player右レールは標準縦リストと半ページcacheで再生成を抑える()
    {
        string mainWindowXaml = GetRepoText("Views", "Main", "MainWindow.xaml");
        int listStart = mainWindowXaml.IndexOf(
            "x:Name=\"PlayerThumbnailList\"",
            StringComparison.Ordinal
        );
        Assert.That(listStart, Is.GreaterThanOrEqualTo(0));

        int listEnd = mainWindowXaml.IndexOf("</ListView>", listStart, StringComparison.Ordinal);
        Assert.That(listEnd, Is.GreaterThan(listStart));
        string playerThumbnailList = mainWindowXaml.Substring(listStart, listEnd - listStart);

        Assert.That(playerThumbnailList, Does.Contain("VirtualizingPanel.ScrollUnit=\"Item\""));
        Assert.That(playerThumbnailList, Does.Contain("VirtualizingPanel.CacheLength=\"0.5\""));
        Assert.That(playerThumbnailList, Does.Contain("VirtualizingPanel.CacheLengthUnit=\"Page\""));
        Assert.That(playerThumbnailList, Does.Contain("<VirtualizingStackPanel"));
        Assert.That(playerThumbnailList, Does.Contain("Orientation=\"Vertical\""));
        Assert.That(playerThumbnailList, Does.Contain("<Setter Property=\"Height\" Value=\"56\" />"));
        Assert.That(playerThumbnailList, Does.Contain("TextTrimming=\"CharacterEllipsis\""));
        Assert.That(playerThumbnailList, Does.Contain("SelectionChanged=\"PlayerThumbnailList_SelectionChanged\""));
        Assert.That(playerThumbnailList, Does.Contain("ContextMenu=\"{StaticResource menuContext}\""));
        Assert.That(playerThumbnailList, Does.Contain("MouseDown=\"Label_MouseDown\""));
        Assert.That(playerThumbnailList, Does.Not.Contain("<vwp:VirtualizingWrapPanel"));
        Assert.That(playerThumbnailList, Does.Not.Contain("VirtualizingPanel.ScrollUnit=\"Pixel\""));
    }

    [Test]
    public void Player右レールのクリック処理はBorderと既存Labelの両senderを受け入れる()
    {
        string selectionSource = GetRepoText("Views", "Main", "MainWindow.Selection.cs");

        Assert.That(
            selectionSource,
            Does.Contain(
                "sender is FrameworkElement clickedElement && clickedElement.DataContext is MovieRecords record"
            )
        );
        Assert.That(
            selectionSource,
            Does.Contain("SelectPlayerThumbnailRecordWithoutScroll(clickedElement, record)")
        );
        Assert.That(selectionSource, Does.Not.Contain("sender is Label label"));
        Assert.That(selectionSource, Does.Not.Contain("label.Content"));
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

        Assert.Fail($"{Path.Combine(relativePathParts)} の位置をrepo rootから解決できませんでした。");
        return string.Empty;
    }
}
