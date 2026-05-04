using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ExternalSkinHeaderChromePolicyTests
{
    [Test]
    public void 外部skinでも共通ヘッダーを表示し最小ヘッダーを畳む()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.WebViewSkin.Chrome.cs");
        string xaml = GetRepoText("Views", "Main", "MainWindow.xaml");
        string method = GetMethodBlock(
            source,
            "private void ApplyExternalSkinMinimalChromeVisibility("
        );

        Assert.Multiple(() =>
        {
            Assert.That(
                method,
                Does.Contain("MainHeaderStandardChromePanel.Visibility = Visibility.Visible;")
            );
            Assert.That(
                method,
                Does.Contain("ExternalSkinMinimalChromePanel.Visibility = Visibility.Collapsed;")
            );
            Assert.That(
                method,
                Does.Contain("SyncExternalSkinMinimalSkinSelector(true, displaySkinName);")
            );
            Assert.That(
                method,
                Does.Not.Contain("MainHeaderStandardChromePanel.Visibility = Visibility.Collapsed;")
            );
            Assert.That(
                method,
                Does.Not.Contain("ExternalSkinMinimalChromePanel.Visibility = Visibility.Visible;")
            );
            Assert.That(xaml, Does.Contain("x:Name=\"MainHeaderStandardChromePanel\""));
            Assert.That(xaml, Does.Contain("<RowDefinition Height=\"48\" />"));
            Assert.That(xaml, Does.Contain("x:Name=\"MainHeaderBar\""));
            Assert.That(xaml, Does.Contain("Grid.Row=\"0\""));
            Assert.That(xaml, Does.Contain("VerticalAlignment=\"Top\""));
            Assert.That(xaml, Does.Contain("Height=\"36\""));
            Assert.That(xaml, Does.Contain("<ColumnDefinition Width=\"*\" MinWidth=\"0\" />"));
            Assert.That(xaml, Does.Contain("x:Name=\"ExternalSkinMinimalSkinSelector\""));
            Assert.That(xaml, Does.Contain("Width=\"170\""));
            Assert.That(xaml, Does.Contain("MaxWidth=\"420\""));
            Assert.That(xaml, Does.Contain("TextTrimming=\"CharacterEllipsis\""));
        });
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

        Assert.Fail($"{signature} の本文終了が見つかりません。");
        return string.Empty;
    }
}
