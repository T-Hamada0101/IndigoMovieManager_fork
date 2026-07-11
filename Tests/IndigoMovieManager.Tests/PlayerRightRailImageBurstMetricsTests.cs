using IndigoMovieManager.UpperTabs.Player;
using IndigoMovieManager.UpperTabs.Common;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class PlayerRightRailImageBurstMetricsTests
{
    [SetUp]
    public void SetUp()
    {
        PlayerRightRailImageBurstMetrics.ResetForTesting();
    }

    [TearDown]
    public void TearDown()
    {
        PlayerRightRailImageBurstMetrics.ResetForTesting();
        PlayerRightRailImageWarmQueue.SetSuspensionProvider(null);
    }

    [Test]
    public void ActiveSession外の記録は無視する()
    {
        PlayerRightRailImageBurstMetrics.RecordConvert();
        PlayerRightRailImageBurstMetrics.RecordCacheHit();
        PlayerRightRailImageBurstMetrics.RecordCacheMiss();
        PlayerRightRailImageBurstMetrics.RecordQueueResult(
            PlayerRightRailImageWarmQueueResult.Enqueued
        );

        Assert.That(PlayerRightRailImageBurstMetrics.End(1, out _), Is.False);
    }

    [Test]
    public void EndはactiveSessionの全カウンターを返す()
    {
        PlayerRightRailImageBurstMetrics.Begin(10);
        PlayerRightRailImageBurstMetrics.RecordConvert();
        PlayerRightRailImageBurstMetrics.RecordConvert();
        PlayerRightRailImageBurstMetrics.RecordCacheHit();
        PlayerRightRailImageBurstMetrics.RecordCacheMiss();
        PlayerRightRailImageBurstMetrics.RecordQueueResult(
            PlayerRightRailImageWarmQueueResult.Enqueued
        );
        PlayerRightRailImageBurstMetrics.RecordQueueResult(
            PlayerRightRailImageWarmQueueResult.Duplicate
        );
        PlayerRightRailImageBurstMetrics.RecordQueueResult(
            PlayerRightRailImageWarmQueueResult.Suppressed
        );

        bool ended = PlayerRightRailImageBurstMetrics.End(10, out var snapshot);

        Assert.Multiple(() =>
        {
            Assert.That(ended, Is.True);
            Assert.That(snapshot.SessionId, Is.EqualTo(10));
            Assert.That(snapshot.ConvertCount, Is.EqualTo(2));
            Assert.That(snapshot.CacheHitCount, Is.EqualTo(1));
            Assert.That(snapshot.CacheMissCount, Is.EqualTo(1));
            Assert.That(snapshot.QueueEnqueuedCount, Is.EqualTo(1));
            Assert.That(snapshot.QueueDuplicateCount, Is.EqualTo(1));
            Assert.That(snapshot.QueueSuppressedCount, Is.EqualTo(1));
            Assert.That(PlayerRightRailImageBurstMetrics.End(10, out _), Is.False);
        });
    }

    [Test]
    public void 古いSessionIdのEndは新しいSessionを終了しない()
    {
        PlayerRightRailImageBurstMetrics.Begin(20);
        PlayerRightRailImageBurstMetrics.RecordConvert();
        PlayerRightRailImageBurstMetrics.Begin(21);
        PlayerRightRailImageBurstMetrics.RecordCacheMiss();

        bool oldEnded = PlayerRightRailImageBurstMetrics.End(20, out _);
        bool currentEnded = PlayerRightRailImageBurstMetrics.End(21, out var snapshot);

        Assert.Multiple(() =>
        {
            Assert.That(oldEnded, Is.False);
            Assert.That(currentEnded, Is.True);
            Assert.That(snapshot.ConvertCount, Is.Zero);
            Assert.That(snapshot.CacheMissCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void 並列記録をInterlockedで取りこぼさない()
    {
        const int count = 1000;
        PlayerRightRailImageBurstMetrics.Begin(30);

        Parallel.For(
            0,
            count,
            index =>
            {
                PlayerRightRailImageBurstMetrics.RecordConvert();
                PlayerRightRailImageBurstMetrics.RecordCacheMiss();
                PlayerRightRailImageBurstMetrics.RecordQueueResult(
                    index % 2 == 0
                        ? PlayerRightRailImageWarmQueueResult.Enqueued
                        : PlayerRightRailImageWarmQueueResult.Duplicate
                );
            }
        );

        Assert.That(PlayerRightRailImageBurstMetrics.End(30, out var snapshot), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.ConvertCount, Is.EqualTo(count));
            Assert.That(snapshot.CacheMissCount, Is.EqualTo(count));
            Assert.That(snapshot.QueueEnqueuedCount, Is.EqualTo(count / 2));
            Assert.That(snapshot.QueueDuplicateCount, Is.EqualTo(count / 2));
        });
    }

    [Test]
    public void WarmQueueはsuspension中にenqueueしない()
    {
        PlayerRightRailImageWarmQueue.SetSuspensionProvider(() => true);
        ImageRequest request = ImageRequest.ForPlayerRightRail(
            "thumb.jpg",
            "movie-key",
            isVisiblePriority: true,
            requestRevision: 1
        );
        ImageDecodeRequest decodeRequest = ImageDecodeRequest.ForSynchronousDecode(
            request,
            decodePixelHeight: 96,
            logReason: "test"
        );

        PlayerRightRailImageWarmQueueResult result = PlayerRightRailImageWarmQueue.Queue(
            decodeRequest,
            isExists: true,
            _ => Assert.Fail("抑止中に完了通知してはならない")
        );

        Assert.That(result, Is.EqualTo(PlayerRightRailImageWarmQueueResult.Suppressed));
    }

    [Test]
    public void WarmQueueはsuspension判定例外時に抑止しない()
    {
        PlayerRightRailImageWarmQueue.SetSuspensionProvider(() => throw new InvalidOperationException());
        ImageRequest request = ImageRequest.ForPlayerRightRail(
            "missing-test-thumb.jpg",
            "exception-safe-key",
            isVisiblePriority: true,
            requestRevision: 1
        );
        ImageDecodeRequest decodeRequest = ImageDecodeRequest.ForSynchronousDecode(
            request,
            decodePixelHeight: 96,
            logReason: "test"
        );

        PlayerRightRailImageWarmQueueResult result = PlayerRightRailImageWarmQueue.Queue(
            decodeRequest,
            isExists: false,
            _ => { }
        );

        Assert.That(result, Is.EqualTo(PlayerRightRailImageWarmQueueResult.Enqueued));
    }
}
