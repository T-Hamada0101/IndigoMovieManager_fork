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

    [Test]
    public void ShouldUseShortClipFirstFrameSeekFallback_短尺少数パネルだけTrueを返す()
    {
        Assert.That(
            FfmpegAutoGenThumbnailGenerationEngine.ShouldUseShortClipFirstFrameSeekFallback(
                0.8,
                3
            ),
            Is.True
        );
        Assert.That(
            FfmpegAutoGenThumbnailGenerationEngine.ShouldUseShortClipFirstFrameSeekFallback(
                1.2,
                3
            ),
            Is.False
        );
        Assert.That(
            FfmpegAutoGenThumbnailGenerationEngine.ShouldUseShortClipFirstFrameSeekFallback(
                0.8,
                6
            ),
            Is.False
        );
    }

    [Test]
    public void BuildShortClipFirstFrameSeekCandidates_極小seek候補を重複なく返し元開始秒は除外する()
    {
        IReadOnlyList<double> actual =
            FfmpegAutoGenThumbnailGenerationEngine.BuildShortClipFirstFrameSeekCandidates(
                0.8,
                0.01d
            );

        Assert.That(actual, Does.Contain(0.001d));
        Assert.That(actual, Does.Not.Contain(0.01d));
        Assert.That(actual, Does.Contain(0.016d));
        Assert.That(actual, Does.Contain(0.033d));
    }

    [Test]
    public void BuildShortClipFirstFrameSeekCandidates_超短尺では末尾超過候補を除外する()
    {
        IReadOnlyList<double> actual =
            FfmpegAutoGenThumbnailGenerationEngine.BuildShortClipFirstFrameSeekCandidates(
                0.012,
                0d
            );

        Assert.That(actual, Does.Contain(0.001d));
        Assert.That(actual.All(x => x < 0.012d), Is.True);
        Assert.That(actual, Does.Not.Contain(0.016d));
        Assert.That(actual, Does.Not.Contain(0.033d));
    }

    [Test]
    public void ShouldWriteSeekDebugLog_短尺は拡張子に関係なくTrueを返す()
    {
        Assert.That(
            FfmpegAutoGenThumbnailGenerationEngine.ShouldWriteSeekDebugLog(
                @"E:\temp\shortclip.mkv",
                0.069
            ),
            Is.True
        );
        Assert.That(
            FfmpegAutoGenThumbnailGenerationEngine.ShouldWriteSeekDebugLog(
                @"E:\temp\normalclip.mkv",
                3.5
            ),
            Is.False
        );
    }

    [Test]
    public void ShouldAcceptDecodedFrameAtRequestedSecond_要求秒より十分手前のコマは弾く()
    {
        Assert.That(
            FfmpegAutoGenThumbnailGenerationEngine.ShouldAcceptDecodedFrameAtRequestedSecond(
                1d,
                0d
            ),
            Is.False
        );
        Assert.That(
            FfmpegAutoGenThumbnailGenerationEngine.ShouldAcceptDecodedFrameAtRequestedSecond(
                1d,
                0.95d
            ),
            Is.True
        );
    }

    [Test]
    public void ShouldAcceptDecodedFrameAtRequestedSecond_ゼロ秒要求や不明PTSは許容する()
    {
        Assert.That(
            FfmpegAutoGenThumbnailGenerationEngine.ShouldAcceptDecodedFrameAtRequestedSecond(
                0d,
                0d
            ),
            Is.True
        );
        Assert.That(
            FfmpegAutoGenThumbnailGenerationEngine.ShouldAcceptDecodedFrameAtRequestedSecond(
                0.1d,
                -1d
            ),
            Is.True
        );
    }

    [Test]
    public void ShouldPreferClosestNonBlackThenLatestBright_短尺少数パネルだけTrueを返す()
    {
        Assert.That(
            FfmpegAutoGenThumbnailGenerationEngine.ShouldPreferClosestNonBlackThenLatestBright(
                0.8,
                3
            ),
            Is.True
        );
        Assert.That(
            FfmpegAutoGenThumbnailGenerationEngine.ShouldPreferClosestNonBlackThenLatestBright(
                1.2,
                3
            ),
            Is.False
        );
    }

    [Test]
    public void ResolveDecodedFrameSelectionDecision_短尺では近傍の非黒コマを即採用する()
    {
        Assert.That(
            FfmpegAutoGenThumbnailGenerationEngine.ResolveDecodedFrameSelectionDecision(
                1d,
                0.95d,
                isMostlyBlack: false,
                preferClosestNonBlackThenLatestBright: true
            ),
            Is.EqualTo(
                FfmpegAutoGenThumbnailGenerationEngine.DecodedFrameSelectionDecision.AcceptImmediately
            )
        );
    }

    [Test]
    public void ResolveDecodedFrameSelectionDecision_短尺で近傍が黒ならlatestBright救済へ回す()
    {
        Assert.That(
            FfmpegAutoGenThumbnailGenerationEngine.ResolveDecodedFrameSelectionDecision(
                1d,
                0d,
                isMostlyBlack: false,
                preferClosestNonBlackThenLatestBright: true
            ),
            Is.EqualTo(
                FfmpegAutoGenThumbnailGenerationEngine.DecodedFrameSelectionDecision.KeepAsLatestBrightFallback
            )
        );
        Assert.That(
            FfmpegAutoGenThumbnailGenerationEngine.ResolveDecodedFrameSelectionDecision(
                1d,
                0.95d,
                isMostlyBlack: true,
                preferClosestNonBlackThenLatestBright: true
            ),
            Is.EqualTo(
                FfmpegAutoGenThumbnailGenerationEngine.DecodedFrameSelectionDecision.Ignore
            )
        );
    }

    [Test]
    public void ResolveDecodedFrameSelectionDecision_通常時は従来どおり要求秒整合だけで決める()
    {
        Assert.That(
            FfmpegAutoGenThumbnailGenerationEngine.ResolveDecodedFrameSelectionDecision(
                1d,
                0.95d,
                isMostlyBlack: true,
                preferClosestNonBlackThenLatestBright: false
            ),
            Is.EqualTo(
                FfmpegAutoGenThumbnailGenerationEngine.DecodedFrameSelectionDecision.AcceptImmediately
            )
        );
        Assert.That(
            FfmpegAutoGenThumbnailGenerationEngine.ResolveDecodedFrameSelectionDecision(
                1d,
                0d,
                isMostlyBlack: false,
                preferClosestNonBlackThenLatestBright: false
            ),
            Is.EqualTo(
                FfmpegAutoGenThumbnailGenerationEngine.DecodedFrameSelectionDecision.Ignore
            )
        );
    }

    [Test]
    public void ShouldUseRobustProbeOptions_現状は常にTrueを返す()
    {
        Assert.That(
            FfmpegAutoGenThumbnailGenerationEngine.ShouldUseRobustProbeOptions(
                @"E:\temp\movie.mp4",
                5.8
            ),
            Is.True
        );
    }

    [Test]
    public void IsAutogenSeekInvestigationMovie_真空エラー動画だけTrueを返す()
    {
        Assert.That(
            FfmpegAutoGenThumbnailGenerationEngine.IsAutogenSeekInvestigationMovie(
                @"E:\_サムネイル作成困難動画\真空エラー2_ghq5_temp.mp4"
            ),
            Is.True
        );
        Assert.That(
            FfmpegAutoGenThumbnailGenerationEngine.IsAutogenSeekInvestigationMovie(
                @"E:\temp\normal.mp4"
            ),
            Is.False
        );
    }
}
