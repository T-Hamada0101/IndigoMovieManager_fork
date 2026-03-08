using System.Reflection;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailQueueProcessorLeaseTests
{
    [Test]
    public void AcquireLeasedItems_NormalRole_DelegatesSlowInitial_WhenRegularQueueIsEmpty()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-normal-delegate-slow-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        const long oneGbBytes = 1024L * 1024L * 1024L;

        try
        {
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = Path.Combine(Path.GetTempPath(), $"slow-only-{Guid.NewGuid():N}.mkv"),
                        MoviePathKey = Guid.NewGuid().ToString("N"),
                        TabIndex = 1,
                        MovieSizeBytes = 80 * oneGbBytes,
                    },
                ],
                DateTime.UtcNow
            );

            List<QueueDbLeaseItem> leased = InvokeAcquireLeasedItems(
                queueDbService,
                "NORMAL-DELEGATE-OWNER",
                leaseBatchSize: 2,
                leaseMinutes: 5,
                ThumbnailQueueWorkerRole.Normal
            );

            Assert.That(leased, Has.Count.EqualTo(1));
            Assert.That(leased[0].MovieSizeBytes, Is.GreaterThanOrEqualTo(50 * oneGbBytes));
            Assert.That(leased[0].AttemptCount, Is.EqualTo(0));
        }
        finally
        {
            TryDelete(queueDbPath);
            TryDelete(mainDbPath);
        }
    }

    [Test]
    public void AcquireLeasedItems_NormalRole_DoesNotDelegateSlowInitial_WhenRegularQueueExists()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-normal-keeps-regular-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        const long oneGbBytes = 1024L * 1024L * 1024L;

        try
        {
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = Path.Combine(Path.GetTempPath(), $"normal-{Guid.NewGuid():N}.mp4"),
                        MoviePathKey = Guid.NewGuid().ToString("N"),
                        TabIndex = 1,
                        MovieSizeBytes = 2 * oneGbBytes,
                    },
                    new QueueDbUpsertItem
                    {
                        MoviePath = Path.Combine(Path.GetTempPath(), $"slow-{Guid.NewGuid():N}.mkv"),
                        MoviePathKey = Guid.NewGuid().ToString("N"),
                        TabIndex = 2,
                        MovieSizeBytes = 90 * oneGbBytes,
                    },
                ],
                DateTime.UtcNow
            );

            List<QueueDbLeaseItem> leased = InvokeAcquireLeasedItems(
                queueDbService,
                "NORMAL-REGULAR-OWNER",
                leaseBatchSize: 2,
                leaseMinutes: 5,
                ThumbnailQueueWorkerRole.Normal
            );

            Assert.That(leased, Has.Count.EqualTo(1));
            Assert.That(leased[0].MovieSizeBytes, Is.LessThan(50 * oneGbBytes));
        }
        finally
        {
            TryDelete(queueDbPath);
            TryDelete(mainDbPath);
        }
    }

    private static List<QueueDbLeaseItem> InvokeAcquireLeasedItems(
        QueueDbService queueDbService,
        string ownerInstanceId,
        int leaseBatchSize,
        int leaseMinutes,
        ThumbnailQueueWorkerRole workerRole
    )
    {
        MethodInfo? method = typeof(ThumbnailQueueProcessor).GetMethod(
            "AcquireLeasedItems",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert.That(method, Is.Not.Null);

        object? result = method.Invoke(
            null,
            [
                queueDbService,
                ownerInstanceId,
                leaseBatchSize,
                leaseMinutes,
                (Func<int?>)(() => null),
                (Action<string>)(_ => { }),
                workerRole,
            ]
        );
        Assert.That(result, Is.Not.Null);
        return (List<QueueDbLeaseItem>)result;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // テスト後始末の失敗は無視する。
        }
    }
}
