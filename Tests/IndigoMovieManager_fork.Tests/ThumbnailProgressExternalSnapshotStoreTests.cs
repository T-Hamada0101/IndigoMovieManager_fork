using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests
{
    [TestFixture]
    public sealed class ThumbnailProgressExternalSnapshotStoreTests
    {
        [Test]
        public void CreateMergedSnapshot_WorkerFilesAndLocalLogs_AreMerged()
        {
            string dbPath = Path.Combine(
                Path.GetTempPath(),
                $"thumb-progress-{Guid.NewGuid():N}.wb"
            );
            string normalOwner = $"thumb-normal-test:{Guid.NewGuid():N}";
            string idleOwner = $"thumb-idle-test:{Guid.NewGuid():N}";

            using ThumbnailProgressExternalSnapshotPublisher normalPublisher = new(
                dbPath,
                normalOwner
            );
            using ThumbnailProgressExternalSnapshotPublisher idlePublisher = new(dbPath, idleOwner);

            ThumbnailProgressRuntime normalRuntime = new();
            normalRuntime.UpdateSessionProgress(3, 10, 2, 6);
            normalRuntime.MarkJobStarted(
                new QueueObj
                {
                    MovieId = 10,
                    MovieFullPath = @"D:\movies\normal.mp4",
                    Tabindex = 3,
                    MovieSizeBytes = 100,
                }
            );
            normalPublisher.Publish(normalRuntime.CreateSnapshot(), force: true);

            ThumbnailProgressRuntime idleRuntime = new();
            idleRuntime.UpdateSessionProgress(1, 2, 1, 1);
            idleRuntime.MarkJobStarted(
                new QueueObj
                {
                    MovieFullPath = @"D:\movies\slow.mp4",
                    Tabindex = 0,
                    MovieSizeBytes = 60L * 1024 * 1024 * 1024,
                }
            );
            idlePublisher.Publish(idleRuntime.CreateSnapshot(), force: true);

            ThumbnailProgressRuntime localRuntime = new();
            localRuntime.RecordEnqueue(
                new QueueObj
                {
                    MovieFullPath = @"D:\movies\queued.mp4",
                    Tabindex = 0,
                }
            );
            localRuntime.UpdateSessionProgress(0, 0, 0, 7);

            ThumbnailProgressRuntimeSnapshot merged =
                ThumbnailProgressExternalSnapshotStore.CreateMergedSnapshot(
                    dbPath,
                    localRuntime.CreateSnapshot(),
                    [normalOwner, idleOwner],
                    TimeSpan.FromMinutes(1)
                );

            Assert.Multiple(() =>
            {
                Assert.That(merged.SchemaVersion, Is.EqualTo(1));
                Assert.That(merged.SessionCompletedCount, Is.EqualTo(4));
                Assert.That(merged.SessionTotalCount, Is.EqualTo(12));
                Assert.That(merged.CurrentParallelism, Is.EqualTo(3));
                Assert.That(merged.ConfiguredParallelism, Is.EqualTo(7));
                Assert.That(merged.EnqueueLogs.Count, Is.EqualTo(1));
                Assert.That(merged.EnqueueLogs[0], Is.EqualTo("queued.mp4"));
                Assert.That(merged.ActiveWorkers.Count, Is.EqualTo(2));
                Assert.That(merged.WaitingWorkers.Count, Is.EqualTo(5));
                Assert.That(merged.ActiveWorkers.Any(x => x.WorkerLabel == "ゆっくり"), Is.True);
                Assert.That(
                    merged.ActiveWorkers.Any(x => x.DisplayMovieName.Contains("normal")),
                    Is.True
                );
                Assert.That(
                    merged.ActiveWorkers.All(x => x.MainDbFullPath == dbPath),
                    Is.True
                );
                Assert.That(
                    merged.ActiveWorkers.Any(x =>
                        x.MovieId == 10
                        && x.TabIndex == 3
                        && x.OwnerInstanceId == normalOwner
                    ),
                    Is.True
                );
                Assert.That(
                    merged.ActiveWorkers.Any(x =>
                        x.OwnerInstanceId == normalOwner
                        && x.WorkerRole == ThumbnailProgressWorkerRole.Normal
                        && x.State == ThumbnailProgressSnapshotState.Started
                    ),
                    Is.True
                );
                Assert.That(
                    merged.ActiveWorkers.Any(x =>
                        x.OwnerInstanceId == idleOwner
                        && x.WorkerRole == ThumbnailProgressWorkerRole.Idle
                    ),
                    Is.True
                );
            });
        }
    }
}
