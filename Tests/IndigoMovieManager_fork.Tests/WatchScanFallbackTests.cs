using IndigoMovieManager;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class WatchScanFallbackTests
{
    [TestCase(false, 0, true)]
    [TestCase(false, 3, false)]
    [TestCase(true, 0, false)]
    [TestCase(true, 2, false)]
    public void ShouldFallbackToFilesystemOnEmptyIndexedScan_全量走査の空結果だけをfallback対象にする(
        bool isWatchMode,
        int candidateCount,
        bool expected
    )
    {
        bool actual = MainWindow.ShouldFallbackToFilesystemOnEmptyIndexedScan(
            isWatchMode,
            candidateCount
        );

        Assert.That(actual, Is.EqualTo(expected));
    }
}
