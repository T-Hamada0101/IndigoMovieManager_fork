using System.IO;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailWorkerSettingsStoreTests
{
    [Test]
    public void SaveSnapshot_SameContent_KeepsVersionStable()
    {
        ThumbnailWorkerSettingsSnapshot snapshot = new()
        {
            MainDbFullPath = @"C:\work\sample.wb",
            DbName = "sample",
            ThumbFolder = @"C:\work\Thumb\sample",
            Preset = "slow",
            RequestedParallelism = 8,
            SlowLaneMinGb = 50,
            GpuDecodeEnabled = true,
            ResizeThumb = true,
            BasePollIntervalMs = 3000,
            LeaseMinutes = 5,
            CoordinatorNormalParallelismOverride = 5,
            CoordinatorIdleParallelismOverride = 3,
        };

        ThumbnailWorkerSettingsSaveResult first = ThumbnailWorkerSettingsStore.SaveSnapshot(snapshot);
        ThumbnailWorkerSettingsSaveResult second = ThumbnailWorkerSettingsStore.SaveSnapshot(snapshot);
        try
        {
            Assert.That(first.VersionToken, Is.EqualTo(second.VersionToken));

            ThumbnailWorkerSettingsSnapshot loaded = ThumbnailWorkerSettingsStore.LoadSnapshot(
                first.SnapshotFilePath
            );
            Assert.That(loaded.VersionToken, Is.EqualTo(first.VersionToken));
            Assert.That(loaded.DbName, Is.EqualTo("sample"));
            Assert.That(loaded.GpuDecodeEnabled, Is.True);
            Assert.That(loaded.CoordinatorNormalParallelismOverride, Is.EqualTo(5));
            Assert.That(loaded.CoordinatorIdleParallelismOverride, Is.EqualTo(3));
        }
        finally
        {
            TryDelete(first.SnapshotFilePath);
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
            // テストの掃除失敗は結果に影響させない。
        }
    }
}
