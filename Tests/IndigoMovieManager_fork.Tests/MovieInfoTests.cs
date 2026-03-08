using IndigoMovieManager;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public class MovieInfoTests
{
    [Test]
    public void NormalizeDurationSec_コンテナ尺が極端に壊れている時は映像尺を優先する()
    {
        double normalized = MovieInfo.NormalizeDurationSec(
            29742.068390,
            2400,
            30000d / 1001d
        );

        Assert.That(normalized, Is.EqualTo(80.08d).Within(0.05d));
    }

    [Test]
    public void NormalizeDurationSec_差が小さい時はコンテナ尺を維持する()
    {
        double normalized = MovieInfo.NormalizeDurationSec(
            80.2,
            2400,
            30000d / 1001d
        );

        Assert.That(normalized, Is.EqualTo(80.2d).Within(0.0001d));
    }

    [Test]
    public void NormalizeDurationSec_コンテナ尺が無効な時は映像尺を使う()
    {
        double normalized = MovieInfo.NormalizeDurationSec(
            0,
            2400,
            30000d / 1001d
        );

        Assert.That(normalized, Is.EqualTo(80.08d).Within(0.05d));
    }
}
