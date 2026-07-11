using System.IO;
using System.Runtime.CompilerServices;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class PlayerVirtualizationSourcePolicyTests
{
    [Test]
    public void Player右レール_縦リストの仮想化契約を維持する()
    {
        string playerTab = GetTabBlock("TabPlayer", "TabList");
        string playerList = GetElementBlock(playerTab, "<ListView", "x:Name=\"PlayerThumbnailList\"", "</ListView>");

        // 先読み0は再生成を増やしたため、半ページと固定高Recyclingの組み合わせを守る。
        Assert.Multiple(() =>
        {
            Assert.That(playerList, Does.Contain("VirtualizingPanel.CacheLength=\"0.5\""));
            Assert.That(playerList, Does.Contain("VirtualizingPanel.IsVirtualizing=\"True\""));
            Assert.That(playerList, Does.Contain("VirtualizingPanel.ScrollUnit=\"Item\""));
            Assert.That(playerList, Does.Contain("VirtualizingPanel.VirtualizationMode=\"Recycling\""));
            Assert.That(playerList, Does.Contain("<VirtualizingStackPanel"));
            Assert.That(playerList, Does.Contain("Orientation=\"Vertical\""));
            Assert.That(playerList, Does.Contain("<Setter Property=\"Height\" Value=\"56\" />"));
        });
    }

    [Test]
    public void Player右レール_旧WrapPanelとPixelスクロールへ戻さない()
    {
        string playerTab = GetTabBlock("TabPlayer", "TabList");
        string playerList = GetElementBlock(playerTab, "<ListView", "x:Name=\"PlayerThumbnailList\"", "</ListView>");

        Assert.Multiple(() =>
        {
            Assert.That(playerList, Does.Not.Contain("VirtualizingWrapPanel"));
            Assert.That(playerList, Does.Not.Contain("<WrapPanel"));
            Assert.That(playerList, Does.Not.Contain("VirtualizingPanel.ScrollUnit=\"Pixel\""));
        });
    }

    [TestCase("TabSmall", "TabBig")]
    [TestCase("TabBig", "TabGrid")]
    [TestCase("TabGrid", "TabPlayer")]
    [TestCase("TabList", "TabBig10")]
    [TestCase("TabBig10", "")]
    public void 通常タブ_Player用cache契約を持ち込まない(string tabName, string nextTabName)
    {
        // Playerの局所調整が通常5タブの既存cache方針へ波及しない境界を守る。
        string tab = GetTabBlock(tabName, nextTabName);

        Assert.Multiple(() =>
        {
            Assert.That(tab, Does.Not.Contain("VirtualizingPanel.CacheLength="));
            Assert.That(tab, Does.Not.Contain("VirtualizingPanel.CacheLengthUnit="));
        });
    }

    private static string GetTabBlock(string tabName, string nextTabName)
    {
        string xaml = GetMainWindowXaml();
        int start = xaml.IndexOf($"<TabItem x:Name=\"{tabName}\"", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{tabName} が見つかりません。");

        int end = string.IsNullOrEmpty(nextTabName)
            ? xaml.IndexOf("</TabItem>", start, StringComparison.Ordinal)
            : xaml.IndexOf($"<TabItem x:Name=\"{nextTabName}\"", start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start), $"{tabName} の終端が見つかりません。");
        return xaml.Substring(start, end - start);
    }

    private static string GetElementBlock(
        string source,
        string elementStart,
        string marker,
        string elementEnd
    )
    {
        int markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.That(markerIndex, Is.GreaterThanOrEqualTo(0), $"{marker} が見つかりません。");

        int start = source.LastIndexOf(elementStart, markerIndex, StringComparison.Ordinal);
        int end = source.IndexOf(elementEnd, markerIndex, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end, Is.GreaterThan(markerIndex));
        return source.Substring(start, end + elementEnd.Length - start);
    }

    private static string GetMainWindowXaml()
    {
        string root = GetRepositoryRoot();
        return File.ReadAllText(Path.Combine(root, "Views", "Main", "MainWindow.xaml"));
    }

    private static string GetRepositoryRoot([CallerFilePath] string callerFilePath = "")
    {
        string? current = Path.GetDirectoryName(callerFilePath);
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "IndigoMovieManager.sln")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("リポジトリルートを特定できませんでした。");
    }
}
