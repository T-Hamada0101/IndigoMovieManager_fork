using IndigoMovieManager;
using IndigoMovieManager.Thumbnail;
using System.Reflection;

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

    [Test]
    public void TakeMissingThumbnailRescueFlushBatch_Quota低下時も未投入分を残す()
    {
        List<QueueObj> batch =
        [
            CreateQueueObj("a.mp4"),
            CreateQueueObj("b.mp4"),
            CreateQueueObj("c.mp4"),
        ];

        List<QueueObj> flushBatch = MainWindow.TakeMissingThumbnailRescueFlushBatch(
            batch,
            maxFlushCount: 1
        );

        Assert.Multiple(() =>
        {
            Assert.That(flushBatch.Select(x => x.MovieFullPath), Is.EqualTo(new[] { "a.mp4" }));
            Assert.That(batch.Select(x => x.MovieFullPath), Is.EqualTo(new[] { "b.mp4", "c.mp4" }));
        });
    }

    [Test]
    public void MissingThumbnailRescueBufferState_RequeueToFrontで未投入分を先頭へ戻す()
    {
        Type? stateType = typeof(MainWindow).GetNestedType(
            "MissingThumbnailRescueBufferState",
            BindingFlags.NonPublic
        );
        Assert.That(stateType, Is.Not.Null);

        object? state = Activator.CreateInstance(stateType!, nonPublic: true);
        Assert.That(state, Is.Not.Null);

        MethodInfo? replaceCandidates = stateType!.GetMethod(
            "ReplaceCandidates",
            BindingFlags.Instance | BindingFlags.Public
        );
        MethodInfo? requeueToFront = stateType.GetMethod(
            "RequeueToFront",
            BindingFlags.Instance | BindingFlags.Public
        );
        MethodInfo? tryDequeue = stateType.GetMethod(
            "TryDequeue",
            BindingFlags.Instance | BindingFlags.Public
        );
        Assert.That(replaceCandidates, Is.Not.Null);
        Assert.That(requeueToFront, Is.Not.Null);
        Assert.That(tryDequeue, Is.Not.Null);

        replaceCandidates!.Invoke(
            state,
            [new List<QueueObj> { CreateQueueObj("c.mp4"), CreateQueueObj("d.mp4") }, DateTime.UtcNow]
        );
        requeueToFront!.Invoke(state, [new List<QueueObj> { CreateQueueObj("a.mp4"), CreateQueueObj("b.mp4") }]);

        Assert.Multiple(() =>
        {
            Assert.That(DequeueMoviePath(tryDequeue!, state), Is.EqualTo("a.mp4"));
            Assert.That(DequeueMoviePath(tryDequeue!, state), Is.EqualTo("b.mp4"));
            Assert.That(DequeueMoviePath(tryDequeue!, state), Is.EqualTo("c.mp4"));
            Assert.That(DequeueMoviePath(tryDequeue!, state), Is.EqualTo("d.mp4"));
        });
    }

    private static string DequeueMoviePath(MethodInfo tryDequeue, object state)
    {
        object?[] args = [null];
        bool dequeued = (bool)(tryDequeue.Invoke(state, args) ?? false);
        Assert.That(dequeued, Is.True);
        Assert.That(args[0], Is.TypeOf<QueueObj>());
        return ((QueueObj)args[0]!).MovieFullPath;
    }

    private static QueueObj CreateQueueObj(string movieFullPath)
    {
        return new QueueObj
        {
            MovieFullPath = movieFullPath,
            Hash = "hash",
            Tabindex = 0,
        };
    }
}
