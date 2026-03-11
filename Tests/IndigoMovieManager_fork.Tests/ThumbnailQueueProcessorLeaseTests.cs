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

    [Test]
    public void ExecuteWithLeaseHeartbeatAsync_タイムアウト時はTimeoutExceptionで抜ける()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-lease-timeout-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string moviePath = Path.Combine(Path.GetTempPath(), $"timeout-{Guid.NewGuid():N}.mp4");
        bool canceled = false;

        try
        {
            QueueDbLeaseItem leasedItem = CreateLeasedItem(queueDbService, moviePath, tabIndex: 1);
            _ = queueDbService.MarkLeaseAsRunning(
                leasedItem.QueueId,
                leasedItem.OwnerInstanceId,
                DateTime.UtcNow
            );
            leasedItem.StartedAtUtc = DateTime.UtcNow;

            Task task = InvokeExecuteWithLeaseHeartbeatAsync(
                queueDbService,
                leasedItem,
                leasedItem.OwnerInstanceId,
                leaseMinutes: 5,
                async token =>
                {
                    token.Register(() => canceled = true);
                    await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                },
                TimeSpan.FromMilliseconds(200)
            );

            TimeoutException ex = Assert.ThrowsAsync<TimeoutException>(async () => await task);
            Assert.That(ex.Message, Does.Contain("thumbnail processing timeout"));
            Assert.That(canceled, Is.True);
        }
        finally
        {
            TryDelete(queueDbPath);
            TryDelete(mainDbPath);
        }
    }

    [Test]
    public void ExecuteWithLeaseHeartbeatAsync_await前同期ブロックでもTimeoutExceptionで抜ける()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-lease-sync-timeout-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string moviePath = Path.Combine(Path.GetTempPath(), $"sync-timeout-{Guid.NewGuid():N}.mp4");
        bool canceled = false;

        try
        {
            QueueDbLeaseItem leasedItem = CreateLeasedItem(queueDbService, moviePath, tabIndex: 1);
            _ = queueDbService.MarkLeaseAsRunning(
                leasedItem.QueueId,
                leasedItem.OwnerInstanceId,
                DateTime.UtcNow
            );
            leasedItem.StartedAtUtc = DateTime.UtcNow;

            Task task = InvokeExecuteWithLeaseHeartbeatAsync(
                queueDbService,
                leasedItem,
                leasedItem.OwnerInstanceId,
                leaseMinutes: 5,
                token =>
                {
                    token.Register(() => canceled = true);
                    using ManualResetEventSlim gate = new(false);
                    gate.Wait(token);
                    return Task.CompletedTask;
                },
                TimeSpan.FromMilliseconds(200)
            );

            TimeoutException ex = Assert.ThrowsAsync<TimeoutException>(async () => await task);
            Assert.That(ex.Message, Does.Contain("thumbnail processing timeout"));
            Assert.That(canceled, Is.True);
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

    private static Task InvokeExecuteWithLeaseHeartbeatAsync(
        QueueDbService queueDbService,
        QueueDbLeaseItem leasedItem,
        string ownerInstanceId,
        int leaseMinutes,
        Func<CancellationToken, Task> processingAction,
        TimeSpan timeout)
    {
        MethodInfo? method = typeof(ThumbnailQueueProcessor).GetMethod(
            "ExecuteWithLeaseHeartbeatAsync",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert.That(method, Is.Not.Null);

        object? result = method.Invoke(
            null,
            [
                queueDbService,
                leasedItem,
                ownerInstanceId,
                leaseMinutes,
                processingAction,
                (Action<string>)(_ => { }),
                CancellationToken.None,
                timeout,
            ]
        );
        Assert.That(result, Is.Not.Null);
        return (Task)result;
    }

    private static QueueDbLeaseItem CreateLeasedItem(
        QueueDbService queueDbService,
        string moviePath,
        int tabIndex
    )
    {
        _ = queueDbService.Upsert(
            [
                new QueueDbUpsertItem
                {
                    MoviePath = moviePath,
                    MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath),
                    TabIndex = tabIndex,
                },
            ],
            DateTime.UtcNow
        );

        List<QueueDbLeaseItem> leased = queueDbService.GetPendingAndLease(
            $"LEASE-{Guid.NewGuid():N}",
            takeCount: 1,
            leaseDuration: TimeSpan.FromMinutes(5),
            utcNow: DateTime.UtcNow
        );
        Assert.That(leased.Count, Is.EqualTo(1));
        return leased[0];
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
