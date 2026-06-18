using IndigoMovieManager.Thumbnail.QueueDb;
using IndigoMovieManager.UpperTabs.Common;

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
    public void 詳細サムネ要求は非表示または古いrevisionなら捨てる()
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
            Assert.That(MainWindow.ShouldApplyExtensionDetailImageRequest(hiddenRequest, 12), Is.False);
            Assert.That(staleRequest.IsVisiblePriority, Is.True);
            Assert.That(MainWindow.ShouldApplyExtensionDetailImageRequest(staleRequest, 13), Is.False);
        });
    }
}
