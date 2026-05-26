using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class SavedSearchPresenterPolicyTests
{
    [Test]
    public void ReloadItems_SavedSearch読込は背景helperへ委譲する()
    {
        string source = GetRepoText(
            "BottomTabs",
            "SavedSearch",
            "SavedSearchTabPresenter.cs"
        );
        string reloadMethod = ExtractMethod(source, "public void ReloadItems()");
        string queueMethod = ExtractMethod(source, "private void QueueReloadItems()");
        string runMethod = ExtractMethod(
            source,
            "private async Task RunReloadItemsAsync(string dbFullPath, int requestRevision)"
        );
        string guardMethod = ExtractMethod(
            source,
            "private bool IsCurrentReloadRequest(string dbFullPath, int requestRevision)"
        );

        Assert.Multiple(() =>
        {
            Assert.That(reloadMethod, Does.Contain("QueueReloadItems();"));
            Assert.That(reloadMethod, Does.Not.Contain("SavedSearchService.LoadItems("));
            Assert.That(queueMethod, Does.Contain("Interlocked.Increment(ref _reloadRevision)"));
            Assert.That(queueMethod, Does.Contain("_ = RunReloadItemsAsync(dbFullPath, requestRevision);"));
            Assert.That(runMethod, Does.Contain("await Task.Run(() =>"));
            Assert.That(runMethod, Does.Contain("SavedSearchService.LoadItems(dbFullPath)"));
            Assert.That(runMethod, Does.Contain("IsCurrentReloadRequest(dbFullPath, requestRevision)"));
            Assert.That(guardMethod, Does.Contain("Volatile.Read(ref _reloadRevision)"));
            Assert.That(guardMethod, Does.Contain("MainWindow.AreSameMainDbPath("));
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

    private static string ExtractMethod(string source, string signature)
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
