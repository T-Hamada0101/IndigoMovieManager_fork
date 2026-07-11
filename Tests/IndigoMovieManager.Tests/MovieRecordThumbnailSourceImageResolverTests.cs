using IndigoMovieManager;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MovieRecordThumbnailSourceImageResolverTests
{
    [Test]
    public void 管理サムネが見つかる時はsourceImageを探索しない()
    {
        string thumbnailOutPath = Path.Combine(Path.GetTempPath(), "managed-thumbnails");
        string movieFullPath = Path.Combine(Path.GetTempPath(), "sample.mp4");
        string hash = "abc";
        string managedFileName = ThumbnailPathResolver.BuildThumbnailFileName(movieFullPath, hash);
        HashSet<string> existingFileNames = new(StringComparer.OrdinalIgnoreCase)
        {
            managedFileName,
        };
        LazyThumbnailSourceImagePathResolver sourceImageResolver = new(movieFullPath);

        string actual = MainWindow.ResolveThumbnailDisplayPath(
            thumbnailOutPath,
            existingFileNames,
            movieFullPath,
            "sample",
            hash,
            "fallback.jpg",
            sourceImageResolver
        );

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.EqualTo(Path.Combine(thumbnailOutPath, managedFileName)));
            Assert.That(sourceImageResolver.ProbeCount, Is.Zero);
            Assert.That(sourceImageResolver.CacheHitCount, Is.Zero);
        });
    }

    [Test]
    public void 六用途の管理サムネ欠損時はsourceImage探索を1回だけ共有する()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager.Tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(tempRoot);
        string movieFullPath = Path.Combine(tempRoot, "sample.mp4");
        string sourceImagePath = Path.ChangeExtension(movieFullPath, ".jpg");
        File.WriteAllText(sourceImagePath, "image");

        try
        {
            LazyThumbnailSourceImagePathResolver sourceImageResolver = new(movieFullPath);
            string[] actual = new string[6];
            for (int index = 0; index < actual.Length; index++)
            {
                actual[index] = MainWindow.ResolveThumbnailDisplayPath(
                    Path.Combine(tempRoot, $"thumb-{index}"),
                    [],
                    movieFullPath,
                    "sample",
                    "abc",
                    "fallback.jpg",
                    sourceImageResolver
                );
            }

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.All.EqualTo(sourceImagePath));
                Assert.That(sourceImageResolver.ProbeCount, Is.EqualTo(1));
                Assert.That(sourceImageResolver.CacheHitCount, Is.EqualTo(5));
            });
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
