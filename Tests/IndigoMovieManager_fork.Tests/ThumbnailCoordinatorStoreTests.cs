using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests
{
    [TestFixture]
    public sealed class ThumbnailCoordinatorStoreTests
    {
        [Test]
        public void ControlStore_SaveAndLoadLatest_ReadsBackPublishedSnapshot()
        {
            string dbPath = Path.Combine(
                Path.GetTempPath(),
                $"thumb-coordinator-control-{Guid.NewGuid():N}.wb"
            );
            string owner = $"thumb-coordinator-test:{Guid.NewGuid():N}";
            DateTime nowUtc = DateTime.UtcNow;

            ThumbnailCoordinatorControlStore.Save(
                new ThumbnailCoordinatorControlSnapshot
                {
                    MainDbFullPath = dbPath,
                    DbName = "test-db",
                    OwnerInstanceId = owner,
                    CoordinatorState = ThumbnailCoordinatorState.Running,
                    RequestedParallelism = 6,
                    TemporaryParallelismDelta = 1,
                    EffectiveParallelism = 5,
                    LargeMovieThresholdGb = 50,
                    GpuDecodeEnabled = true,
                    OperationMode = ThumbnailCoordinatorOperationMode.NormalFirst,
                    FastSlotCount = 4,
                    SlowSlotCount = 1,
                    ActiveWorkerCount = 5,
                    ActiveFfmpegCount = 1,
                    QueuedNormalCount = 3,
                    QueuedSlowCount = 2,
                    QueuedRecoveryCount = 1,
                    RunningNormalCount = 2,
                    RunningSlowCount = 1,
                    RunningRecoveryCount = 1,
                    DemandNormalCount = 5,
                    DemandSlowCount = 3,
                    DemandRecoveryCount = 2,
                    WeightedNormalDemand = 5,
                    WeightedSlowDemand = 7,
                    SlowSlotMinimum = 1,
                    SlowSlotMaximum = 4,
                    DecisionCategory = ThumbnailCoordinatorDecisionCategory.DemandBiased,
                    DecisionSummary = "通常優先/需要追従: 需要 n/s/r=5/3/2。重み n/s=5/7。slow=3 (比率=3, 範囲=1-4)",
                    Reason = "ok",
                    DecisionHistory =
                    [
                        new ThumbnailCoordinatorDecisionHistoryEntry
                        {
                            UpdatedAtUtc = nowUtc.AddSeconds(-30),
                            OperationMode = ThumbnailCoordinatorOperationMode.NormalFirst,
                            DecisionCategory = ThumbnailCoordinatorDecisionCategory.Minimum,
                            DecisionSummary = "通常優先/最小維持: slow 需要が軽いため最小 slow=1 を維持",
                            FastSlotCount = 5,
                            SlowSlotCount = 1,
                        },
                        new ThumbnailCoordinatorDecisionHistoryEntry
                        {
                            UpdatedAtUtc = nowUtc,
                            OperationMode = ThumbnailCoordinatorOperationMode.NormalFirst,
                            DecisionCategory = ThumbnailCoordinatorDecisionCategory.DemandBiased,
                            DecisionSummary = "通常優先/需要追従: 需要 n/s/r=5/3/2。重み n/s=5/7。slow=3 (比率=3, 範囲=1-4)",
                            FastSlotCount = 3,
                            SlowSlotCount = 3,
                        },
                    ],
                    UpdatedAtUtc = nowUtc,
                }
            );

            ThumbnailCoordinatorControlSnapshot snapshot =
                ThumbnailCoordinatorControlStore.LoadLatest(
                    dbPath,
                    owner,
                    TimeSpan.FromMinutes(1)
                );

            Assert.That(snapshot, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.CoordinatorState, Is.EqualTo(ThumbnailCoordinatorState.Running));
                Assert.That(snapshot.RequestedParallelism, Is.EqualTo(6));
                Assert.That(snapshot.EffectiveParallelism, Is.EqualTo(5));
                Assert.That(snapshot.OperationMode, Is.EqualTo(ThumbnailCoordinatorOperationMode.NormalFirst));
                Assert.That(snapshot.GpuDecodeEnabled, Is.True);
                Assert.That(snapshot.QueuedSlowCount, Is.EqualTo(2));
                Assert.That(snapshot.RunningRecoveryCount, Is.EqualTo(1));
                Assert.That(snapshot.WeightedSlowDemand, Is.EqualTo(7));
                Assert.That(snapshot.SlowSlotMaximum, Is.EqualTo(4));
                Assert.That(snapshot.DecisionCategory, Is.EqualTo(ThumbnailCoordinatorDecisionCategory.DemandBiased));
                Assert.That(snapshot.DecisionSummary, Does.Contain("通常優先"));
                Assert.That(snapshot.DecisionHistory, Has.Count.EqualTo(2));
                Assert.That(snapshot.DecisionHistory[0].DecisionCategory, Is.EqualTo(ThumbnailCoordinatorDecisionCategory.Minimum));
                Assert.That(snapshot.DecisionHistory[1].SlowSlotCount, Is.EqualTo(3));
            });
        }

        [Test]
        public void CommandStore_SaveAndLoadLatest_ReadsBackPublishedSnapshot()
        {
            string dbPath = Path.Combine(
                Path.GetTempPath(),
                $"thumb-coordinator-command-{Guid.NewGuid():N}.wb"
            );
            string owner = $"thumb-coordinator-test:{Guid.NewGuid():N}";
            DateTime nowUtc = DateTime.UtcNow;

            ThumbnailCoordinatorCommandStore.Save(
                new ThumbnailCoordinatorCommandSnapshot
                {
                    MainDbFullPath = dbPath,
                    DbName = "test-db",
                    OwnerInstanceId = owner,
                    RequestedParallelism = 8,
                    TemporaryParallelismDelta = -1,
                    LargeMovieThresholdGb = 80,
                    GpuDecodeEnabled = false,
                    OperationMode = ThumbnailCoordinatorOperationMode.PowerSave,
                    IssuedBy = "unit-test",
                    IssuedAtUtc = nowUtc,
                }
            );

            ThumbnailCoordinatorCommandSnapshot snapshot =
                ThumbnailCoordinatorCommandStore.LoadLatest(
                    dbPath,
                    owner,
                    TimeSpan.FromMinutes(1)
                );

            Assert.That(snapshot, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.RequestedParallelism, Is.EqualTo(8));
                Assert.That(snapshot.TemporaryParallelismDelta, Is.EqualTo(-1));
                Assert.That(snapshot.LargeMovieThresholdGb, Is.EqualTo(80));
                Assert.That(snapshot.OperationMode, Is.EqualTo(ThumbnailCoordinatorOperationMode.PowerSave));
                Assert.That(snapshot.IssuedBy, Is.EqualTo("unit-test"));
            });
        }
    }
}
