using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public class QueueDbPathResolverTests
{
    [Test]
    public void ResolveQueueDbPath_拡張子はQueueImmになる()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-ext-{Guid.NewGuid():N}.wb"
        );

        string resolved = QueueDbPathResolver.ResolveQueueDbPath(mainDbPath);
        string fileName = Path.GetFileName(resolved);

        Assert.That(fileName, Does.EndWith(".queue.imm"));
    }

    [Test]
    public void CreateMoviePathKey_拡張長パス接頭辞ありでも同一キーを返す()
    {
        // 同じ実体パスを通常表記と \\?\ 表記で用意する。
        string normalPath = Path.Combine(
            Path.GetTempPath(),
            "imm_queue_key_test",
            "movie.mp4"
        );
        string extendedPath = $@"\\?\{normalPath}";

        string normalKey = QueueDbPathResolver.CreateMoviePathKey(normalPath);
        string extendedKey = QueueDbPathResolver.CreateMoviePathKey(extendedPath);

        Assert.That(extendedKey, Is.EqualTo(normalKey));
    }

    [Test]
    public void Upsert_同一動画の表記ゆれは1行に集約される()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);

        string queueDbPath = queueDbService.QueueDbFullPath;
        string normalPath = Path.Combine(
            Path.GetTempPath(),
            "imm_queue_upsert_test",
            $"movie-{Guid.NewGuid():N}.mp4"
        );
        string extendedPath = $@"\\?\{normalPath}";

        try
        {
            // 同じ動画を表記違いで2回投入し、QueueDB上で1行に保たれることを確認する。
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = normalPath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(normalPath),
                        TabIndex = 2,
                    },
                    new QueueDbUpsertItem
                    {
                        MoviePath = extendedPath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(extendedPath),
                        TabIndex = 2,
                    },
                ],
                DateTime.UtcNow
            );

            using SQLiteConnection connection = new($"Data Source={queueDbPath}");
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT COUNT(1)
FROM ThumbnailQueue
WHERE TabIndex = @TabIndex;";
            command.Parameters.AddWithValue("@TabIndex", 2);
            int count = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);

            Assert.That(count, Is.EqualTo(1));
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    [Test]
    public void UpsertAndLease_動画サイズを保持して取得できる()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-size-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string moviePath = Path.Combine(
            Path.GetTempPath(),
            "imm_queue_size_test",
            $"movie-{Guid.NewGuid():N}.mkv"
        );
        long expectedSizeBytes = 72L * 1024 * 1024 * 1024;

        try
        {
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = moviePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath),
                        TabIndex = 0,
                        MovieSizeBytes = expectedSizeBytes,
                    },
                ],
                DateTime.UtcNow
            );

            List<QueueDbLeaseItem> leased = queueDbService.GetPendingAndLease(
                "TEST-OWNER",
                takeCount: 1,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: DateTime.UtcNow
            );
            Assert.That(leased.Count, Is.EqualTo(1));
            Assert.That(leased[0].MovieSizeBytes, Is.EqualTo(expectedSizeBytes));

            using SQLiteConnection connection = new($"Data Source={queueDbPath}");
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT MovieSizeBytes
FROM ThumbnailQueue
WHERE TabIndex = @TabIndex
LIMIT 1;";
            command.Parameters.AddWithValue("@TabIndex", 0);
            long stored = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            Assert.That(stored, Is.EqualTo(expectedSizeBytes));
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    [Test]
    public void EnsureInitialized_旧QueueDBでもMovieSizeBytes列を自動追加する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-legacy-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string moviePath = Path.Combine(
            Path.GetTempPath(),
            "imm_queue_legacy_test",
            $"movie-{Guid.NewGuid():N}.mp4"
        );

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(queueDbPath) ?? "");
            using (SQLiteConnection legacyConnection = new($"Data Source={queueDbPath}"))
            {
                legacyConnection.Open();
                using SQLiteCommand createTableCommand = legacyConnection.CreateCommand();
            // 旧形式: MovieSizeBytes 列なし。
                createTableCommand.CommandText = @"
CREATE TABLE IF NOT EXISTS ThumbnailQueue (
    QueueId INTEGER PRIMARY KEY AUTOINCREMENT,
    MainDbPathHash TEXT NOT NULL,
    MoviePath TEXT NOT NULL,
    MoviePathKey TEXT NOT NULL,
    TabIndex INTEGER NOT NULL,
    ThumbPanelPos INTEGER,
    ThumbTimePos INTEGER,
    Status INTEGER NOT NULL DEFAULT 0,
    AttemptCount INTEGER NOT NULL DEFAULT 0,
    LastError TEXT NOT NULL DEFAULT '',
    OwnerInstanceId TEXT NOT NULL DEFAULT '',
    LeaseUntilUtc TEXT NOT NULL DEFAULT '',
    CreatedAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    UpdatedAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    UNIQUE (MainDbPathHash, MoviePathKey, TabIndex)
);";
                createTableCommand.ExecuteNonQuery();
            }

            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = moviePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath),
                        TabIndex = 1,
                        MovieSizeBytes = 123456789,
                    },
                ],
                DateTime.UtcNow
            );

            using SQLiteConnection verifyConnection = new($"Data Source={queueDbPath}");
            verifyConnection.Open();
            using SQLiteCommand columnCheckCommand = verifyConnection.CreateCommand();
            columnCheckCommand.CommandText = @"
SELECT COUNT(1)
FROM pragma_table_info('ThumbnailQueue')
WHERE name = 'MovieSizeBytes';";
            int columnCount = Convert.ToInt32(
                columnCheckCommand.ExecuteScalar(),
                CultureInfo.InvariantCulture
            );
            Assert.That(columnCount, Is.EqualTo(1));
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    [Test]
    public void DeleteMovieEntries_同一動画の全タブキューを削除する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-delete-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string moviePath = Path.Combine(
            Path.GetTempPath(),
            "imm_queue_delete_test",
            $"movie-{Guid.NewGuid():N}.mp4"
        );

        try
        {
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = moviePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath),
                        TabIndex = 0,
                    },
                    new QueueDbUpsertItem
                    {
                        MoviePath = moviePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath),
                        TabIndex = 2,
                    },
                ],
                DateTime.UtcNow
            );

            int deleted = queueDbService.DeleteMovieEntries(moviePath);
            Assert.That(deleted, Is.EqualTo(2));

            using SQLiteConnection connection = new($"Data Source={queueDbPath}");
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT COUNT(1)
FROM ThumbnailQueue
WHERE MainDbPathHash = @MainDbPathHash;";
            command.Parameters.AddWithValue("@MainDbPathHash", queueDbService.MainDbPathHash);
            int remaining = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);

            Assert.That(remaining, Is.EqualTo(0));
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    [Test]
    public void GetFailedItems_Failedのみを更新時刻降順で返す()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-failed-list-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        DateTime nowUtc = DateTime.UtcNow;

        string movieDonePath = Path.Combine(Path.GetTempPath(), $"movie-done-{Guid.NewGuid():N}.mp4");
        string movieFailedOldPath = Path.Combine(
            Path.GetTempPath(),
            $"movie-failed-old-{Guid.NewGuid():N}.mp4"
        );
        string movieFailedNewPath = Path.Combine(
            Path.GetTempPath(),
            $"movie-failed-new-{Guid.NewGuid():N}.mp4"
        );

        try
        {
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = movieDonePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(movieDonePath),
                        TabIndex = 0,
                    },
                    new QueueDbUpsertItem
                    {
                        MoviePath = movieFailedOldPath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(movieFailedOldPath),
                        TabIndex = 0,
                    },
                    new QueueDbUpsertItem
                    {
                        MoviePath = movieFailedNewPath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(movieFailedNewPath),
                        TabIndex = 0,
                    },
                ],
                nowUtc
            );

            const string owner = "FAILED-LIST-TEST-OWNER";
            List<QueueDbLeaseItem> leased = queueDbService.GetPendingAndLease(
                owner,
                takeCount: 10,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: nowUtc
            );
            Assert.That(leased.Count, Is.EqualTo(3));

            QueueDbLeaseItem doneLease = leased.Single(x =>
                string.Equals(x.MoviePath, movieDonePath, StringComparison.OrdinalIgnoreCase)
            );
            QueueDbLeaseItem failedOldLease = leased.Single(x =>
                string.Equals(x.MoviePath, movieFailedOldPath, StringComparison.OrdinalIgnoreCase)
            );
            QueueDbLeaseItem failedNewLease = leased.Single(x =>
                string.Equals(x.MoviePath, movieFailedNewPath, StringComparison.OrdinalIgnoreCase)
            );

            _ = queueDbService.UpdateStatus(
                doneLease.QueueId,
                owner,
                ThumbnailQueueStatus.Done,
                nowUtc.AddSeconds(1)
            );
            _ = queueDbService.UpdateStatus(
                failedOldLease.QueueId,
                owner,
                ThumbnailQueueStatus.Failed,
                nowUtc.AddSeconds(2),
                "old error"
            );
            _ = queueDbService.UpdateStatus(
                failedNewLease.QueueId,
                owner,
                ThumbnailQueueStatus.Failed,
                nowUtc.AddSeconds(3),
                "new error"
            );

            List<QueueDbFailedItem> failedItems = queueDbService.GetFailedItems();

            Assert.That(failedItems.Count, Is.EqualTo(2));
            Assert.That(failedItems[0].MoviePath, Is.EqualTo(movieFailedNewPath));
            Assert.That(failedItems[1].MoviePath, Is.EqualTo(movieFailedOldPath));
            Assert.That(failedItems.All(x => x.Status == ThumbnailQueueStatus.Failed), Is.True);
            Assert.That(failedItems[0].LastError, Is.EqualTo("new error"));
            Assert.That(failedItems[1].LastError, Is.EqualTo("old error"));
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    [Test]
    public void HasRecoveryQueueDemand_再試行Pendingと自OwnerProcessingだけ検知する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-recovery-demand-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        DateTime nowUtc = DateTime.UtcNow;
        string moviePath = Path.Combine(Path.GetTempPath(), $"movie-recovery-{Guid.NewGuid():N}.mp4");
        string owner = "RECOVERY-DEMAND-OWNER";

        try
        {
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = moviePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath),
                        TabIndex = 0,
                    },
                ],
                nowUtc
            );

            Assert.That(queueDbService.HasRecoveryQueueDemand(owner), Is.False);

            List<QueueDbLeaseItem> leased = queueDbService.GetPendingAndLease(
                owner,
                takeCount: 1,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: nowUtc
            );
            Assert.That(leased.Count, Is.EqualTo(1));

            _ = queueDbService.UpdateStatus(
                leased[0].QueueId,
                owner,
                ThumbnailQueueStatus.Pending,
                nowUtc.AddSeconds(1),
                "retry",
                incrementAttemptCount: true
            );

            Assert.That(queueDbService.HasRecoveryQueueDemand(owner), Is.True);

            List<QueueDbLeaseItem> recoveryLease = queueDbService.GetPendingAndLease(
                owner,
                takeCount: 1,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: nowUtc.AddSeconds(2),
                minAttemptCount: 1
            );
            Assert.That(recoveryLease.Count, Is.EqualTo(1));
            Assert.That(recoveryLease[0].AttemptCount, Is.EqualTo(1));
            Assert.That(queueDbService.HasRecoveryQueueDemand(owner), Is.True);

            _ = queueDbService.UpdateStatus(
                recoveryLease[0].QueueId,
                owner,
                ThumbnailQueueStatus.Done,
                nowUtc.AddSeconds(3)
            );

            Assert.That(queueDbService.HasRecoveryQueueDemand(owner), Is.False);
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    [Test]
    public void GetPendingAndLease_minAttemptCount指定時は再試行ジョブだけ取得する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-recovery-filter-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        DateTime nowUtc = DateTime.UtcNow;
        string normalMoviePath = Path.Combine(Path.GetTempPath(), $"movie-normal-{Guid.NewGuid():N}.mp4");
        string recoveryMoviePath = Path.Combine(Path.GetTempPath(), $"movie-recovery-{Guid.NewGuid():N}.mp4");
        string owner = "RECOVERY-FILTER-OWNER";

        try
        {
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = normalMoviePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(normalMoviePath),
                        TabIndex = 0,
                    },
                    new QueueDbUpsertItem
                    {
                        MoviePath = recoveryMoviePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(recoveryMoviePath),
                        TabIndex = 0,
                    },
                ],
                nowUtc
            );

            List<QueueDbLeaseItem> firstLease = queueDbService.GetPendingAndLease(
                owner,
                takeCount: 2,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: nowUtc
            );
            Assert.That(firstLease.Count, Is.EqualTo(2));

            QueueDbLeaseItem recoveryLease = firstLease.Single(x =>
                string.Equals(x.MoviePath, recoveryMoviePath, StringComparison.OrdinalIgnoreCase)
            );
            QueueDbLeaseItem normalLease = firstLease.Single(x =>
                string.Equals(x.MoviePath, normalMoviePath, StringComparison.OrdinalIgnoreCase)
            );

            _ = queueDbService.UpdateStatus(
                recoveryLease.QueueId,
                owner,
                ThumbnailQueueStatus.Pending,
                nowUtc.AddSeconds(1),
                "retry",
                incrementAttemptCount: true
            );
            _ = queueDbService.UpdateStatus(
                normalLease.QueueId,
                owner,
                ThumbnailQueueStatus.Done,
                nowUtc.AddSeconds(1)
            );

            List<QueueDbLeaseItem> onlyRecovery = queueDbService.GetPendingAndLease(
                owner,
                takeCount: 5,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: nowUtc.AddSeconds(2),
                minAttemptCount: 1
            );

            Assert.That(onlyRecovery.Count, Is.EqualTo(1));
            Assert.That(onlyRecovery[0].MoviePath, Is.EqualTo(recoveryMoviePath));
            Assert.That(onlyRecovery[0].AttemptCount, Is.EqualTo(1));
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    [Test]
    public void Upsert_二度失敗済み動画は再投入されてもRecovery対象を維持する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-recovery-keep-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        DateTime nowUtc = DateTime.UtcNow;
        string moviePath = Path.Combine(Path.GetTempPath(), $"movie-recovery-keep-{Guid.NewGuid():N}.mp4");
        string owner = "RECOVERY-KEEP-OWNER";

        try
        {
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = moviePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath),
                        TabIndex = 0,
                    },
                ],
                nowUtc
            );

            for (int attempt = 0; attempt < 2; attempt++)
            {
                List<QueueDbLeaseItem> leased = queueDbService.GetPendingAndLease(
                    owner,
                    takeCount: 1,
                    leaseDuration: TimeSpan.FromMinutes(5),
                    utcNow: nowUtc.AddSeconds(attempt)
                );
                Assert.That(leased.Count, Is.EqualTo(1));

                _ = queueDbService.UpdateStatus(
                    leased[0].QueueId,
                    owner,
                    ThumbnailQueueStatus.Pending,
                    nowUtc.AddSeconds(attempt + 1),
                    $"retry-{attempt + 1}",
                    incrementAttemptCount: true
                );
            }

            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = moviePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath),
                        TabIndex = 0,
                    },
                ],
                nowUtc.AddSeconds(3)
            );

            List<QueueDbLeaseItem> recoveryLease = queueDbService.GetPendingAndLease(
                owner,
                takeCount: 1,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: nowUtc.AddSeconds(4),
                minAttemptCount: 1
            );

            Assert.That(recoveryLease.Count, Is.EqualTo(1));
            Assert.That(recoveryLease[0].AttemptCount, Is.EqualTo(2));
            Assert.That(queueDbService.HasRecoveryQueueDemand(owner), Is.True);
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    [Test]
    public void ResetFailedToPending_二度失敗済み動画はRecovery対象を維持する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-reset-recovery-keep-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        DateTime nowUtc = DateTime.UtcNow;
        string moviePath = Path.Combine(Path.GetTempPath(), $"movie-reset-recovery-{Guid.NewGuid():N}.mp4");
        string owner = "RESET-RECOVERY-OWNER";

        try
        {
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = moviePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath),
                        TabIndex = 0,
                    },
                ],
                nowUtc
            );

            for (int attempt = 0; attempt < 5; attempt++)
            {
                List<QueueDbLeaseItem> leased = queueDbService.GetPendingAndLease(
                    owner,
                    takeCount: 1,
                    leaseDuration: TimeSpan.FromMinutes(5),
                    utcNow: nowUtc.AddSeconds(attempt)
                );
                Assert.That(leased.Count, Is.EqualTo(1));

                ThumbnailQueueStatus status = attempt == 4
                    ? ThumbnailQueueStatus.Failed
                    : ThumbnailQueueStatus.Pending;
                bool incrementAttemptCount = attempt < 4;
                _ = queueDbService.UpdateStatus(
                    leased[0].QueueId,
                    owner,
                    status,
                    nowUtc.AddSeconds(attempt + 1),
                    $"retry-{attempt + 1}",
                    incrementAttemptCount: incrementAttemptCount
                );
            }

            int resetCount = queueDbService.ResetFailedToPending(nowUtc.AddSeconds(10));
            Assert.That(resetCount, Is.EqualTo(1));

            List<QueueDbLeaseItem> recoveryLease = queueDbService.GetPendingAndLease(
                owner,
                takeCount: 1,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: nowUtc.AddSeconds(11),
                minAttemptCount: 1
            );

            Assert.That(recoveryLease.Count, Is.EqualTo(1));
            Assert.That(recoveryLease[0].AttemptCount, Is.EqualTo(4));
            Assert.That(queueDbService.HasRecoveryQueueDemand(owner), Is.True);
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    [Test]
    public void GetPendingAndLease_Attempt範囲が逆転している時は空を返す()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-attempt-range-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;

        try
        {
            List<QueueDbLeaseItem> leased = queueDbService.GetPendingAndLease(
                "ATTEMPT-RANGE-OWNER",
                takeCount: 5,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: DateTime.UtcNow,
                minAttemptCount: 2,
                maxAttemptCount: 1
            );

            Assert.That(leased, Is.Empty);
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    [Test]
    public void GetPendingAndLease_MovieSize範囲が逆転している時は空を返す()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-size-range-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;

        try
        {
            List<QueueDbLeaseItem> leased = queueDbService.GetPendingAndLease(
                "SIZE-RANGE-OWNER",
                takeCount: 5,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: DateTime.UtcNow,
                minMovieSizeBytes: 20,
                maxMovieSizeBytes: 10
            );

            Assert.That(leased, Is.Empty);
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    [Test]
    public void HasSlowQueueDemand_巨大動画Pendingと自OwnerProcessingだけ検知する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-slow-demand-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        DateTime nowUtc = DateTime.UtcNow;
        string moviePath = Path.Combine(Path.GetTempPath(), $"movie-slow-{Guid.NewGuid():N}.mkv");
        string owner = "SLOW-DEMAND-OWNER";
        long slowThresholdBytes = 10L * 1024 * 1024 * 1024;
        long movieSizeBytes = slowThresholdBytes + (512L * 1024 * 1024);

        try
        {
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = moviePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath),
                        TabIndex = 0,
                        MovieSizeBytes = movieSizeBytes,
                    },
                ],
                nowUtc
            );

            Assert.That(
                queueDbService.HasSlowQueueDemand(owner, slowThresholdBytes, maxAttemptCount: 0),
                Is.True
            );

            List<QueueDbLeaseItem> leased = queueDbService.GetPendingAndLease(
                owner,
                takeCount: 1,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: nowUtc,
                minMovieSizeBytes: slowThresholdBytes,
                maxAttemptCount: 0
            );
            Assert.That(leased.Count, Is.EqualTo(1));
            Assert.That(leased[0].MovieSizeBytes, Is.EqualTo(movieSizeBytes));

            Assert.That(
                queueDbService.HasSlowQueueDemand(owner, slowThresholdBytes, maxAttemptCount: 0),
                Is.True
            );

            _ = queueDbService.UpdateStatus(
                leased[0].QueueId,
                owner,
                ThumbnailQueueStatus.Done,
                nowUtc.AddSeconds(1)
            );

            Assert.That(
                queueDbService.HasSlowQueueDemand(owner, slowThresholdBytes, maxAttemptCount: 0),
                Is.False
            );
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    [Test]
    public void HasRecoveryQueueDemand_他OwnerProcessing中の再試行は検知しない()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-recovery-other-owner-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        DateTime nowUtc = DateTime.UtcNow;
        string moviePath = Path.Combine(Path.GetTempPath(), $"movie-recovery-other-{Guid.NewGuid():N}.mp4");
        string ownerA = "RECOVERY-OWNER-A";
        string ownerB = "RECOVERY-OWNER-B";

        try
        {
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = moviePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath),
                        TabIndex = 0,
                    },
                ],
                nowUtc
            );

            List<QueueDbLeaseItem> firstLease = queueDbService.GetPendingAndLease(
                ownerA,
                takeCount: 1,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: nowUtc
            );
            Assert.That(firstLease.Count, Is.EqualTo(1));

            _ = queueDbService.UpdateStatus(
                firstLease[0].QueueId,
                ownerA,
                ThumbnailQueueStatus.Pending,
                nowUtc.AddSeconds(1),
                "retry",
                incrementAttemptCount: true
            );

            List<QueueDbLeaseItem> recoveryLease = queueDbService.GetPendingAndLease(
                ownerB,
                takeCount: 1,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: nowUtc.AddSeconds(2),
                minAttemptCount: 1
            );
            Assert.That(recoveryLease.Count, Is.EqualTo(1));
            Assert.That(recoveryLease[0].AttemptCount, Is.EqualTo(1));

            Assert.That(queueDbService.HasRecoveryQueueDemand(ownerA), Is.False);
            Assert.That(queueDbService.HasRecoveryQueueDemand(ownerB), Is.True);
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    [Test]
    public void HasSlowQueueDemand_maxAttemptCount0時は再試行巨大動画を除外する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-slow-retry-exclude-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        DateTime nowUtc = DateTime.UtcNow;
        string moviePath = Path.Combine(Path.GetTempPath(), $"movie-slow-retry-{Guid.NewGuid():N}.mkv");
        string owner = "SLOW-RETRY-OWNER";
        long slowThresholdBytes = 10L * 1024 * 1024 * 1024;

        try
        {
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = moviePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath),
                        TabIndex = 0,
                        MovieSizeBytes = slowThresholdBytes + 1,
                    },
                ],
                nowUtc
            );

            List<QueueDbLeaseItem> firstLease = queueDbService.GetPendingAndLease(
                owner,
                takeCount: 1,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: nowUtc
            );
            Assert.That(firstLease.Count, Is.EqualTo(1));

            _ = queueDbService.UpdateStatus(
                firstLease[0].QueueId,
                owner,
                ThumbnailQueueStatus.Pending,
                nowUtc.AddSeconds(1),
                "retry",
                incrementAttemptCount: true
            );

            Assert.That(
                queueDbService.HasSlowQueueDemand(owner, slowThresholdBytes),
                Is.True
            );
            Assert.That(
                queueDbService.HasSlowQueueDemand(owner, slowThresholdBytes, maxAttemptCount: 0),
                Is.False
            );
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    [Test]
    public void GetPendingAndLease_movieSize条件指定時は巨大動画だけ取得する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-size-filter-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        DateTime nowUtc = DateTime.UtcNow;
        string normalMoviePath = Path.Combine(Path.GetTempPath(), $"movie-size-normal-{Guid.NewGuid():N}.mp4");
        string slowMoviePath = Path.Combine(Path.GetTempPath(), $"movie-size-slow-{Guid.NewGuid():N}.mkv");
        string owner = "SIZE-FILTER-OWNER";
        long slowThresholdBytes = 8L * 1024 * 1024 * 1024;

        try
        {
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = normalMoviePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(normalMoviePath),
                        TabIndex = 0,
                        MovieSizeBytes = 512L * 1024 * 1024,
                    },
                    new QueueDbUpsertItem
                    {
                        MoviePath = slowMoviePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(slowMoviePath),
                        TabIndex = 0,
                        MovieSizeBytes = slowThresholdBytes + 1,
                    },
                ],
                nowUtc
            );

            List<QueueDbLeaseItem> onlySlow = queueDbService.GetPendingAndLease(
                owner,
                takeCount: 5,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: nowUtc,
                minMovieSizeBytes: slowThresholdBytes,
                maxAttemptCount: 0
            );

            Assert.That(onlySlow.Count, Is.EqualTo(1));
            Assert.That(onlySlow[0].MoviePath, Is.EqualTo(slowMoviePath));
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    [Test]
    public void GetPendingAndLease_maxMovieSize指定時は通常サイズだけ取得する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-size-max-filter-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        DateTime nowUtc = DateTime.UtcNow;
        string normalMoviePath = Path.Combine(Path.GetTempPath(), $"movie-size-max-normal-{Guid.NewGuid():N}.mp4");
        string slowMoviePath = Path.Combine(Path.GetTempPath(), $"movie-size-max-slow-{Guid.NewGuid():N}.mkv");
        string owner = "SIZE-MAX-FILTER-OWNER";
        long maxNormalMovieSizeBytes = 2L * 1024 * 1024 * 1024;

        try
        {
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = normalMoviePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(normalMoviePath),
                        TabIndex = 0,
                        MovieSizeBytes = 512L * 1024 * 1024,
                    },
                    new QueueDbUpsertItem
                    {
                        MoviePath = slowMoviePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(slowMoviePath),
                        TabIndex = 0,
                        MovieSizeBytes = 12L * 1024 * 1024 * 1024,
                    },
                ],
                nowUtc
            );

            List<QueueDbLeaseItem> onlyNormal = queueDbService.GetPendingAndLease(
                owner,
                takeCount: 5,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: nowUtc,
                maxMovieSizeBytes: maxNormalMovieSizeBytes
            );

            Assert.That(onlyNormal.Count, Is.EqualTo(1));
            Assert.That(onlyNormal[0].MoviePath, Is.EqualTo(normalMoviePath));
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    // テスト後のQueueDBを掃除して、ローカル環境を汚さない。
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
            // 一時ファイル削除失敗はテスト結果に影響しないため握りつぶす。
        }
    }
}
