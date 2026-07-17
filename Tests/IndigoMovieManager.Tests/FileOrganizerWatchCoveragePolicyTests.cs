using IndigoMovieManager.BottomTabs.FileOrganizer;
using NUnit.Framework;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class FileOrganizerWatchCoveragePolicyTests
{
    [Test]
    public void 同じフォルダがwatch有効なら監視対象になる()
    {
        bool actual = FileOrganizerWatchCoveragePolicy.IsCovered(
            @"C:\Library\Sorted",
            [new FileOrganizerWatchFolder(@"C:\Library\Sorted", true, false)]
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void 親フォルダがsub有効なら配下の移動先も監視対象になる()
    {
        bool actual = FileOrganizerWatchCoveragePolicy.IsCovered(
            @"C:\Library\Sorted\Anime",
            [new FileOrganizerWatchFolder(@"C:\Library", true, true)]
        );

        Assert.That(actual, Is.True);
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public void watch無効またはsub無効の親だけなら監視対象外になる(
        bool watchEnabled,
        bool includeSubdirectories
    )
    {
        bool actual = FileOrganizerWatchCoveragePolicy.IsCovered(
            @"C:\Library\Sorted",
            [new FileOrganizerWatchFolder(@"C:\Library", watchEnabled, includeSubdirectories)]
        );

        Assert.That(actual, Is.False);
    }
}
