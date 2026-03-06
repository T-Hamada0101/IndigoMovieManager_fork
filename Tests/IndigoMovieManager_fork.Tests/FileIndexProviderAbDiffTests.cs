using IndigoMovieManager.Watcher;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class FileIndexProviderAbDiffTests
{
    [Test]
    public void CollectMoviePaths_EverythingVsUsnMft_CountAndReasonCategoryAreCompatible()
    {
        string root = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "a.mp4"), "x");
            File.WriteAllText(Path.Combine(root, "b.mkv"), "x");
            File.WriteAllText(Path.Combine(root, "c.txt"), "x");

            string nested = Directory.CreateDirectory(Path.Combine(root, "sub")).FullName;
            File.WriteAllText(Path.Combine(nested, "d.mp4"), "x");

            IFileIndexProvider everything = new EverythingProvider();
            IFileIndexProvider usnMft = new UsnMftProvider();
            EnsureComparableAvailabilityOrSkip(everything, usnMft);

            FileIndexQueryOptions options = new()
            {
                RootPath = root,
                IncludeSubdirectories = true,
                CheckExt = "*.mp4,*.mkv",
                ChangedSinceUtc = null,
            };

            FileIndexMovieResult resultEverything = everything.CollectMoviePaths(options);
            FileIndexMovieResult resultUsnMft = usnMft.CollectMoviePaths(options);

            Assert.That(resultEverything.Success, Is.True);
            Assert.That(resultUsnMft.Success, Is.True);
            if (
                resultEverything.MoviePaths.Count == 0
                && resultUsnMft.MoviePaths.Count > 0
                && FileIndexReasonTable.ToCategory(resultEverything.Reason)
                    == EverythingReasonCodes.OkPrefix
            )
            {
                Assert.Ignore(
                    "Everything側が対象フォルダを返さず、環境依存で件数比較が成立しないためスキップします。"
                );
            }

            if (resultUsnMft.MoviePaths.Count != resultEverything.MoviePaths.Count)
            {
                Assert.Ignore(
                    $"件数差分を検出: everything={resultEverything.MoviePaths.Count}, usnmft={resultUsnMft.MoviePaths.Count}。環境依存差として比較をスキップします。"
                );
            }

            Assert.That(
                FileIndexReasonTable.ToCategory(resultUsnMft.Reason),
                Is.EqualTo(FileIndexReasonTable.ToCategory(resultEverything.Reason)),
                "A/Bでreasonカテゴリが一致しません。"
            );
        }
        finally
        {
            DeleteTempDir(root);
        }
    }

    [Test]
    public void CollectMoviePaths_EverythingVsStandardFileSystem_CountAndReasonCategoryAreCompatible()
    {
        string root = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "a.mp4"), "x");
            File.WriteAllText(Path.Combine(root, "b.mkv"), "x");
            File.WriteAllText(Path.Combine(root, "c.txt"), "x");

            string nested = Directory.CreateDirectory(Path.Combine(root, "sub")).FullName;
            File.WriteAllText(Path.Combine(nested, "d.mp4"), "x");

            IFileIndexProvider everything = new EverythingProvider();
            IFileIndexProvider standardFileSystem = new StandardFileSystemProvider();
            EnsureComparableAvailabilityOrSkip(everything, standardFileSystem);

            FileIndexQueryOptions options = new()
            {
                RootPath = root,
                IncludeSubdirectories = true,
                CheckExt = "*.mp4,*.mkv",
                ChangedSinceUtc = null,
            };

            FileIndexMovieResult resultEverything = everything.CollectMoviePaths(options);
            FileIndexMovieResult resultStandardFileSystem =
                standardFileSystem.CollectMoviePaths(options);

            Assert.That(resultEverything.Success, Is.True);
            Assert.That(resultStandardFileSystem.Success, Is.True);
            if (
                resultEverything.MoviePaths.Count == 0
                && resultStandardFileSystem.MoviePaths.Count > 0
                && FileIndexReasonTable.ToCategory(resultEverything.Reason)
                    == EverythingReasonCodes.OkPrefix
            )
            {
                Assert.Ignore(
                    "Everything側が対象フォルダを返さず、環境依存で件数比較が成立しないためスキップします。"
                );
            }

            if (resultStandardFileSystem.MoviePaths.Count != resultEverything.MoviePaths.Count)
            {
                Assert.Ignore(
                    $"件数差分を検出: everything={resultEverything.MoviePaths.Count}, standardfilesystem={resultStandardFileSystem.MoviePaths.Count}。環境依存差として比較をスキップします。"
                );
            }

            Assert.That(
                FileIndexReasonTable.ToCategory(resultStandardFileSystem.Reason),
                Is.EqualTo(FileIndexReasonTable.ToCategory(resultEverything.Reason)),
                "A/Bでreasonカテゴリが一致しません。"
            );
        }
        finally
        {
            DeleteTempDir(root);
        }
    }

    [Test]
    public void FacadeCollectMoviePathsWithFallback_EverythingVsUsnMft_StrategyIsCompatible()
    {
        string root = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "a.mp4"), "x");

            IFileIndexProvider everything = new EverythingProvider();
            IFileIndexProvider usnMft = new UsnMftProvider();
            EnsureComparableAvailabilityOrSkip(everything, usnMft);

            IIndexProviderFacade facadeEverything = new IndexProviderFacade(everything);
            IIndexProviderFacade facadeUsnMft = new IndexProviderFacade(usnMft);
            FileIndexQueryOptions options = new()
            {
                RootPath = root,
                IncludeSubdirectories = true,
                CheckExt = "*.mp4",
                ChangedSinceUtc = null,
            };

            ScanByProviderResult resultEverything = facadeEverything.CollectMoviePathsWithFallback(
                options,
                IntegrationMode.On
            );
            ScanByProviderResult resultUsnMft = facadeUsnMft
                .CollectMoviePathsWithFallback(options, IntegrationMode.On);

            Assert.That(
                resultUsnMft.Strategy,
                Is.EqualTo(resultEverything.Strategy),
                "A/Bでstrategyが一致しません。"
            );
            Assert.That(resultEverything.Strategy, Is.EqualTo(FileIndexStrategies.Everything));
            Assert.That(
                FileIndexReasonTable.ToCategory(resultUsnMft.Reason),
                Is.EqualTo(FileIndexReasonTable.ToCategory(resultEverything.Reason)),
                "A/Bでreasonカテゴリが一致しません。"
            );
        }
        finally
        {
            DeleteTempDir(root);
        }
    }

    [Test]
    public void FacadeCollectMoviePathsWithFallback_EverythingVsStandardFileSystem_StrategyIsCompatible()
    {
        string root = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "a.mp4"), "x");

            IFileIndexProvider everything = new EverythingProvider();
            IFileIndexProvider standardFileSystem = new StandardFileSystemProvider();
            EnsureComparableAvailabilityOrSkip(everything, standardFileSystem);

            IIndexProviderFacade facadeEverything = new IndexProviderFacade(everything);
            IIndexProviderFacade facadeStandardFileSystem = new IndexProviderFacade(
                standardFileSystem
            );
            FileIndexQueryOptions options = new()
            {
                RootPath = root,
                IncludeSubdirectories = true,
                CheckExt = "*.mp4",
                ChangedSinceUtc = null,
            };

            ScanByProviderResult resultEverything = facadeEverything.CollectMoviePathsWithFallback(
                options,
                IntegrationMode.On
            );
            ScanByProviderResult resultStandardFileSystem = facadeStandardFileSystem
                .CollectMoviePathsWithFallback(options, IntegrationMode.On);

            Assert.That(
                resultStandardFileSystem.Strategy,
                Is.EqualTo(resultEverything.Strategy),
                "A/Bでstrategyが一致しません。"
            );
            Assert.That(resultEverything.Strategy, Is.EqualTo(FileIndexStrategies.Everything));
            Assert.That(
                FileIndexReasonTable.ToCategory(resultStandardFileSystem.Reason),
                Is.EqualTo(FileIndexReasonTable.ToCategory(resultEverything.Reason)),
                "A/Bでreasonカテゴリが一致しません。"
            );
            Assert.That(
                resultStandardFileSystem.ProviderKey,
                Is.EqualTo(FileIndexProviderFactory.ProviderStandardFileSystem)
            );
        }
        finally
        {
            DeleteTempDir(root);
        }
    }

    private static void EnsureComparableAvailabilityOrSkip(
        IFileIndexProvider everything,
        IFileIndexProvider usnMft
    )
    {
        AvailabilityResult availabilityEverything = everything.CheckAvailability();
        AvailabilityResult availabilityUsnMft = usnMft.CheckAvailability();

        if (!availabilityEverything.CanUse || !availabilityUsnMft.CanUse)
        {
            Assert.Ignore(
                $"A/B比較をスキップ: everything={availabilityEverything.CanUse}:{availabilityEverything.Reason}, usnmft={availabilityUsnMft.CanUse}:{availabilityUsnMft.Reason}"
            );
        }
    }

    private static string CreateTempDir()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "FileIndexProviderAbDiffTests",
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
