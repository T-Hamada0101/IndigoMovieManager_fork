using IndigoMovieManager;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailQueueSameNameImageTests
{
    [Test]
    public void TryResolveSameNameThumbnailSourceImagePath_pngがあれば画像パスを返す()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager.Tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(tempRoot);

        try
        {
            string moviePath = Path.Combine(tempRoot, "movie.mp4");
            string imagePath = Path.Combine(tempRoot, "movie.png");
            File.WriteAllBytes(moviePath, []);
            File.WriteAllBytes(imagePath, [0x01, 0x02, 0x03]);

            bool actual = ThumbnailSourceImagePathResolver.TryResolveSameNameThumbnailSourceImagePath(
                moviePath,
                out string resolvedPath
            );

            Assert.That(actual, Is.True);
            Assert.That(resolvedPath, Is.EqualTo(imagePath));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public void TryResolveSameNameThumbnailSourceImagePath_jpgをpngより優先する()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager.Tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(tempRoot);

        try
        {
            string moviePath = Path.Combine(tempRoot, "movie.mp4");
            string jpgPath = Path.Combine(tempRoot, "movie.jpg");
            string pngPath = Path.Combine(tempRoot, "movie.png");
            File.WriteAllBytes(moviePath, []);
            File.WriteAllBytes(jpgPath, [0x01]);
            File.WriteAllBytes(pngPath, [0x02]);

            bool actual = ThumbnailSourceImagePathResolver.TryResolveSameNameThumbnailSourceImagePath(
                moviePath,
                out string resolvedPath
            );

            Assert.That(actual, Is.True);
            Assert.That(resolvedPath, Is.EqualTo(jpgPath));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [TestCase(".jpg")]
    [TestCase(".jpeg")]
    [TestCase(".png")]
    public void TryResolveSameNameThumbnailSourceImagePath_対応する同名画像を従来どおり返す(
        string imageExtension
    )
    {
        string tempRoot = CreateTempRoot();

        try
        {
            string moviePath = Path.Combine(tempRoot, "movie.mp4");
            string imagePath = Path.ChangeExtension(moviePath, imageExtension);
            File.WriteAllBytes(moviePath, []);
            File.WriteAllBytes(imagePath, [0x01]);

            bool actual = ThumbnailSourceImagePathResolver.TryResolveSameNameThumbnailSourceImagePath(
                moviePath,
                out string resolvedPath
            );

            Assert.That(actual, Is.True);
            Assert.That(resolvedPath, Is.EqualTo(imagePath));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void TryResolveSameNameThumbnailSourceImagePath_全拡張子欠損ならFalseと空文字を返す()
    {
        string tempRoot = CreateTempRoot();

        try
        {
            string moviePath = Path.Combine(tempRoot, "movie.mp4");
            File.WriteAllBytes(moviePath, []);

            bool actual = ThumbnailSourceImagePathResolver.TryResolveSameNameThumbnailSourceImagePath(
                moviePath,
                out string resolvedPath
            );

            Assert.That(actual, Is.False);
            Assert.That(resolvedPath, Is.Empty);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void HasSameNameThumbnailSourceImage_画像が無ければFalseを返す()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager.Tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(tempRoot);

        try
        {
            string moviePath = Path.Combine(tempRoot, "movie.mp4");
            File.WriteAllBytes(moviePath, []);

            bool actual = MainWindow.HasSameNameThumbnailSourceImage(moviePath);

            Assert.That(actual, Is.False);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string CreateTempRoot()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager.Tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(tempRoot);
        return tempRoot;
    }
}
