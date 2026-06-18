using System.IO;
using System.Runtime.CompilerServices;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ImagePipelineSourcePolicyTests
{
    private static readonly string[] ImageIoFragments =
    [
        "File.Exists(",
        "new FileInfo(",
        "BitmapDecoder",
        "BitmapImage",
        "NoLockImageConverter",
        "IsErrorMarker(",
    ];

    [Test]
    public void 詳細サムネ要求入口はファイルI_Oとdecodeへ進まない()
    {
        string source = GetRepoText(
            "BottomTabs",
            "Extension",
            "MainWindow.BottomTab.Extension.DetailThumbnail.cs"
        );
        string prepareMethod = ExtractMethod(source, "private void PrepareExtensionDetailThumbnail(");
        string queueMethod = ExtractMethod(
            source,
            "private void QueueExtensionDetailThumbnailSnapshotRefresh("
        );
        string ensureMethod = ExtractMethod(source, "private void EnsureActiveExtensionDetailThumbnail(");

        AssertMethodDoesNotContainImageIo(prepareMethod, nameof(prepareMethod));
        AssertMethodDoesNotContainImageIo(queueMethod, nameof(queueMethod));
        AssertMethodDoesNotContainImageIo(ensureMethod, nameof(ensureMethod));
        Assert.That(queueMethod, Does.Contain("RunExtensionDetailThumbnailSnapshotRefreshAsync(request)"));
    }

    [Test]
    public void Player右レール選択はvisible_refreshへ寄せてdecodeしない()
    {
        string source = GetRepoText(
            "UpperTabs",
            "Player",
            "MainWindow.UpperTabs.PlayerTab.cs"
        );
        string syncMethod = ExtractMethod(source, "private void SyncUpperTabPlayerSelection(");
        string selectionMethod = ExtractMethod(
            source,
            "private void HandleUpperTabPlayerSelectionChanged("
        );
        string queueAutoOpenMethod = ExtractMethod(
            source,
            "private void QueuePlayerTabActivationAutoOpen("
        );

        AssertMethodDoesNotContainImageIo(syncMethod, nameof(syncMethod));
        AssertMethodDoesNotContainImageIo(selectionMethod, nameof(selectionMethod));
        AssertMethodDoesNotContainImageIo(queueAutoOpenMethod, nameof(queueAutoOpenMethod));
        Assert.That(
            syncMethod,
            Does.Contain("RequestUpperTabVisibleRangeRefresh(immediate: true, reason: \"player-selection\")")
        );
        Assert.That(selectionMethod, Does.Contain("QueuePlayerTabActivationAutoOpen(selectedMovie);"));
    }

    [Test]
    public void 上側タブviewport更新入口はvisible_first予約だけを行う()
    {
        string source = GetRepoText(
            "UpperTabs",
            "Common",
            "MainWindow.UpperTabs.Viewport.cs"
        );
        string requestMethod = ExtractMethod(
            source,
            "private void RequestUpperTabVisibleRangeRefresh("
        );

        AssertMethodDoesNotContainImageIo(requestMethod, nameof(requestMethod));
        Assert.That(requestMethod, Does.Contain("MarkRecentViewportInteraction(reason);"));
        Assert.That(requestMethod, Does.Contain("TryStartDispatcherTimer("));
        Assert.That(requestMethod, Does.Contain("ApplyUpperTabVisibleRangeRefresh(reason);"));
    }

    [Test]
    public void 上側タブconverterはImageRequestを作ってからdecodeへ進む()
    {
        string converterSource = GetRepoText(
            "UpperTabs",
            "Common",
            "UpperTabImageSourceConverter.cs"
        );
        string convertMethod = ExtractMethod(converterSource, "public object Convert(");

        Assert.That(convertMethod, Does.Contain("CreateUpperTabImageRequest("));
        Assert.That(convertMethod, Does.Contain("ShouldApplyImageRequest(request)"));
        Assert.That(convertMethod, Does.Contain("request.ThumbnailPath"));
    }

    [Test]
    public void 詳細サムネsnapshotはImageRequestを持ちapply直前でstaleを捨てる()
    {
        string source = GetRepoText(
            "BottomTabs",
            "Extension",
            "MainWindow.BottomTab.Extension.DetailThumbnail.cs"
        );
        string captureMethod = ExtractMethod(
            source,
            "private ExtensionDetailThumbnailSnapshotRequest CaptureExtensionDetailThumbnailSnapshotRequest("
        );
        string applyMethod = ExtractMethod(
            source,
            "private void ApplyExtensionDetailThumbnailSnapshotResult("
        );

        AssertMethodDoesNotContainImageIo(captureMethod, nameof(captureMethod));
        AssertMethodDoesNotContainImageIo(applyMethod, nameof(applyMethod));
        Assert.That(source, Does.Contain("ImageRequest ImageRequest"));
        Assert.That(captureMethod, Does.Contain("CreateExtensionDetailImageRequest("));
        Assert.That(applyMethod, Does.Contain("ShouldApplyExtensionDetailImageRequest("));
        Assert.That(applyMethod, Does.Contain("Volatile.Read(ref _extensionDetailThumbnailRequestVersion)"));
    }

    private static void AssertMethodDoesNotContainImageIo(string methodSource, string methodName)
    {
        foreach (string fragment in ImageIoFragments)
        {
            Assert.That(
                methodSource,
                Does.Not.Contain(fragment),
                $"{methodName} に画像I/Oまたはdecodeを戻さないでください: {fragment}"
            );
        }
    }

    private static string GetRepoText(params string[] relativePathParts)
    {
        foreach (DirectoryInfo searchRoot in EnumerateRepoSearchRoots())
        {
            DirectoryInfo? current = searchRoot;
            while (current != null)
            {
                string candidate = Path.Combine([current.FullName, .. relativePathParts]);
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }

                current = current.Parent;
            }
        }

        Assert.Fail($"{Path.Combine(relativePathParts)} の位置を repo root から解決できませんでした。");
        return "";
    }

    private static IEnumerable<DirectoryInfo> EnumerateRepoSearchRoots(
        [CallerFilePath] string callerFilePath = ""
    )
    {
        string? callerDirectory = Path.GetDirectoryName(callerFilePath);
        if (!string.IsNullOrWhiteSpace(callerDirectory))
        {
            yield return new DirectoryInfo(callerDirectory);
        }

        yield return new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        yield return new DirectoryInfo(TestContext.CurrentContext.WorkDirectory);
        yield return new DirectoryInfo(Directory.GetCurrentDirectory());
    }

    private static string ExtractMethod(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{signature} が見つかりません。");

        int bodyStart = source.IndexOf('{', start);
        Assert.That(bodyStart, Is.GreaterThanOrEqualTo(0), $"{signature} の本文開始が見つかりません。");

        int depth = 0;
        for (int i = bodyStart; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(start, i - start + 1);
                }
            }
        }

        Assert.Fail($"{signature} の本文終端が見つかりません。");
        return "";
    }
}
