using IndigoMovieManager;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class MissingThumbnailRescuePolicyTests
{
    [Test]
    public void ShouldSkipMissingThumbnailRescueForBusyQueue_Watch高負荷時は抑止する()
    {
        bool result = MainWindow.ShouldSkipMissingThumbnailRescueForBusyQueue(
            isManualRequest: false,
            activeCount: 14,
            busyThreshold: 14
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldSkipMissingThumbnailRescueForBusyQueue_Manual高負荷時でも抑止しない()
    {
        bool result = MainWindow.ShouldSkipMissingThumbnailRescueForBusyQueue(
            isManualRequest: true,
            activeCount: 14,
            busyThreshold: 14
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void ResolveMissingThumbnailRescueEnqueueQuota_空き枠分だけ投入する()
    {
        int result = MainWindow.ResolveMissingThumbnailRescueEnqueueQuota(
            activeCount: 5,
            targetActiveCount: 32,
            maxEnqueuePerRun: 32
        );

        Assert.That(result, Is.EqualTo(27));
    }

    [Test]
    public void ResolveMissingThumbnailRescueEnqueueQuota_高負荷時は追加しない()
    {
        int result = MainWindow.ResolveMissingThumbnailRescueEnqueueQuota(
            activeCount: 40,
            targetActiveCount: 32,
            maxEnqueuePerRun: 32
        );

        Assert.That(result, Is.Zero);
    }

    [Test]
    public void ShouldRebuildMissingThumbnailRescueBuffer_Manualは常に再構築する()
    {
        bool result = MainWindow.ShouldRebuildMissingThumbnailRescueBuffer(
            isManualRequest: true,
            bufferedCount: 10
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldRebuildMissingThumbnailRescueBuffer_Watchはバッファ残ありなら再利用する()
    {
        bool result = MainWindow.ShouldRebuildMissingThumbnailRescueBuffer(
            isManualRequest: false,
            bufferedCount: 10
        );

        Assert.That(result, Is.False);
    }
}