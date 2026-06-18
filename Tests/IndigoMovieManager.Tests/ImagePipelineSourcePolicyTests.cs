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
        Assert.That(convertMethod, Does.Contain("ConvertImageRequest("));
        Assert.That(convertMethod, Does.Contain("\"image.upper-tab.sync-decode\""));
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
        Assert.That(source, Does.Contain("ImageProbeRequest ImageProbeRequest"));
        Assert.That(source, Does.Contain("ImageProbeResult ImageProbeResult"));
        Assert.That(captureMethod, Does.Contain("CreateExtensionDetailImageRequest("));
        Assert.That(captureMethod, Does.Contain("ImageProbeRequest.ForExtensionDetailStatus(imageRequest)"));
        Assert.That(applyMethod, Does.Contain("ShouldApplyExtensionDetailImageRequest("));
        Assert.That(applyMethod, Does.Contain("int currentImageRequestRevision = Volatile.Read("));
        Assert.That(applyMethod, Does.Contain("ref _extensionDetailThumbnailRequestVersion"));
    }

    [Test]
    public void 詳細サムネmissing_error_stamp判定は背景probeへ閉じる()
    {
        string source = GetRepoText(
            "BottomTabs",
            "Extension",
            "MainWindow.BottomTab.Extension.DetailThumbnail.cs"
        );
        string loadMethod = ExtractMethod(
            source,
            "private ExtensionDetailThumbnailSnapshotResult LoadExtensionDetailThumbnailSnapshotCore("
        );
        string probeMethod = ExtractMethod(
            source,
            "private static ImageProbeResult BuildExtensionDetailImageProbeResult("
        );
        string stampMethod = ExtractMethod(source, "private static long TryGetImageStampUtcTicks(");
        string applyMethod = ExtractMethod(
            source,
            "private void ApplyExtensionDetailThumbnailSnapshotResult("
        );

        Assert.That(loadMethod, Does.Contain("BuildExtensionDetailImageProbeResult("));
        Assert.That(loadMethod, Does.Contain("ImageProbeLogFields.Build("));
        Assert.That(loadMethod, Does.Contain("BuildExtensionDetailImageLoadResult("));
        Assert.That(loadMethod, Does.Contain("ImageLoadLogFields.Build("));
        Assert.That(probeMethod, Does.Contain("ImageProbeOutcome.Found"));
        Assert.That(probeMethod, Does.Contain("ImageProbeOutcome.ErrorMarker"));
        Assert.That(probeMethod, Does.Contain("ImageProbeOutcome.Missing"));
        Assert.That(probeMethod, Does.Contain("request.ImageProbeRequest.RequiresStampProbe"));
        Assert.That(stampMethod, Does.Contain("FileInfo fileInfo = new(imagePath);"));
        AssertMethodDoesNotContainImageIo(applyMethod, nameof(applyMethod));
        Assert.That(applyMethod, Does.Not.Contain("ImageProbeLogFields.Build("));
    }

    [Test]
    public void 詳細サムネstale_skipはImageLoadResultのcanceled語彙でログへ閉じる()
    {
        string source = GetRepoText(
            "BottomTabs",
            "Extension",
            "MainWindow.BottomTab.Extension.DetailThumbnail.cs"
        );
        string loadMethod = ExtractMethod(
            source,
            "private ExtensionDetailThumbnailSnapshotResult LoadExtensionDetailThumbnailSnapshotCore("
        );
        string applyMethod = ExtractMethod(
            source,
            "private void ApplyExtensionDetailThumbnailSnapshotResult("
        );

        Assert.That(loadMethod, Does.Contain("ImageLoadResult.Canceled("));
        Assert.That(loadMethod, Does.Contain("\"stale-background\""));
        Assert.That(applyMethod, Does.Contain("ImageLoadResult.Canceled("));
        Assert.That(applyMethod, Does.Contain("\"stale-apply\""));
        Assert.That(applyMethod, Does.Contain("\"stale-image-request\""));
        Assert.That(applyMethod, Does.Contain("ImageLoadLogFields.Build("));
        Assert.That(applyMethod, Does.Contain("detail thumbnail image request discarded"));
    }

    [Test]
    public void Player右レールconverterはImageRequestを作ってからdecodeへ進む()
    {
        string converterSource = GetRepoText(
            "UpperTabs",
            "Player",
            "PlayerRightRailImageSourceConverter.cs"
        );
        string convertMethod = ExtractMethod(converterSource, "public object Convert(");

        Assert.That(convertMethod, Does.Contain("CreatePlayerRightRailImageRequest("));
        Assert.That(convertMethod, Does.Contain("ShouldApplyPlayerRightRailImageRequest("));
        Assert.That(convertMethod, Does.Contain("ResolveImageRequestRevision("));
        Assert.That(convertMethod, Does.Contain("BuildImageDecodeRequest("));
        Assert.That(convertMethod, Does.Contain("ConvertDecodeRequest("));
        Assert.That(convertMethod, Does.Contain("ImageDecodeResult"));
        Assert.That(convertMethod, Does.Not.Contain("ConvertImageRequest("));
        Assert.That(convertMethod, Does.Contain("\"image.player-right-rail.sync-decode\""));
        Assert.That(converterSource, Does.Contain("ImageLoadResult.Canceled("));
        Assert.That(converterSource, Does.Contain("\"stale-player-right-rail\""));
    }

    [Test]
    public void ImagePipeline軽量語彙はsource_policyで固定する()
    {
        string source = GetRepoText("UpperTabs", "Common", "ImageRequest.cs");

        Assert.That(source, Does.Contain("internal readonly record struct ImageRequest("));
        Assert.That(source, Does.Contain("internal readonly record struct ImageDecodeRequest("));
        Assert.That(source, Does.Contain("internal readonly record struct ImageDecodeResult("));
        Assert.That(source, Does.Contain("ImageDecodeRequest ForSynchronousDecode("));
        Assert.That(source, Does.Contain("ImageLoadResult ImageLoadResult"));
    }

    [Test]
    public void サムネ進捗preview_converterはImageRequestを作ってからfallback_decodeへ進む()
    {
        string converterSource = GetRepoText(
            "Infrastructure",
            "Converter",
            "ThumbnailProgressPreviewConverter.cs"
        );
        string convertMethod = ExtractMethod(converterSource, "public object Convert(");

        Assert.That(convertMethod, Does.Contain("ImageRequest.ForThumbnailProgressPreview("));
        Assert.That(convertMethod, Does.Contain("ConvertImageRequest("));
        Assert.That(convertMethod, Does.Contain("\"image.thumbnail-progress-preview.sync-decode\""));
        Assert.That(convertMethod, Does.Contain("ThumbnailPreviewCache.Shared.TryGet("));
    }

    [Test]
    public void ThumbnailError一覧converterはImageRequestを作ってからdecodeへ進む()
    {
        string converterSource = GetRepoText(
            "BottomTabs",
            "ThumbnailError",
            "ThumbnailErrorImageSourceConverter.cs"
        );
        string convertMethod = ExtractMethod(converterSource, "public object Convert(");

        Assert.That(convertMethod, Does.Contain("ImageRequest.ForThumbnailErrorList("));
        Assert.That(convertMethod, Does.Contain("ShouldApplyThumbnailErrorListImageRequest(request)"));
        Assert.That(convertMethod, Does.Contain("ConvertImageRequest("));
        Assert.That(convertMethod, Does.Contain("\"image.thumbnail-error-list.sync-decode\""));
        Assert.That(converterSource, Does.Contain("ImageRequestThumbnailRole.ThumbnailErrorList"));
    }

    [Test]
    public void ThumbnailError一覧XAMLは専用converterへ画像要求を渡す()
    {
        string xamlSource = GetRepoText(
            "BottomTabs",
            "ThumbnailError",
            "ThumbnailErrorTabView.xaml"
        );

        Assert.That(xamlSource, Does.Contain("thumbnailErrorImageSourceConverter"));
        Assert.That(xamlSource, Does.Contain("ThumbnailImagePath"));
        Assert.That(xamlSource, Does.Contain("ThumbnailImageRequestRevision"));
        Assert.That(xamlSource, Does.Contain("Width=\"32\""));
        Assert.That(xamlSource, Does.Contain("Height=\"18\""));
        Assert.That(xamlSource, Does.Contain("ConverterParameter=\"18\""));
        Assert.That(xamlSource, Does.Not.Contain("noLockImageConverter"));
    }

    [Test]
    public void ThumbnailError一覧画像列は高密度行高を広げない固定サイズにする()
    {
        string xamlSource = GetRepoText(
            "BottomTabs",
            "ThumbnailError",
            "ThumbnailErrorTabView.xaml"
        );
        string styleSource = GetRepoText("Themes", "Controls", "Lightweight.xaml");

        Assert.That(styleSource, Does.Contain("HighDensityBottomTabDataGridRowStyle"));
        Assert.That(styleSource, Does.Contain("<Setter Property=\"Height\" Value=\"22\" />"));
        Assert.That(xamlSource, Does.Contain("Width=\"32\""));
        Assert.That(xamlSource, Does.Contain("Height=\"18\""));
        Assert.That(xamlSource, Does.Contain("ConverterParameter=\"18\""));
        Assert.That(xamlSource, Does.Not.Contain("ConverterParameter=\"36\""));
    }

    [Test]
    public void ThumbnailError一覧画像pathは背景集計で確定する()
    {
        string source = GetRepoText("Watcher", "MainWindow.ThumbnailFailedTab.cs");
        string buildMethod = ExtractMethod(
            source,
            "private ThumbnailErrorRecordViewModel BuildThumbnailErrorRecord("
        );
        string scanMethod = ExtractMethod(
            source,
            "public static ThumbnailErrorTabScanSnapshot Scan("
        );

        Assert.That(buildMethod, Does.Contain("ResolveThumbnailErrorListImagePath("));
        Assert.That(buildMethod, Does.Contain("ThumbnailImagePath = thumbnailImagePath"));
        Assert.That(buildMethod, Does.Contain("ThumbnailImageRequestRevision = BuildThumbnailErrorListImageRequestRevision("));
        Assert.That(scanMethod, Does.Contain("ThumbnailPathResolver.IsErrorMarker(thumbnailPath)"));
        Assert.That(scanMethod, Does.Contain("MarkerPath = thumbnailPath"));
    }

    [Test]
    public void ThumbnailError一覧UIイベントへ画像I_Oやdecodeを戻さない()
    {
        string source = GetRepoText("Watcher", "MainWindow.ThumbnailFailedTab.cs");
        string reloadMethod = ExtractMethod(
            source,
            "private void ReloadThumbnailErrorListButton_Click("
        );
        string clearMethod = ExtractMethod(
            source,
            "private async void ClearThumbnailErrorListButton_Click("
        );
        string selectedMethod = ExtractMethod(
            source,
            "private async void RescueSelectedThumbnailErrorsButton_Click("
        );
        string allMethod = ExtractMethod(
            source,
            "private async void RescueAllThumbnailErrorsButton_Click("
        );

        AssertMethodDoesNotContainImageIo(reloadMethod, nameof(reloadMethod));
        AssertMethodDoesNotContainImageIo(clearMethod, nameof(clearMethod));
        AssertMethodDoesNotContainImageIo(selectedMethod, nameof(selectedMethod));
        AssertMethodDoesNotContainImageIo(allMethod, nameof(allMethod));
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
