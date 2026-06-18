namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class RegisteredMovieCountRefreshPolicyTests
{
    [TestCase(3, 3, true, true)]
    [TestCase(2, 3, true, false)]
    [TestCase(3, 3, false, false)]
    public void ShouldApplyRefreshResult_最新revisionかつ現DBだけ反映する(
        int requestRevision,
        int currentRevision,
        bool isCurrentDb,
        bool expected
    )
    {
        bool actual = RegisteredMovieCountRefreshPolicy.ShouldApplyRefreshResult(
            requestRevision,
            currentRevision,
            isCurrentDb
        );

        Assert.That(actual, Is.EqualTo(expected));
    }
}
