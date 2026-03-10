using IndigoMovieManager.Thumbnail.FailureDb;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailFailureDebugDbTests
{
    [Test]
    public void ResolveFailureDbPath_拡張子はFailureDebugImmになる()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-main-{Guid.NewGuid():N}.wb"
        );

        string resolved = ThumbnailFailureDebugDbPathResolver.ResolveFailureDbPath(mainDbPath);

        Assert.That(Path.GetFileName(resolved), Does.EndWith(".failure-debug.imm"));
    }

    [Test]
    public void InsertFailureRecord_GetFailureRecordsで新しい順に返る()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-records-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDebugDbService service = new(mainDbPath);
        string dbPath = service.FailureDbFullPath;

        try
        {
            _ = service.InsertFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\older.mkv",
                    PanelType = "grid",
                    MovieSizeBytes = 111,
                    Duration = 12.3,
                    Reason = "older",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    AttemptCount = 1,
                    OccurredAtUtc = new DateTime(2026, 3, 10, 1, 0, 0, DateTimeKind.Utc),
                    UpdatedAtUtc = new DateTime(2026, 3, 10, 1, 0, 1, DateTimeKind.Utc),
                    EngineId = "autogen",
                    QueueStatus = "Pending",
                }
            );
            _ = service.InsertFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\newer.mkv",
                    PanelType = "grid",
                    MovieSizeBytes = 222,
                    Duration = 45.6,
                    Reason = "newer",
                    FailureKind = ThumbnailFailureKind.TransientDecodeFailure,
                    AttemptCount = 2,
                    OccurredAtUtc = new DateTime(2026, 3, 10, 2, 0, 0, DateTimeKind.Utc),
                    UpdatedAtUtc = new DateTime(2026, 3, 10, 2, 0, 1, DateTimeKind.Utc),
                    EngineId = "autogen",
                    QueueStatus = "Failed",
                    LastError = "no frames decoded",
                    ExtraJson = "{\"attempt\":\"original\"}",
                }
            );

            List<ThumbnailFailureRecord> records = service.GetFailureRecords();

            Assert.That(records.Count, Is.EqualTo(2));
            Assert.That(records[0].MoviePath, Is.EqualTo(@"E:\movies\newer.mkv"));
            Assert.That(records[0].MoviePathKey, Is.EqualTo(ThumbnailFailureDebugDbPathResolver.CreateMoviePathKey(@"E:\movies\newer.mkv")));
            Assert.That(records[0].FailureKind, Is.EqualTo(ThumbnailFailureKind.TransientDecodeFailure));
            Assert.That(records[0].LastError, Is.EqualTo("no frames decoded"));
            Assert.That(records[1].MoviePath, Is.EqualTo(@"E:\movies\older.mkv"));
        }
        finally
        {
            TryDeleteSqliteFamily(dbPath);
        }
    }

    [Test]
    public void InsertFailureRecord_DbNameとMainDbFullPathは既定値で補完される()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            "FailureDbTests",
            $"sample-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDebugDbService service = new(mainDbPath);
        string dbPath = service.FailureDbFullPath;

        try
        {
            _ = service.InsertFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\sample.mkv",
                    Reason = "sample",
                    FailureKind = ThumbnailFailureKind.IndexCorruption,
                }
            );

            ThumbnailFailureRecord record = service.GetFailureRecords().Single();

            Assert.That(record.DbName, Is.EqualTo(Path.GetFileNameWithoutExtension(mainDbPath)));
            Assert.That(record.MainDbFullPath, Is.EqualTo(mainDbPath));
            Assert.That(record.MainDbPathHash, Is.EqualTo(QueueDbPathResolver.GetMainDbPathHash8(mainDbPath)));
        }
        finally
        {
            TryDeleteSqliteFamily(dbPath);
        }
    }

    private static void TryDeleteSqliteFamily(string dbPath)
    {
        TryDeleteFile(dbPath);
        TryDeleteFile(dbPath + "-wal");
        TryDeleteFile(dbPath + "-shm");
    }

    private static void TryDeleteFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // テスト後の掃除失敗は握りつぶす。
        }
    }
}
