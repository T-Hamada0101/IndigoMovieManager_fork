using IndigoMovieManager;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class FfmpegAutoGenThumbnailGenerationEngineTests
{
    [Test]
    public void BuildHeaderFallbackCandidateSeconds_短尺では末尾へ丸めて重複除去する()
    {
        List<double> actual =
            FfmpegAutoGenThumbnailGenerationEngine.BuildHeaderFallbackCandidateSeconds(0.069);

        Assert.That(actual, Is.EqualTo(new[] { 0d, 0.068d }));
    }

    [Test]
    public void BuildHeaderFallbackCandidateSeconds_通常尺では既定候補をそのまま返す()
    {
        List<double> actual =
            FfmpegAutoGenThumbnailGenerationEngine.BuildHeaderFallbackCandidateSeconds(3.2);

        Assert.That(actual, Is.EqualTo(new[] { 0d, 0.1d, 0.25d, 0.5d, 1d, 2d }));
    }

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
