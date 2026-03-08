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
    public void CollectThumbnailBodies_管理者サービス未使用でもローカル収集できる()
    {
        UsnMftProvider provider = new();
        string thumbFolder = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(thumbFolder, "movie.#123.jpg"), "x");

            FileIndexThumbnailBodyResult result = provider.CollectThumbnailBodies(thumbFolder);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Bodies.Contains("movie"), Is.True);
        }
        finally
        {
            DeleteTempDir(thumbFolder);
        }
    }

    [Test]
    public void CollectMoviePaths_AvailabilityFalseなら理由をそのまま返す()
    {
        FakeAdminFileIndexClient client = new()
        {
            Availability = new AvailabilityResult(
                false,
                $"{EverythingReasonCodes.AvailabilityErrorPrefix}AdminServiceUnavailable"
            ),
        };
        UsnMftProvider provider = new(client);

        FileIndexMovieResult result = provider.CollectMoviePaths(
            new FileIndexQueryOptions
            {
                RootPath = @"C:\movies",
                IncludeSubdirectories = true,
                CheckExt = "*.mp4",
            }
        );

        Assert.That(result.Success, Is.False);
        Assert.That(result.Reason, Is.EqualTo(client.Availability.Reason));
        Assert.That(client.CollectMoviePathsCallCount, Is.EqualTo(0));
    }

    [Test]
    public void CollectMoviePaths_ServiceTimeoutならQueryErrorへ包む()
    {
        FakeAdminFileIndexClient client = new()
        {
            Availability = new AvailabilityResult(true, EverythingReasonCodes.Ok),
            CollectMoviePathsException = new TimeoutException("timeout"),
        };
        UsnMftProvider provider = new(client);

        FileIndexMovieResult result = provider.CollectMoviePaths(
            new FileIndexQueryOptions
            {
                RootPath = @"C:\movies",
                IncludeSubdirectories = true,
                CheckExt = "*.mp4",
            }
        );

        Assert.That(result.Success, Is.False);
        Assert.That(
            result.Reason.StartsWith(
                EverythingReasonCodes.EverythingQueryErrorPrefix,
                StringComparison.Ordinal
            ),
            Is.True
        );
        Assert.That(result.Reason.Contains("TimeoutException", StringComparison.Ordinal), Is.True);
        Assert.That(client.CollectMoviePathsCallCount, Is.EqualTo(1));
    }

    [Test]
    public void CheckAvailability_InjectedClientの結果を返す()
    {
        FakeAdminFileIndexClient client = new()
        {
            Availability = new AvailabilityResult(true, EverythingReasonCodes.Ok),
        };
        UsnMftProvider provider = new(client);

        AvailabilityResult result = provider.CheckAvailability();

        Assert.That(result.CanUse, Is.True);
        Assert.That(result.Reason, Is.EqualTo(EverythingReasonCodes.Ok));
        Assert.That(client.CheckAvailabilityCallCount, Is.EqualTo(1));
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

    private sealed class FakeAdminFileIndexClient : IAdminFileIndexClient
    {
        public AvailabilityResult Availability { get; set; } =
            new(false, $"{EverythingReasonCodes.AvailabilityErrorPrefix}NotConfigured");

        public FileIndexMovieResult CollectMoviePathsResult { get; set; } =
            new(true, [], null, EverythingReasonCodes.Ok);

        public Exception? CollectMoviePathsException { get; set; }

        public int CheckAvailabilityCallCount { get; private set; }
        public int CollectMoviePathsCallCount { get; private set; }

        public AvailabilityResult CheckAvailability()
        {
            CheckAvailabilityCallCount++;
            return Availability;
        }

        public FileIndexMovieResult CollectMoviePaths(FileIndexQueryOptions options)
        {
            CollectMoviePathsCallCount++;
            if (CollectMoviePathsException != null)
            {
                throw CollectMoviePathsException;
            }

            return CollectMoviePathsResult;
        }
    }
}
