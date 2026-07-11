using NUnit.Framework;

namespace IndigoMovieManager.Tests;

public sealed class PlayerThumbnailScrollSourceTests
{
    [Test]
    public void Player右レールは行単位スクロールと半ページcacheで実現数を抑える()
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
        Assert.That(playerThumbnailList, Does.Not.Contain("VirtualizingPanel.ScrollUnit=\"Pixel\""));
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
