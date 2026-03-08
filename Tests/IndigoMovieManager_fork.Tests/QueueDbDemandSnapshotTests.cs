using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class QueueDbDemandSnapshotTests
{
    [Test]
    public void GetDemandSnapshot_QueuesAndRunningCounts_AreSplitByLane()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-demand-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        DateTime nowUtc = DateTime.UtcNow;
        const long oneGbBytes = 1024L * 1024L * 1024L;
        string owner = "DEMAND-OWNER";

        try
        {
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = Path.Combine(Path.GetTempPath(), $"normal-{Guid.NewGuid():N}.mp4"),
                        MoviePathKey = Guid.NewGuid().ToString("N"),
                        TabIndex = 1,
                        MovieSizeBytes = oneGbBytes,
                    },
                    new QueueDbUpsertItem
                    {
                        MoviePath = Path.Combine(Path.GetTempPath(), $"slow-{Guid.NewGuid():N}.mkv"),
                        MoviePathKey = Guid.NewGuid().ToString("N"),
                        TabIndex = 2,
                        MovieSizeBytes = 100 * oneGbBytes,
                    },
                    new QueueDbUpsertItem
                    {
                        MoviePath = Path.Combine(Path.GetTempPath(), $"recovery-{Guid.NewGuid():N}.mp4"),
                        MoviePathKey = Guid.NewGuid().ToString("N"),
                        TabIndex = 3,
                        MovieSizeBytes = 2 * oneGbBytes,
                    },
                ],
                nowUtc
            );

            List<QueueDbLeaseItem> recoverySeedLease = queueDbService.GetPendingAndLease(
                owner,
                takeCount: 1,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: nowUtc,
                preferredTabIndex: 3
            );
            _ = queueDbService.UpdateStatus(
                recoverySeedLease[0].QueueId,
                owner,
                ThumbnailQueueStatus.Pending,
                nowUtc.AddSeconds(1),
                lastError: "seed retry",
                incrementAttemptCount: true
            );

            List<QueueDbLeaseItem> slowRunningLease = queueDbService.GetPendingAndLease(
                owner,
                takeCount: 1,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: nowUtc.AddSeconds(2),
                preferredTabIndex: 2
            );

            QueueDbDemandSnapshot snapshot = queueDbService.GetDemandSnapshot(
                [owner],
                50 * oneGbBytes,
                nowUtc.AddSeconds(3)
            );

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.QueuedNormalCount, Is.EqualTo(1));
                Assert.That(snapshot.QueuedSlowCount, Is.EqualTo(0));
                Assert.That(snapshot.QueuedRecoveryCount, Is.EqualTo(1));
                Assert.That(snapshot.RunningNormalCount, Is.EqualTo(0));
                Assert.That(snapshot.RunningSlowCount, Is.EqualTo(1));
                Assert.That(snapshot.RunningRecoveryCount, Is.EqualTo(0));
            });
        }
        finally
        {
            TryDelete(queueDbPath);
            TryDelete(mainDbPath);
        }
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
            // テスト後始末の失敗は握りつぶす。
        }
    }
}
