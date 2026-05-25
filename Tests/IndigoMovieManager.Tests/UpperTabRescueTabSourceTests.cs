using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UpperTabRescueTabSourceTests
{
    [Test]
    public void Rescue一覧サムネは行サイズのdecode高さを渡す()
    {
        string xaml = GetRepoText("UpperTabs", "Rescue", "RescueTabView.xaml");

        Assert.That(
            xaml,
            Does.Contain(
                "Source=\"{Binding ThumbnailPath, Converter={StaticResource noLockImageConverter}, ConverterParameter=18}\""
            )
        );
    }

    [TestCase("private async void UpperTabRescueBulkNormalRetryButton_Click(", "queuedCount")]
    [TestCase("private async void UpperTabRescueSelectedNormalRetryButton_Click(", "queuedCount")]
    [TestCase("private async Task RunUpperTabRescueBulkBlackRetryAsync(bool useLiteMode)", "queuedCount")]
    [TestCase("private async Task RunUpperTabRescueSelectedBlackRetryAsync(bool useLiteMode)", "queuedCount")]
    [TestCase("private async Task RunUpperTabRescueSelectedIndexRepairAsync()", "startedCount")]
    public void Rescue再試行dispatch後はRefreshせず投入成功時だけ下部snapshot更新を予約する(
        string signature,
        string guardVariable
    )
    {
        string source = GetRepoText("UpperTabs", "Rescue", "MainWindow.UpperTabs.RescueTab.cs")
            .Replace("\r\n", "\n");
        string method = ExtractMethod(source, signature);
        string guardBlock = ExtractBlock(method, $"if ({guardVariable} > 0)");

        Assert.That(method, Does.Not.Match(@"(?m)^\s*Refresh\(\);\s*$"));
        Assert.That(guardBlock, Does.Contain("RequestThumbnailErrorSnapshotRefresh();"));
        Assert.That(guardBlock, Does.Contain("RequestThumbnailProgressSnapshotRefresh();"));
        Assert.That(
            method.Replace(guardBlock, ""),
            Does.Not.Contain("RequestThumbnailErrorSnapshotRefresh();")
        );
        Assert.That(
            method.Replace(guardBlock, ""),
            Does.Not.Contain("RequestThumbnailProgressSnapshotRefresh();")
        );
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

        return ExtractBlockFromBrace(source, start, signature);
    }

    private static string ExtractBlock(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{signature} が見つかりません。");

        return ExtractBlockFromBrace(source, start, signature);
    }

    private static string ExtractBlockFromBrace(string source, int start, string signature)
    {
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
