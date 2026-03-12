using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailCoordinatorWorkerProcessManagerTests
{
    [Test]
    public void ShouldDeferRestartForActiveWork_ownerが実行中ならTrueを返す()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-coordinator-worker-restart-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        DateTime nowUtc = DateTime.UtcNow;
        string owner = $"thumb-idle:test:{Guid.NewGuid():N}";

        try
        {
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = Path.Combine(Path.GetTempPath(), $"movie-{Guid.NewGuid():N}.mp4"),
                        MoviePathKey = Guid.NewGuid().ToString("N"),
                        TabIndex = 2,
                        MovieSizeBytes = 1024,
                    },
                ],
                nowUtc
            );

            List<QueueDbLeaseItem> leased = queueDbService.GetPendingAndLease(
                owner,
                takeCount: 1,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: nowUtc
            );

            Assert.That(leased.Count, Is.EqualTo(1));

            bool shouldDefer =
                ThumbnailCoordinatorWorkerProcessManager.ShouldDeferRestartForActiveWork(
                    new ThumbnailCoordinatorWorkerProcessManager.ThumbnailWorkerLaunchConfig
                    {
                        WorkerRole = ThumbnailQueueWorkerRole.Idle,
                        MainDbFullPath = mainDbPath,
                        OwnerInstanceId = owner,
                        SettingsSnapshotPath = "dummy.json",
                        SettingsVersionToken = "next",
                    }
                );

            Assert.That(shouldDefer, Is.True);
        }
        finally
        {
            TryDelete(queueDbPath);
            TryDelete(mainDbPath);
        }
    }

    [Test]
    public void ShouldDeferRestartForActiveWork_ownerが仕事を持たなければFalseを返す()
    {
        bool shouldDefer =
            ThumbnailCoordinatorWorkerProcessManager.ShouldDeferRestartForActiveWork(
                new ThumbnailCoordinatorWorkerProcessManager.ThumbnailWorkerLaunchConfig
                {
                    WorkerRole = ThumbnailQueueWorkerRole.Normal,
                    MainDbFullPath = Path.Combine(
                        Path.GetTempPath(),
                        $"imm-coordinator-worker-idle-{Guid.NewGuid():N}.wb"
                    ),
                    OwnerInstanceId = $"thumb-normal:test:{Guid.NewGuid():N}",
                    SettingsSnapshotPath = "dummy.json",
                    SettingsVersionToken = "next",
                }
            );

        Assert.That(shouldDefer, Is.False);
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
