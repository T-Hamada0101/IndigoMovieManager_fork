using System.Text;
using NUnit.Framework;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailUiApplyUserPrioritySourceTests
{
    [Test]
    public void 生成後UI反映はBackgroundで操作優先解除まで延期する()
    {
        string source = GetRepoText("Thumbnail", "MainWindow.ThumbnailCreation.cs");
        string method = ExtractMethod(
            source,
            "private async Task<bool> TryInvokeThumbnailUiReflectionAsync("
        );

        Assert.That(method, Does.Contain("DispatcherPriority priority = DispatcherPriority.Background"));
        Assert.That(method, Does.Contain("if (IsUserPriorityWorkActive())"));
        Assert.That(method, Does.Contain("await Task.Delay(120, cts).ConfigureAwait(false);"));
        Assert.That(method, Does.Contain(".InvokeAsync("));
    }

    [Test]
    public void 生成成功後の局所refreshは操作中に単一タイマーへ戻す()
    {
        string source = GetRepoText("Thumbnail", "MainWindow.ThumbnailFailureSync.cs");
        string method = ExtractMethod(
            source,
            "private async void ThumbnailSuccessMainTabReloadTimer_Tick("
        );

        Assert.That(method, Does.Contain("if (IsUserPriorityWorkActive())"));
        Assert.That(method, Does.Contain("TryStartDispatcherTimer("));
        Assert.That(method, Does.Not.Contain("RequestMainTabLocalRefreshAfterThumbnailSuccess("));
    }

    [Test]
    public void 進捗fallbackは操作中にsnapshot要求だけを保持する()
    {
        string source = GetRepoText(
            "BottomTabs",
            "ThumbnailProgress",
            "MainWindow.BottomTab.ThumbnailProgress.cs"
        );
        string method = ExtractMethod(source, "private void ThumbnailProgressUiTimer_Tick(");

        Assert.That(method, Does.Contain("if (IsUserPriorityWorkActive())"));
        Assert.That(method, Does.Contain("RequestThumbnailProgressSnapshotRefresh();"));
        Assert.That(method, Does.Contain("UpdateThumbnailProgressSnapshotUi();"));
    }

    private static string GetRepoText(params string[] relativeParts)
    {
        string[] pathParts = new[] { FindRepoRoot() }.Concat(relativeParts).ToArray();
        string path = Path.Combine(pathParts);
        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IndigoMovieManager.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("repository root was not found");
    }

    private static string ExtractMethod(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"method not found: {signature}");

        int bodyStart = source.IndexOf('{', start);
        Assert.That(bodyStart, Is.GreaterThan(start));

        int depth = 0;
        for (int index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source[start..(index + 1)];
            }
        }

        Assert.Fail($"method end not found: {signature}");
        return string.Empty;
    }
}
