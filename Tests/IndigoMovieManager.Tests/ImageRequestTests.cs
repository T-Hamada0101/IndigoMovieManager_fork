using IndigoMovieManager.Thumbnail.QueueDb;
using IndigoMovieManager.UpperTabs.Common;
using IndigoMovieManager.UpperTabs.Player;
using System.Windows;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ImageRequestTests
{
    [SetUp]
    public void SetUp()
    {
        UpperTabActivationGate.ClearPreferredMoviePathKeys();
    }

    [TearDown]
    public void TearDown()
    {
        UpperTabActivationGate.ClearPreferredMoviePathKeys();
    }

    [Test]
    public void UpperTab要求はrole_cache_revisionを保持する()
    {
        string moviePath = Path.Combine("movies", "visible.mp4");
        ImageRequest request = UpperTabActivationGate.CreateUpperTabImageRequest(
            @"C:\thumb\visible.jpg",
            true,
            moviePath,
            requestRevision: 42
        );

        Assert.That(request.ThumbnailRole, Is.EqualTo(ImageRequestThumbnailRole.UpperTab));
        Assert.That(request.CachePolicy, Is.EqualTo(ImageRequestCachePolicy.UseConverterCache));
        Assert.That(request.RequestRevision, Is.EqualTo(42));
        Assert.That(request.ThumbnailPath, Is.EqualTo(@"C:\thumb\visible.jpg"));
        Assert.That(request.MoviePathKey, Is.EqualTo(QueueDbPathResolver.CreateMoviePathKey(moviePath)));
        Assert.That(request.IsVisiblePriority, Is.True);
        Assert.That(request.ShouldDecode, Is.True);
    }

    [Test]
    public void 非選択タブはdecode対象外の要求になる()
    {
        ImageRequest request = UpperTabActivationGate.CreateUpperTabImageRequest(
            @"C:\thumb\hidden.jpg",
            false,
            Path.Combine("movies", "hidden.mp4"),
            requestRevision: 1
        );

        Assert.That(request.IsVisiblePriority, Is.False);
        Assert.That(UpperTabActivationGate.ShouldApplyImageRequest(request), Is.False);
    }

    [Test]
    public void 可視近傍snapshot外はstale要求として捨てられる()
    {
        string visibleMoviePath = Path.Combine("movies", "visible.mp4");
        string hiddenMoviePath = Path.Combine("movies", "hidden.mp4");
        UpperTabActivationGate.UpdatePreferredMoviePathKeys(
            [QueueDbPathResolver.CreateMoviePathKey(visibleMoviePath)]
        );

        ImageRequest hiddenRequest = UpperTabActivationGate.CreateUpperTabImageRequest(
            @"C:\thumb\hidden.jpg",
            true,
            hiddenMoviePath,
            requestRevision: 7
        );

        Assert.That(hiddenRequest.MoviePathKey, Is.Not.Empty);
        Assert.That(hiddenRequest.IsVisiblePriority, Is.False);
        Assert.That(hiddenRequest.ShouldDecode, Is.False);
    }

    [Test]
    public void 可視近傍snapshot一致はvisible_first要求として通す()
    {
        string visibleMoviePath = Path.Combine("movies", "visible.mp4");
        UpperTabActivationGate.UpdatePreferredMoviePathKeys(
            [QueueDbPathResolver.CreateMoviePathKey(visibleMoviePath)]
        );

        ImageRequest request = UpperTabActivationGate.CreateUpperTabImageRequest(
            @"C:\thumb\visible.jpg",
            true,
            visibleMoviePath,
            requestRevision: 8
        );

        Assert.That(request.IsVisiblePriority, Is.True);
        Assert.That(UpperTabActivationGate.ShouldApplyImageRequest(request), Is.True);
    }

    [Test]
    public void 詳細サムネ要求はrole_cache_revisionを保持する()
    {
        string moviePath = Path.Combine("movies", "detail.mp4");
        ImageRequest request = MainWindow.CreateExtensionDetailImageRequest(
            @"C:\thumb\detail.jpg",
            moviePath,
            isVisiblePriority: true,
            requestRevision: 11
        );

        Assert.Multiple(() =>
        {
            Assert.That(request.ThumbnailRole, Is.EqualTo(ImageRequestThumbnailRole.ExtensionDetail));
            Assert.That(request.CachePolicy, Is.EqualTo(ImageRequestCachePolicy.UseConverterCache));
            Assert.That(request.RequestRevision, Is.EqualTo(11));
            Assert.That(request.ThumbnailPath, Is.EqualTo(@"C:\thumb\detail.jpg"));
            Assert.That(request.MoviePathKey, Is.EqualTo(QueueDbPathResolver.CreateMoviePathKey(moviePath)));
            Assert.That(request.IsVisiblePriority, Is.True);
            Assert.That(MainWindow.ShouldApplyExtensionDetailImageRequest(request, 11), Is.True);
        });
    }

    [Test]
    public void 詳細サムネ要求は非表示でも状態を保持し古いrevisionだけ捨てる()
    {
        ImageRequest hiddenRequest = MainWindow.CreateExtensionDetailImageRequest(
            @"C:\thumb\hidden-detail.jpg",
            Path.Combine("movies", "hidden-detail.mp4"),
            isVisiblePriority: false,
            requestRevision: 12
        );
        ImageRequest staleRequest = MainWindow.CreateExtensionDetailImageRequest(
            @"C:\thumb\stale-detail.jpg",
            Path.Combine("movies", "stale-detail.mp4"),
            isVisiblePriority: true,
            requestRevision: 12
        );

        Assert.Multiple(() =>
        {
            Assert.That(hiddenRequest.IsVisiblePriority, Is.False);
            Assert.That(MainWindow.ShouldApplyExtensionDetailImageRequest(hiddenRequest, 12), Is.True);
            Assert.That(staleRequest.IsVisiblePriority, Is.True);
            Assert.That(MainWindow.ShouldApplyExtensionDetailImageRequest(staleRequest, 13), Is.False);
        });
    }

    [Test]
    public void 詳細サムネprobe要求はmissing_error_stampを背景確認語彙として保持する()
    {
        ImageRequest imageRequest = MainWindow.CreateExtensionDetailImageRequest(
            Path.Combine("thumb", "detail.jpg"),
            Path.Combine("movies", "detail.mp4"),
            isVisiblePriority: false,
            requestRevision: 31
        );

        ImageProbeRequest probeRequest = ImageProbeRequest.ForExtensionDetailStatus(imageRequest);
        ImageProbeResult probeResult = new(
            ImageProbeOutcome.Missing,
            IsMissing: true,
            HasErrorMarker: false,
            StampUtcTicks: 0
        );

        string logFields = ImageProbeLogFields.Build(probeRequest, probeResult);

        Assert.Multiple(() =>
        {
            Assert.That(probeRequest.ImageRequest, Is.EqualTo(imageRequest));
            Assert.That(probeRequest.RequiresMissingProbe, Is.True);
            Assert.That(probeRequest.RequiresErrorMarkerProbe, Is.True);
            Assert.That(probeRequest.RequiresStampProbe, Is.True);
            Assert.That(probeRequest.LogReason, Is.EqualTo("image.extension-detail.probe"));
            Assert.That(probeResult.OutcomeLogValue, Is.EqualTo("missing"));
            Assert.That(logFields, Does.Contain("image_log_reason=image.extension-detail.probe"));
            Assert.That(logFields, Does.Contain("image_role=ExtensionDetail"));
            Assert.That(logFields, Does.Contain("probe_outcome=missing"));
            Assert.That(logFields, Does.Contain("missing=true"));
            Assert.That(logFields, Does.Contain("error_marker=false"));
            Assert.That(logFields, Does.Contain("stamp_utc_ticks=0"));
        });
    }

    [Test]
    public void Player右レール要求はrole_visible_cache_revisionを保持する()
    {
        string moviePath = Path.Combine("movies", "player-right-rail.mp4");
        ImageRequest request = UpperTabActivationGate.CreatePlayerRightRailImageRequest(
            @"C:\thumb\player-right-rail.jpg",
            true,
            moviePath,
            requestRevision: 21
        );

        Assert.Multiple(() =>
        {
            Assert.That(request.ThumbnailRole, Is.EqualTo(ImageRequestThumbnailRole.PlayerRightRail));
            Assert.That(request.CachePolicy, Is.EqualTo(ImageRequestCachePolicy.UseConverterCache));
            Assert.That(request.RequestRevision, Is.EqualTo(21));
            Assert.That(request.ThumbnailPath, Is.EqualTo(@"C:\thumb\player-right-rail.jpg"));
            Assert.That(request.MoviePathKey, Is.EqualTo(QueueDbPathResolver.CreateMoviePathKey(moviePath)));
            Assert.That(request.IsVisiblePriority, Is.True);
            Assert.That(UpperTabActivationGate.ShouldApplyPlayerRightRailImageRequest(request, 21), Is.True);
        });
    }

    [Test]
    public void Player右レール要求は非表示でも状態を保持し古いrevisionだけ捨てる()
    {
        ImageRequest hiddenRequest = UpperTabActivationGate.CreatePlayerRightRailImageRequest(
            @"C:\thumb\hidden-player-right-rail.jpg",
            false,
            Path.Combine("movies", "hidden-player-right-rail.mp4"),
            requestRevision: 22
        );
        ImageRequest staleRequest = UpperTabActivationGate.CreatePlayerRightRailImageRequest(
            @"C:\thumb\stale-player-right-rail.jpg",
            true,
            Path.Combine("movies", "stale-player-right-rail.mp4"),
            requestRevision: 22
        );

        Assert.Multiple(() =>
        {
            Assert.That(hiddenRequest.IsVisiblePriority, Is.False);
            Assert.That(
                UpperTabActivationGate.ShouldApplyPlayerRightRailImageRequest(hiddenRequest, 22),
                Is.True
            );
            Assert.That(staleRequest.IsVisiblePriority, Is.True);
            Assert.That(
                UpperTabActivationGate.ShouldApplyPlayerRightRailImageRequest(staleRequest, 23),
                Is.False
            );
        });
    }

    [Test]
    public void Player右レールstale_skipはcanceled_decode_resultを保持する()
    {
        ImageRequest request = UpperTabActivationGate.CreatePlayerRightRailImageRequest(
            @"C:\thumb\stale-player-right-rail.jpg",
            true,
            Path.Combine("movies", "stale-player-right-rail.mp4"),
            requestRevision: 22
        );

        object image = PlayerRightRailImageSourceConverter.ResolveStalePlayerRightRailImageResult(
            request,
            currentRevision: 23,
            out ImageDecodeResult decodeResult
        );

        Assert.Multiple(() =>
        {
            Assert.That(image, Is.SameAs(DependencyProperty.UnsetValue));
            Assert.That(decodeResult.ImageRequest, Is.EqualTo(request));
            Assert.That(decodeResult.Outcome, Is.EqualTo(ImageLoadOutcome.Canceled));
            Assert.That(decodeResult.ImageLoadResult.IsStale, Is.True);
            Assert.That(
                decodeResult.ImageLoadResult.FailureReason,
                Is.EqualTo("stale-player-right-rail")
            );
            Assert.That(decodeResult.ImageLoadResult.ResultRevision, Is.EqualTo(23));
            Assert.That(decodeResult.DecodeElapsedMilliseconds, Is.EqualTo(0));
            Assert.That(decodeResult.CacheHit, Is.False);
        });
    }

    [Test]
    public void サムネ進捗preview要求はrole_cache_revisionを保持する()
    {
        ImageRequest request = ImageRequest.ForThumbnailProgressPreview(
            @"C:\thumb\progress-preview.jpg",
            "worker-preview-key",
            requestRevision: 33
        );

        Assert.Multiple(() =>
        {
            Assert.That(request.ThumbnailRole, Is.EqualTo(ImageRequestThumbnailRole.ThumbnailProgressPreview));
            Assert.That(request.CachePolicy, Is.EqualTo(ImageRequestCachePolicy.UseConverterCache));
            Assert.That(request.RequestRevision, Is.EqualTo(33));
            Assert.That(request.ThumbnailPath, Is.EqualTo(@"C:\thumb\progress-preview.jpg"));
            Assert.That(request.MoviePathKey, Is.EqualTo("worker-preview-key"));
            Assert.That(request.IsVisiblePriority, Is.True);
            Assert.That(request.ShouldDecode, Is.True);
        });
    }

    [Test]
    public void ThumbnailError一覧要求はrole_cache_revisionを保持する()
    {
        string moviePath = Path.Combine("movies", "error-list.mp4");
        ImageRequest request = ImageRequest.ForThumbnailErrorList(
            @"C:\thumb\error-list.#ERROR.jpg",
            QueueDbPathResolver.CreateMoviePathKey(moviePath),
            requestRevision: 44
        );

        Assert.Multiple(() =>
        {
            Assert.That(request.ThumbnailRole, Is.EqualTo(ImageRequestThumbnailRole.ThumbnailErrorList));
            Assert.That(request.CachePolicy, Is.EqualTo(ImageRequestCachePolicy.UseConverterCache));
            Assert.That(request.RequestRevision, Is.EqualTo(44));
            Assert.That(request.ThumbnailPath, Is.EqualTo(@"C:\thumb\error-list.#ERROR.jpg"));
            Assert.That(request.MoviePathKey, Is.EqualTo(QueueDbPathResolver.CreateMoviePathKey(moviePath)));
            Assert.That(request.IsVisiblePriority, Is.True);
            Assert.That(request.ShouldDecode, Is.True);
        });
    }

    [Test]
    public void ImageLoadResultはready_missing_canceled_failedをログ語彙へ畳める()
    {
        ImageRequest request = ImageRequest.ForExtensionDetail(
            Path.Combine("thumb", "detail.jpg"),
            "movie-key",
            isVisiblePriority: true,
            requestRevision: 51
        );

        ImageLoadResult ready = ImageLoadResult.Ready(
            request,
            usesPlaceholder: false,
            resultRevision: 51
        );
        ImageLoadResult missing = ImageLoadResult.Missing(request, resultRevision: 51);
        ImageLoadResult canceled = ImageLoadResult.Canceled(
            request,
            resultRevision: 52,
            failureReason: "stale-image-request",
            isStale: true
        );
        ImageLoadResult failed = ImageLoadResult.Failed(
            request,
            resultRevision: 51,
            failureReason: "error-marker",
            usesPlaceholder: true
        );
        ImageLoadResult markerFailed = ImageLoadResult.Failed(
            request,
            resultRevision: 51,
            failureReason: "error-marker",
            usesPlaceholder: false,
            hasResolvedImage: true
        );

        string readyLog = ImageLoadLogFields.Build(ready);
        string missingLog = ImageLoadLogFields.Build(missing);
        string canceledLog = ImageLoadLogFields.Build(canceled);
        string failedLog = ImageLoadLogFields.Build(failed);

        Assert.Multiple(() =>
        {
            Assert.That(ready.OutcomeLogValue, Is.EqualTo("ready"));
            Assert.That(missing.OutcomeLogValue, Is.EqualTo("missing"));
            Assert.That(canceled.OutcomeLogValue, Is.EqualTo("canceled"));
            Assert.That(failed.OutcomeLogValue, Is.EqualTo("failed"));
            Assert.That(readyLog, Does.Contain("image_role=ExtensionDetail"));
            Assert.That(readyLog, Does.Contain("image_outcome=ready"));
            Assert.That(readyLog, Does.Contain("resolved=true"));
            Assert.That(missingLog, Does.Contain("image_outcome=missing"));
            Assert.That(missingLog, Does.Contain("resolved=false"));
            Assert.That(canceledLog, Does.Contain("image_request_revision=51"));
            Assert.That(canceledLog, Does.Contain("image_result_revision=52"));
            Assert.That(canceledLog, Does.Contain("image_outcome=canceled"));
            Assert.That(canceledLog, Does.Contain("stale=true"));
            Assert.That(canceledLog, Does.Contain("failure_reason=stale-image-request"));
            Assert.That(failedLog, Does.Contain("placeholder=true"));
            Assert.That(failedLog, Does.Contain("failure_reason=error-marker"));
            Assert.That(markerFailed.HasResolvedImage, Is.True);
            Assert.That(markerFailed.UsesPlaceholder, Is.False);
            Assert.That(ImageLoadLogFields.Build(markerFailed), Does.Contain("resolved=true"));
        });
    }

    [Test]
    public void ImageDecodeRequestはdecode最小語彙をImageRequestから作る()
    {
        ImageRequest imageRequest = ImageRequest.ForUpperTab(
            Path.Combine("thumb", "visible.jpg"),
            "movie-key",
            isVisiblePriority: true,
            requestRevision: 61
        );

        ImageDecodeRequest decodeRequest = ImageDecodeRequest.ForSynchronousDecode(
            imageRequest,
            decodePixelHeight: 72,
            logReason: "image.upper-tab.sync-decode"
        );

        Assert.Multiple(() =>
        {
            Assert.That(decodeRequest.ImageRequest, Is.EqualTo(imageRequest));
            Assert.That(decodeRequest.DecodePixelHeight, Is.EqualTo(72));
            Assert.That(decodeRequest.LogReason, Is.EqualTo("image.upper-tab.sync-decode"));
            Assert.That(decodeRequest.RequestRevision, Is.EqualTo(61));
        });
    }

    [Test]
    public void ImageDecodeResultはload結果とdecode計測を保持する()
    {
        ImageRequest imageRequest = ImageRequest.ForThumbnailErrorList(
            Path.Combine("thumb", "error.jpg"),
            "movie-key",
            requestRevision: 71
        );
        ImageLoadResult loadResult = ImageLoadResult.Ready(
            imageRequest,
            usesPlaceholder: false,
            resultRevision: 71
        );
        ImageDecodeRequest decodeRequest = ImageDecodeRequest.ForSynchronousDecode(
            imageRequest,
            decodePixelHeight: 18,
            logReason: "image.thumbnail-error-list.sync-decode"
        );

        ImageDecodeResult decodeResult = new(
            loadResult,
            DecodeElapsedMilliseconds: 12,
            CacheHit: true
        );
        string logFields = ImageDecodeLogFields.Build(decodeRequest, decodeResult);

        Assert.Multiple(() =>
        {
            Assert.That(decodeResult.ImageRequest, Is.EqualTo(imageRequest));
            Assert.That(decodeResult.Outcome, Is.EqualTo(ImageLoadOutcome.Ready));
            Assert.That(decodeResult.DecodeElapsedMilliseconds, Is.EqualTo(12));
            Assert.That(decodeResult.CacheHit, Is.True);
            Assert.That(logFields, Does.Contain("image_log_reason=image.thumbnail-error-list.sync-decode"));
            Assert.That(logFields, Does.Contain("decode_pixel_height=18"));
            Assert.That(logFields, Does.Contain("decode_elapsed_ms=12"));
            Assert.That(logFields, Does.Contain("cache_hit=true"));
        });
    }

    [Test]
    public void ImageDecodePlanResultは背景probeの判定をdecode語彙へ畳める()
    {
        ImageRequest imageRequest = ImageRequest.ForThumbnailErrorList(
            Path.Combine("thumb", "error.#ERROR.jpg"),
            "movie-key",
            requestRevision: 81
        );
        ImageDecodeRequest decodeRequest = ImageDecodeRequest.ForSynchronousDecode(
            imageRequest,
            decodePixelHeight: 18,
            logReason: "image.thumbnail-error-list.aggregate-decode-plan"
        );
        ImageLoadResult loadResult = ImageLoadResult.Failed(
            imageRequest,
            resultRevision: 81,
            failureReason: "error-marker",
            usesPlaceholder: false,
            hasResolvedImage: true
        );

        ImageDecodePlanResult planResult = ImageDecodePlanResult.FromBackgroundProbe(
            decodeRequest,
            loadResult
        );
        string logFields = ImageDecodePlanLogFields.Build(planResult);

        Assert.Multiple(() =>
        {
            Assert.That(planResult.DecodeRequest, Is.EqualTo(decodeRequest));
            Assert.That(planResult.ImageLoadResult, Is.EqualTo(loadResult));
            Assert.That(planResult.DecodeResult.Outcome, Is.EqualTo(ImageLoadOutcome.Failed));
            Assert.That(planResult.DecodeResult.DecodeElapsedMilliseconds, Is.EqualTo(0));
            Assert.That(planResult.DecodeResult.CacheHit, Is.False);
            Assert.That(planResult.DecodeAttempted, Is.False);
            Assert.That(logFields, Does.Contain("image_log_reason=image.thumbnail-error-list.aggregate-decode-plan"));
            Assert.That(logFields, Does.Contain("decode_pixel_height=18"));
            Assert.That(logFields, Does.Contain("image_outcome=failed"));
            Assert.That(logFields, Does.Contain("decode_attempted=false"));
            Assert.That(logFields, Does.Contain("image_result_revision=81"));
            Assert.That(logFields, Does.Contain("resolved=true"));
            Assert.That(logFields, Does.Contain("placeholder=false"));
            Assert.That(logFields, Does.Contain("stale=false"));
            Assert.That(logFields, Does.Contain("failure_reason=error-marker"));
        });
    }
}
