using IndigoMovieManager.Converter;
using IndigoMovieManager.UpperTabs.Common;
using System.Windows.Data;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class NoLockImageConverterTests
{
    [Test]
    public void 画像キャッシュ上限は下限で丸める()
    {
        int actual = NoLockImageConverter.ClampImageCacheEntryLimit(1);

        Assert.That(actual, Is.EqualTo(NoLockImageConverter.MinImageCacheEntries));
    }

    [Test]
    public void 画像キャッシュ上限は上限で丸める()
    {
        int actual = NoLockImageConverter.ClampImageCacheEntryLimit(99999);

        Assert.That(actual, Is.EqualTo(NoLockImageConverter.MaxImageCacheEntries));
    }

    [Test]
    public void 画像キャッシュ上限は範囲内ならそのまま返す()
    {
        int actual = NoLockImageConverter.ClampImageCacheEntryLimit(
            NoLockImageConverter.DefaultImageCacheEntries
        );

        Assert.That(actual, Is.EqualTo(NoLockImageConverter.DefaultImageCacheEntries));
    }

    [Test]
    public void 画像読込試行回数はUIスレッドでは1回()
    {
        int actual = NoLockImageConverter.ResolveBitmapLoadMaxAttempts(isUiThread: true);

        Assert.That(actual, Is.EqualTo(NoLockImageConverter.UiThreadBitmapLoadMaxAttempts));
    }

    [Test]
    public void 画像読込試行回数は非UIスレッドでは3回()
    {
        int actual = NoLockImageConverter.ResolveBitmapLoadMaxAttempts(isUiThread: false);

        Assert.That(actual, Is.EqualTo(NoLockImageConverter.BackgroundBitmapLoadMaxAttempts));
    }

    [Test]
    public void cache_only入口は欠損pathでI_Oせずmissを返す()
    {
        ImageRequest request = ImageRequest.ForUpperTab(
            Path.Combine("missing", Guid.NewGuid() + ".jpg"),
            "movie-key",
            isVisiblePriority: true,
            requestRevision: 4
        );
        ImageDecodeRequest decodeRequest = NoLockImageConverter.BuildImageDecodeRequest(
            request,
            decodePixelHeight: 36,
            logReason: "image.test.cache-only"
        );

        bool found = NoLockImageConverter.TryGetCachedDecodeRequest(
            decodeRequest,
            isExists: true,
            out _
        );

        Assert.That(found, Is.False);
    }

    [Test]
    public void 同期decode入口はImageRequestからDecodeRequestを作る()
    {
        ImageRequest request = ImageRequest.ForUpperTab(
            Path.Combine("thumb", "missing.jpg"),
            "movie-key",
            isVisiblePriority: true,
            requestRevision: 5
        );

        ImageDecodeRequest decodeRequest = NoLockImageConverter.BuildImageDecodeRequest(
            request,
            decodePixelHeight: 36,
            logReason: "image.upper-tab.sync-decode"
        );

        Assert.Multiple(() =>
        {
            Assert.That(decodeRequest.ImageRequest, Is.EqualTo(request));
            Assert.That(decodeRequest.DecodePixelHeight, Is.EqualTo(36));
            Assert.That(decodeRequest.RequestRevision, Is.EqualTo(5));
            Assert.That(decodeRequest.LogReason, Is.EqualTo("image.upper-tab.sync-decode"));
        });
    }

    [Test]
    public void 同期decode入口は既存どおり存在しない画像をDoNothingへ畳む()
    {
        ImageRequest request = ImageRequest.ForUpperTab(
            "",
            "movie-key",
            isVisiblePriority: true,
            requestRevision: 6
        );
        ImageDecodeRequest decodeRequest = NoLockImageConverter.BuildImageDecodeRequest(
            request,
            decodePixelHeight: 0,
            logReason: "image.upper-tab.sync-decode"
        );

        NoLockImageConverter.ImageDecodeExecutionResult result =
            NoLockImageConverter.ConvertDecodeRequest(decodeRequest, isExists: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.Image, Is.SameAs(Binding.DoNothing));
            Assert.That(result.DecodeResult.ImageRequest, Is.EqualTo(request));
            Assert.That(result.DecodeResult.Outcome, Is.EqualTo(ImageLoadOutcome.Missing));
            Assert.That(result.DecodeResult.CacheHit, Is.False);
            Assert.That(result.DecodeResult.DecodeElapsedMilliseconds, Is.GreaterThanOrEqualTo(0));
        });
    }
}
