using IndigoMovieManager.Converter;

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
}
