using IndigoMovieManager;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MainWindowDeleteActionTests
{
    [Test]
    public void PartitionCurrentPlayerMovieDeleteRecords_再生中だけ保留し他動画は即時へ分ける()
    {
        MovieRecords playing = new() { Movie_Id = 1, Movie_Path = @"D:\Movies\playing.mp4" };
        MovieRecords other = new() { Movie_Id = 2, Movie_Path = @"D:\Movies\other.mp4" };

        (MovieRecords[] pending, MovieRecords[] immediate) =
            MainWindow.PartitionCurrentPlayerMovieDeleteRecords(
                [playing, other],
                @"d:\movies\PLAYING.mp4"
            );

        Assert.Multiple(() =>
        {
            Assert.That(pending, Is.EqualTo(new[] { playing }));
            Assert.That(immediate, Is.EqualTo(new[] { other }));
        });
    }

    [Test]
    public void PartitionCurrentPlayerMovieDeleteRecords_再生パスなしは全件即時へ返す()
    {
        MovieRecords first = new() { Movie_Id = 1, Movie_Path = @"D:\Movies\first.mp4" };
        MovieRecords second = new() { Movie_Id = 2, Movie_Path = @"D:\Movies\second.mp4" };

        (MovieRecords[] pending, MovieRecords[] immediate) =
            MainWindow.PartitionCurrentPlayerMovieDeleteRecords([first, second], "");

        Assert.Multiple(() =>
        {
            Assert.That(pending, Is.Empty);
            Assert.That(immediate, Is.EqualTo(new[] { first, second }));
        });
    }

    [Test]
    public void TryDeleteThumbnailFile_使用中ファイルはfalseで返して落ちない()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"imm-delete-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        string targetPath = Path.Combine(tempRoot, "locked-thumb.jpg");
        File.WriteAllText(targetPath, "lock");

        try
        {
            using FileStream lockStream = new(
                targetPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None
            );

            bool deleted = MainWindow.TryDeleteThumbnailFile(
                targetPath,
                sendToRecycleBin: false,
                out string failureReason
            );

            Assert.Multiple(() =>
            {
                Assert.That(deleted, Is.False);
                Assert.That(failureReason, Is.Not.Empty);
                Assert.That(File.Exists(targetPath), Is.True);
            });
        }
        finally
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public void TryDeleteThumbnailFile_解放後ファイルは削除できる()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"imm-delete-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        string targetPath = Path.Combine(tempRoot, "thumb.jpg");
        File.WriteAllText(targetPath, "delete");

        try
        {
            bool deleted = MainWindow.TryDeleteThumbnailFile(
                targetPath,
                sendToRecycleBin: false,
                out string failureReason
            );

            Assert.Multiple(() =>
            {
                Assert.That(deleted, Is.True);
                Assert.That(failureReason, Is.Empty);
                Assert.That(File.Exists(targetPath), Is.False);
            });
        }
        finally
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
