using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class LogTabPreviewIoPolicyTests
{
    [Test]
    public void LogタブPreview更新は同期ファイルIOを背景helperへ逃がす()
    {
        string source = GetRepoText("BottomTabs", "LogTab", "MainWindow.BottomTab.Log.cs");
        string refreshMethod = ExtractMethod(
            source,
            "private async void RefreshLogTabPreview("
        );
        string loadMethod = ExtractMethod(
            source,
            "private static LogPreviewSnapshot LoadLogPreviewSnapshot("
        );

        Assert.That(refreshMethod, Does.Contain("Task.Run("));
        Assert.That(refreshMethod, Does.Contain("LoadLogPreviewSnapshot("));
        Assert.That(refreshMethod, Does.Contain("requestId != _logTabPreviewRequestId"));
        Assert.That(refreshMethod, Does.Not.Contain("File.Exists("));
        Assert.That(refreshMethod, Does.Not.Contain("File.GetLastWriteTimeUtc("));
        Assert.That(refreshMethod, Does.Not.Contain("ReadLogPreview("));

        Assert.That(loadMethod, Does.Contain("File.Exists("));
        Assert.That(loadMethod, Does.Contain("File.GetLastWriteTimeUtc("));
        Assert.That(loadMethod, Does.Contain("ReadLogPreview(logPath)"));
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
