using IndigoMovieManager.BottomTabs.FileOrganizer;
using NUnit.Framework;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class FileOrganizerMoveConfirmationPolicyTests
{
    [TestCase(1)]
    [TestCase(37)]
    public void 確認文には代表ファイル名と実件数と移動先を含める(int targetCount)
    {
        string actual = FileOrganizerMoveConfirmationPolicy.BuildMessage(
            @"C:\Library\代表動画.mp4",
            targetCount,
            @"D:\Sorted"
        );

        Assert.Multiple(() =>
        {
            Assert.That(actual, Does.Contain("代表ファイル: 代表動画.mp4"));
            Assert.That(actual, Does.Contain($"移動件数: {targetCount} 件"));
            Assert.That(actual, Does.Contain(@"移動先: D:\Sorted"));
        });
    }
}
