using IndigoMovieManager.Skin.Runtime;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WhiteBrowserSkinHostOperationResultTests
{
    [Test]
    public void Factory直後のtimingは成功失敗skipで0埋めされる()
    {
        WhiteBrowserSkinHostOperationResult success =
            WhiteBrowserSkinHostOperationResult.CreateSuccess("SkinA");
        WhiteBrowserSkinHostOperationResult runtimeUnavailable =
            WhiteBrowserSkinHostOperationResult.CreateRuntimeUnavailable("SkinB", "runtime missing");
        WhiteBrowserSkinHostOperationResult missingHtml =
            WhiteBrowserSkinHostOperationResult.CreateMissingHtml("SkinC", "missing.html");
        WhiteBrowserSkinHostOperationResult skipped =
            WhiteBrowserSkinHostOperationResult.CreateSkipped("SkinD", "stale");
        WhiteBrowserSkinHostOperationResult failed =
            WhiteBrowserSkinHostOperationResult.CreateFailed("SkinE", "failed", "HostFailed");

        Assert.Multiple(() =>
        {
            AssertTimingZeros(success);
            AssertTimingZeros(runtimeUnavailable);
            AssertTimingZeros(missingHtml);
            AssertTimingZeros(skipped);
            AssertTimingZeros(failed);
        });
    }

    [Test]
    public void WithTimingsは負値NaNInfinityを0へ丸める()
    {
        WhiteBrowserSkinHostOperationResult result = WhiteBrowserSkinHostOperationResult
            .CreateFailed("SkinA", "null result", "HostNavigateReturnedNull")
            .WithTimings(
                prepareElapsedMilliseconds: -1,
                filePrepareElapsedMilliseconds: double.NaN,
                hostNavigateElapsedMilliseconds: double.PositiveInfinity,
                initialDocumentBuildElapsedMilliseconds: double.NegativeInfinity,
                navigateToStringElapsedMilliseconds: -0.1
            );

        AssertTimingZeros(result);
    }

    [Test]
    public void WithTimingsは未指定の既存timingを保持する()
    {
        WhiteBrowserSkinHostOperationResult initial = WhiteBrowserSkinHostOperationResult
            .CreateSuccess("SkinA")
            .WithTimings(
                prepareElapsedMilliseconds: 11.5,
                filePrepareElapsedMilliseconds: 2.25,
                hostNavigateElapsedMilliseconds: 8.75,
                initialDocumentBuildElapsedMilliseconds: 3.5,
                navigateToStringElapsedMilliseconds: 4.5
            );

        WhiteBrowserSkinHostOperationResult updated = initial.WithTimings(
            hostNavigateElapsedMilliseconds: 9.25
        );

        Assert.Multiple(() =>
        {
            Assert.That(updated.PrepareElapsedMilliseconds, Is.EqualTo(11.5));
            Assert.That(updated.FilePrepareElapsedMilliseconds, Is.EqualTo(2.25));
            Assert.That(updated.HostNavigateElapsedMilliseconds, Is.EqualTo(9.25));
            Assert.That(updated.InitialDocumentBuildElapsedMilliseconds, Is.EqualTo(3.5));
            Assert.That(updated.NavigateToStringElapsedMilliseconds, Is.EqualTo(4.5));
        });
    }

    private static void AssertTimingZeros(WhiteBrowserSkinHostOperationResult result)
    {
        Assert.That(result.PrepareElapsedMilliseconds, Is.EqualTo(0));
        Assert.That(result.FilePrepareElapsedMilliseconds, Is.EqualTo(0));
        Assert.That(result.HostNavigateElapsedMilliseconds, Is.EqualTo(0));
        Assert.That(result.InitialDocumentBuildElapsedMilliseconds, Is.EqualTo(0));
        Assert.That(result.NavigateToStringElapsedMilliseconds, Is.EqualTo(0));
    }
}
