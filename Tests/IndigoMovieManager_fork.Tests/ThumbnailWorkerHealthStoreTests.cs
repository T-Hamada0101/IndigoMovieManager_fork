using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests
{
    [TestFixture]
    public sealed class ThumbnailWorkerHealthStoreTests
    {
        [Test]
        public void LoadSnapshots_PublishedHealth_IsReadBack()
        {
            string dbPath = Path.Combine(
                Path.GetTempPath(),
                $"thumb-health-{Guid.NewGuid():N}.wb"
            );
            string owner = $"thumb-normal-test:{Guid.NewGuid():N}";
            ThumbnailWorkerHealthPublisher publisher = new(
                dbPath,
                owner,
                ThumbnailQueueWorkerRole.Normal,
                "v-test"
            );

            publisher.Publish(
                ThumbnailWorkerHealthState.Running,
                processId: 1234,
                currentPriority: "BelowNormal",
                message: "ok",
                reasonCode: ThumbnailWorkerHealthReasonCode.None,
                lastHeartbeatUtc: DateTime.UtcNow
            );

            IReadOnlyList<ThumbnailWorkerHealthSnapshot> snapshots =
                ThumbnailWorkerHealthStore.LoadSnapshots(
                    dbPath,
                    [owner],
                    TimeSpan.FromMinutes(1)
                );

            Assert.That(snapshots, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(snapshots[0].State, Is.EqualTo(ThumbnailWorkerHealthState.Running));
                Assert.That(snapshots[0].ProcessId, Is.EqualTo(1234));
                Assert.That(snapshots[0].CurrentPriority, Is.EqualTo("BelowNormal"));
                Assert.That(snapshots[0].SettingsVersionToken, Is.EqualTo("v-test"));
                Assert.That(snapshots[0].ReasonCode, Is.EqualTo(ThumbnailWorkerHealthReasonCode.None));
            });
        }

        [Test]
        public void HealthReasonResolver_DbMismatchAndDllMissing_AreClassified()
        {
            string dbMismatch = ThumbnailWorkerHealthReasonResolver.Resolve(
                ThumbnailWorkerHealthState.Exited,
                new InvalidOperationException("worker settings db mismatch: options='a' snapshot='b'")
            );
            string dllMissing = ThumbnailWorkerHealthReasonResolver.Resolve(
                ThumbnailWorkerHealthState.Exited,
                new DllNotFoundException("e_sqlite3")
            );

            Assert.Multiple(() =>
            {
                Assert.That(dbMismatch, Is.EqualTo(ThumbnailWorkerHealthReasonCode.DbMismatch));
                Assert.That(dllMissing, Is.EqualTo(ThumbnailWorkerHealthReasonCode.DllMissing));
                Assert.That(
                    ThumbnailWorkerHealthReasonResolver.ToDisplayText(dllMissing),
                    Is.EqualTo("DLL不足")
                );
            });
        }
    }
}
