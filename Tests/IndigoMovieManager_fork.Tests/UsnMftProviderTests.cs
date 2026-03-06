using System.Collections.Generic;
using IndigoMovieManager.Watcher;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class UsnMftProviderTests
{
    [SetUp]
    public void SetUp()
    {
        // キャッシュ共有によるケース間干渉を防ぐ。
        UsnMftProvider.ClearCacheForTesting();
        StandardFileSystemProvider.ClearCacheForTesting();
    }

    [TearDown]
    public void TearDown()
    {
        // テスト後にキャッシュを明示クリアする。
        UsnMftProvider.ClearCacheForTesting();
        StandardFileSystemProvider.ClearCacheForTesting();
    }

    [Test]
    public void CheckAvailability_ReturnsExpectedShape()
    {
        UsnMftProvider provider = new();
        AvailabilityResult result = provider.CheckAvailability();

        if (result.CanUse)
        {
            Assert.That(result.Reason, Is.EqualTo(EverythingReasonCodes.Ok));
            return;
        }

        Assert.That(
            result.Reason.StartsWith(EverythingReasonCodes.AvailabilityErrorPrefix, StringComparison.Ordinal),
            Is.True
        );
    }

    [Test]
    public void CollectMoviePaths_WhenAvailable_UsesUsnMftReason()
    {
        UsnMftProvider provider = new();
        AvailabilityResult availability = provider.CheckAvailability();
        if (!availability.CanUse)
        {
            Assert.Ignore($"UsnMft を利用できないためスキップ: {availability.Reason}");
        }

        string root = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "direct.mp4"), "x");
            FileIndexMovieResult result = provider.CollectMoviePaths(
                new FileIndexQueryOptions
                {
                    RootPath = root,
                    IncludeSubdirectories = true,
                    CheckExt = "*.mp4",
                    ChangedSinceUtc = null,
                }
            );

            Assert.That(result.Success, Is.True);
            Assert.That(result.Reason.Contains("provider=usnmft", StringComparison.Ordinal), Is.True);
        }
        finally
        {
            DeleteTempDir(root);
        }
    }

    [Test]
    public void CollectMoviePaths_SecondCallWithinCooldown_WhenAvailable_UsesCachedIndex()
    {
        UsnMftProvider provider = new();
        AvailabilityResult availability = provider.CheckAvailability();
        if (!availability.CanUse)
        {
            Assert.Ignore($"UsnMft を利用できないためスキップ: {availability.Reason}");
        }

        string root = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "movie.mp4"), "x");

            FileIndexQueryOptions options = new()
            {
                RootPath = root,
                IncludeSubdirectories = true,
                CheckExt = "*.mp4",
                ChangedSinceUtc = null,
            };

            FileIndexMovieResult first = provider.CollectMoviePaths(options);
            FileIndexMovieResult second = provider.CollectMoviePaths(options);

            Assert.That(first.Success, Is.True);
            Assert.That(second.Success, Is.True);
            Assert.That(first.Reason.Contains("index=rebuilt", StringComparison.Ordinal), Is.True);
            Assert.That(second.Reason.Contains("index=cached", StringComparison.Ordinal), Is.True);
        }
        finally
        {
            DeleteTempDir(root);
        }
    }

    [Test]
    public void CollectMoviePaths_ManyRoots_WhenAvailable_CacheEntryCountIsLimited()
    {
        UsnMftProvider provider = new();
        AvailabilityResult availability = provider.CheckAvailability();
        if (!availability.CanUse)
        {
            Assert.Ignore($"UsnMft を利用できないためスキップ: {availability.Reason}");
        }

        List<string> roots = [];
        try
        {
            int target = UsnMftProvider.GetCacheCapacityForTesting() + 8;
            for (int i = 0; i < target; i++)
            {
                string root = CreateTempDir();
                roots.Add(root);
                File.WriteAllText(Path.Combine(root, $"movie_{i}.mp4"), "x");

                FileIndexMovieResult result = provider.CollectMoviePaths(
                    new FileIndexQueryOptions
                    {
                        RootPath = root,
                        IncludeSubdirectories = true,
                        CheckExt = "*.mp4",
                        ChangedSinceUtc = null,
                    }
                );
                Assert.That(result.Success, Is.True);
            }

            Assert.That(
                UsnMftProvider.GetCacheEntryCountForTesting(),
                Is.LessThanOrEqualTo(UsnMftProvider.GetCacheCapacityForTesting())
            );
        }
        finally
        {
            foreach (string root in roots)
            {
                DeleteTempDir(root);
            }
        }
    }

    private static string CreateTempDir()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "UsnMftProviderTests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDir(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        Directory.Delete(path, true);
    }
}
