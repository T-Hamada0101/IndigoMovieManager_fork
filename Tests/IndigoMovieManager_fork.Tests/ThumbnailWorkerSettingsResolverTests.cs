using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailWorkerSettingsResolverTests
{
    [Test]
    public void Resolve_NormalRole_UsesPresetDerivedParallelism()
    {
        ThumbnailWorkerSettingsSnapshot snapshot = new()
        {
            MainDbFullPath = @"C:\work\sample.wb",
            DbName = "sample",
            ThumbFolder = @"C:\work\Thumb\sample",
            Preset = "fast",
            RequestedParallelism = 8,
            SlowLaneMinGb = 50,
            GpuDecodeEnabled = true,
            ResizeThumb = true,
            BasePollIntervalMs = 3000,
            LeaseMinutes = 5,
            VersionToken = "v1",
        };

        ThumbnailWorkerResolvedSettings resolved = ThumbnailWorkerSettingsResolver.Resolve(
            snapshot,
            ThumbnailQueueWorkerRole.Normal,
            logicalCoreCount: 16
        );

        Assert.That(resolved.ConfiguredTotalParallelism, Is.EqualTo(8));
        Assert.That(resolved.MaxParallelism, Is.EqualTo(7));
        Assert.That(resolved.LeaseBatchSize, Is.EqualTo(7));
        Assert.That(resolved.ProcessPriorityName, Is.EqualTo("BelowNormal"));
        Assert.That(resolved.FfmpegPriorityName, Is.EqualTo("BelowNormal"));
        Assert.That(resolved.AllowDynamicScaleUp, Is.True);
    }

    [Test]
    public void Resolve_IdleRole_ForcesSingleParallelism()
    {
        ThumbnailWorkerSettingsSnapshot snapshot = new()
        {
            MainDbFullPath = @"C:\work\sample.wb",
            DbName = "sample",
            ThumbFolder = @"C:\work\Thumb\sample",
            Preset = "slow",
            RequestedParallelism = 24,
            SlowLaneMinGb = 99,
            GpuDecodeEnabled = false,
            ResizeThumb = false,
            BasePollIntervalMs = 3000,
            LeaseMinutes = 5,
            VersionToken = "v2",
        };

        ThumbnailWorkerResolvedSettings resolved = ThumbnailWorkerSettingsResolver.Resolve(
            snapshot,
            ThumbnailQueueWorkerRole.Idle,
            logicalCoreCount: 24
        );

        Assert.That(resolved.ConfiguredTotalParallelism, Is.EqualTo(2));
        Assert.That(resolved.MaxParallelism, Is.EqualTo(1));
        Assert.That(resolved.LeaseBatchSize, Is.EqualTo(1));
        Assert.That(resolved.ProcessPriorityName, Is.EqualTo("Idle"));
        Assert.That(resolved.FfmpegPriorityName, Is.EqualTo("Idle"));
        Assert.That(resolved.AllowDynamicScaleUp, Is.False);
        Assert.That(resolved.SlowLaneMinGb, Is.EqualTo(99));
    }

    [Test]
    public void Resolve_CoordinatorOverride_UsesRoleSpecificParallelism()
    {
        ThumbnailWorkerSettingsSnapshot snapshot = new()
        {
            MainDbFullPath = @"C:\work\sample.wb",
            DbName = "sample",
            ThumbFolder = @"C:\work\Thumb\sample",
            Preset = "fast",
            RequestedParallelism = 8,
            SlowLaneMinGb = 50,
            GpuDecodeEnabled = true,
            ResizeThumb = true,
            BasePollIntervalMs = 3000,
            LeaseMinutes = 5,
            CoordinatorNormalParallelismOverride = 5,
            CoordinatorIdleParallelismOverride = 3,
            VersionToken = "v3",
        };

        ThumbnailWorkerResolvedSettings normalResolved = ThumbnailWorkerSettingsResolver.Resolve(
            snapshot,
            ThumbnailQueueWorkerRole.Normal,
            logicalCoreCount: 16
        );
        ThumbnailWorkerResolvedSettings idleResolved = ThumbnailWorkerSettingsResolver.Resolve(
            snapshot,
            ThumbnailQueueWorkerRole.Idle,
            logicalCoreCount: 16
        );

        Assert.Multiple(() =>
        {
            Assert.That(normalResolved.ConfiguredTotalParallelism, Is.EqualTo(8));
            Assert.That(normalResolved.MaxParallelism, Is.EqualTo(5));
            Assert.That(normalResolved.LeaseBatchSize, Is.EqualTo(5));
            Assert.That(idleResolved.ConfiguredTotalParallelism, Is.EqualTo(8));
            Assert.That(idleResolved.MaxParallelism, Is.EqualTo(3));
            Assert.That(idleResolved.LeaseBatchSize, Is.EqualTo(1));
        });
    }
}
