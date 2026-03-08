using IndigoMovieManager;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class FfmpegAutoGenThumbnailGenerationEngineTests
{
    [Test]
    public void ResolveCaptureSeconds_1秒未満で全パネル0秒の時は実時間へ均等化する()
    {
        ThumbInfo thumbInfo = new();
        thumbInfo.ThumbSec = [0, 0, 0, 0];

        List<double> actual = FfmpegAutoGenThumbnailGenerationEngine.ResolveCaptureSeconds(
            thumbInfo,
            0.069
        );

        Assert.That(actual, Has.Count.EqualTo(4));
        Assert.That(actual[0], Is.GreaterThan(0).And.LessThan(0.069));
        Assert.That(actual[1], Is.GreaterThan(actual[0]));
        Assert.That(actual[2], Is.GreaterThan(actual[1]));
        Assert.That(actual[3], Is.GreaterThan(actual[2]));
    }

    [Test]
    public void ResolveCaptureSeconds_1秒以上では既存の整数秒を維持する()
    {
        ThumbInfo thumbInfo = new();
        thumbInfo.ThumbSec = [0, 1, 2];

        List<double> actual = FfmpegAutoGenThumbnailGenerationEngine.ResolveCaptureSeconds(
            thumbInfo,
            3.5
        );

        Assert.That(actual, Is.EqualTo(new[] { 0d, 1d, 2d }));
    }
}
