using IndigoMovieManager.Thumbnail;
using System.Runtime.CompilerServices;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class SharedThumbnailStoragePolicyTests
{
    [Test]
    public void DB別モードは従来のサムネイルルートを維持する()
    {
        string legacyRoot = Path.Combine(Path.GetTempPath(), "legacy-thumb");

        string actual = SharedThumbnailStoragePolicy.ResolveEffectiveThumbnailRoot(
            @"D:\movies\anime.wb",
            "anime",
            legacyRoot,
            SharedThumbnailStoragePolicy.PerDatabaseMode,
            @"E:\shared"
        );

        Assert.That(actual, Is.EqualTo(legacyRoot));
    }

    [Test]
    public void 共通モードはDBパスごとに別の表示領域を返す()
    {
        string sharedRoot = Path.Combine(Path.GetTempPath(), "imm-shared-root");

        string first = SharedThumbnailStoragePolicy.ResolveEffectiveThumbnailRoot(
            @"D:\movies\anime.wb",
            "anime",
            @"D:\legacy\anime",
            SharedThumbnailStoragePolicy.SharedStoreMode,
            sharedRoot
        );
        string same = SharedThumbnailStoragePolicy.ResolveEffectiveThumbnailRoot(
            @"D:\movies\anime.wb",
            "anime",
            @"X:\other",
            SharedThumbnailStoragePolicy.SharedStoreMode,
            sharedRoot
        );
        string second = SharedThumbnailStoragePolicy.ResolveEffectiveThumbnailRoot(
            @"D:\movies\other.wb",
            "other",
            @"D:\legacy\other",
            SharedThumbnailStoragePolicy.SharedStoreMode,
            sharedRoot
        );

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(same));
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(
                first,
                Does.StartWith(Path.Combine(sharedRoot, "databases") + Path.DirectorySeparatorChar)
            );
        });
    }

    [Test]
    public void 共通モードの同一JPEGはobjectを共有し片方削除後も他方を読める()
    {
        string sharedRoot = CreateTempDirectory("imm-shared-thumbnail-dedupe");
        string firstPath = Path.Combine(sharedRoot, "databases", "db1", "Grid", "first.jpg");
        string secondPath = Path.Combine(sharedRoot, "databases", "db2", "Grid", "second.jpg");
        byte[] jpegBytes = [0xFF, 0xD8, 0x11, 0x22, 0x33, 0xFF, 0xD9];

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(firstPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(secondPath)!);
            File.WriteAllBytes(firstPath, jpegBytes);
            File.WriteAllBytes(secondPath, jpegBytes);

            SharedThumbnailDeduplicationResult first =
                SharedThumbnailStoragePolicy.TryDeduplicateGeneratedThumbnail(
                    firstPath,
                    SharedThumbnailStoragePolicy.SharedStoreMode,
                    sharedRoot
                );
            SharedThumbnailDeduplicationResult second =
                SharedThumbnailStoragePolicy.TryDeduplicateGeneratedThumbnail(
                    secondPath,
                    SharedThumbnailStoragePolicy.SharedStoreMode,
                    sharedRoot
                );

            Assert.Multiple(() =>
            {
                Assert.That(first.HardLinked, Is.True, first.Reason);
                Assert.That(second.HardLinked, Is.True, second.Reason);
                Assert.That(second.ObjectPath, Is.EqualTo(first.ObjectPath));
                Assert.That(File.Exists(first.ObjectPath), Is.True);
            });

            File.Delete(firstPath);

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(secondPath), Is.True);
                Assert.That(File.ReadAllBytes(secondPath), Is.EqualTo(jpegBytes));
                Assert.That(File.Exists(second.ObjectPath), Is.True);
            });
        }
        finally
        {
            TryDeleteDirectory(sharedRoot);
        }
    }

    [Test]
    public void DB別モードでは共有objectを作らない()
    {
        string sharedRoot = CreateTempDirectory("imm-shared-thumbnail-off");
        string thumbnailPath = Path.Combine(sharedRoot, "databases", "db1", "Grid", "sample.jpg");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(thumbnailPath)!);
            File.WriteAllBytes(thumbnailPath, [0xFF, 0xD8, 0xFF, 0xD9]);

            SharedThumbnailDeduplicationResult result =
                SharedThumbnailStoragePolicy.TryDeduplicateGeneratedThumbnail(
                    thumbnailPath,
                    SharedThumbnailStoragePolicy.PerDatabaseMode,
                    sharedRoot
                );

            Assert.Multiple(() =>
            {
                Assert.That(result.Attempted, Is.False);
                Assert.That(result.Reason, Is.EqualTo("per-database-mode"));
                Assert.That(Directory.Exists(Path.Combine(sharedRoot, "objects")), Is.False);
            });
        }
        finally
        {
            TryDeleteDirectory(sharedRoot);
        }
    }

    [Test]
    public void リンク済みDB側JPEGを分離して上書きしてもobjectと他DBを変更しない()
    {
        string sharedRoot = CreateTempDirectory("imm-shared-thumbnail-overwrite");
        string firstPath = Path.Combine(sharedRoot, "databases", "db1", "Grid", "first.jpg");
        string secondPath = Path.Combine(sharedRoot, "databases", "db2", "Grid", "second.jpg");
        byte[] originalBytes = [0xFF, 0xD8, 0x10, 0x20, 0xFF, 0xD9];
        byte[] replacementBytes = [0xFF, 0xD8, 0x30, 0x40, 0xFF, 0xD9];

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(firstPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(secondPath)!);
            File.WriteAllBytes(firstPath, originalBytes);
            File.WriteAllBytes(secondPath, originalBytes);
            SharedThumbnailDeduplicationResult first =
                SharedThumbnailStoragePolicy.TryDeduplicateGeneratedThumbnail(
                    firstPath,
                    SharedThumbnailStoragePolicy.SharedStoreMode,
                    sharedRoot
                );
            SharedThumbnailDeduplicationResult second =
                SharedThumbnailStoragePolicy.TryDeduplicateGeneratedThumbnail(
                    secondPath,
                    SharedThumbnailStoragePolicy.SharedStoreMode,
                    sharedRoot
                );

            SharedThumbnailWritePreparationResult preparation =
                SharedThumbnailStoragePolicy.TryPrepareThumbnailForOverwrite(
                    firstPath,
                    SharedThumbnailStoragePolicy.SharedStoreMode,
                    sharedRoot
                );
            File.WriteAllBytes(firstPath, replacementBytes);

            Assert.Multiple(() =>
            {
                Assert.That(first.HardLinked, Is.True, first.Reason);
                Assert.That(second.HardLinked, Is.True, second.Reason);
                Assert.That(preparation.SafeToWrite, Is.True, preparation.Reason);
                Assert.That(preparation.Detached, Is.True);
                Assert.That(File.ReadAllBytes(firstPath), Is.EqualTo(replacementBytes));
                Assert.That(File.ReadAllBytes(secondPath), Is.EqualTo(originalBytes));
                Assert.That(File.ReadAllBytes(first.ObjectPath), Is.EqualTo(originalBytes));
                Assert.That(
                    Directory.EnumerateFiles(Path.GetDirectoryName(firstPath)!, "*.private-*.tmp"),
                    Is.Empty
                );
            });
        }
        finally
        {
            TryDeleteDirectory(sharedRoot);
        }
    }

    [Test]
    public void DB別モードの既存JPEGは上書き前コピーを行わない()
    {
        string workRoot = CreateTempDirectory("imm-per-db-overwrite");
        string sharedRoot = Path.Combine(workRoot, "shared");
        string thumbnailPath = Path.Combine(workRoot, "legacy", "Grid", "sample.jpg");
        byte[] originalBytes = [0xFF, 0xD8, 0x31, 0xFF, 0xD9];

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(thumbnailPath)!);
            File.WriteAllBytes(thumbnailPath, originalBytes);

            SharedThumbnailWritePreparationResult preparation =
                SharedThumbnailStoragePolicy.TryPrepareThumbnailForOverwrite(
                    thumbnailPath,
                    SharedThumbnailStoragePolicy.PerDatabaseMode,
                    sharedRoot
                );

            Assert.Multiple(() =>
            {
                Assert.That(preparation.SafeToWrite, Is.True, preparation.Reason);
                Assert.That(preparation.Detached, Is.False);
                Assert.That(preparation.Reason, Is.EqualTo("per-database-path"));
                Assert.That(File.ReadAllBytes(thumbnailPath), Is.EqualTo(originalBytes));
                Assert.That(
                    Directory.EnumerateFiles(
                        Path.GetDirectoryName(thumbnailPath)!,
                        "*.private-*.tmp"
                    ),
                    Is.Empty
                );
            });
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    [Test]
    public void DB別モードへ変更直後も旧共通ストア配下のJPEGは分離する()
    {
        string sharedRoot = CreateTempDirectory("imm-old-shared-overwrite");
        string thumbnailPath = Path.Combine(
            sharedRoot,
            "databases",
            "db1",
            "Grid",
            "sample.jpg"
        );
        byte[] originalBytes = [0xFF, 0xD8, 0x61, 0xFF, 0xD9];
        byte[] replacementBytes = [0xFF, 0xD8, 0x62, 0xFF, 0xD9];

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(thumbnailPath)!);
            File.WriteAllBytes(thumbnailPath, originalBytes);
            SharedThumbnailDeduplicationResult deduplication =
                SharedThumbnailStoragePolicy.TryDeduplicateGeneratedThumbnail(
                    thumbnailPath,
                    SharedThumbnailStoragePolicy.SharedStoreMode,
                    sharedRoot
                );

            SharedThumbnailWritePreparationResult preparation =
                SharedThumbnailStoragePolicy.TryPrepareThumbnailForOverwrite(
                    thumbnailPath,
                    SharedThumbnailStoragePolicy.PerDatabaseMode,
                    sharedRoot
                );
            File.WriteAllBytes(thumbnailPath, replacementBytes);

            Assert.Multiple(() =>
            {
                Assert.That(deduplication.HardLinked, Is.True, deduplication.Reason);
                Assert.That(preparation.SafeToWrite, Is.True, preparation.Reason);
                Assert.That(preparation.Detached, Is.True);
                Assert.That(File.ReadAllBytes(thumbnailPath), Is.EqualTo(replacementBytes));
                Assert.That(File.ReadAllBytes(deduplication.ObjectPath), Is.EqualTo(originalBytes));
            });
        }
        finally
        {
            TryDeleteDirectory(sharedRoot);
        }
    }

    [Test]
    public void 一発移行は元フォルダーを残して全ファイルを移し同一JPEGを共有化する()
    {
        string workRoot = CreateTempDirectory("imm-shared-thumbnail-migration");
        string sourceRoot = Path.Combine(workRoot, "legacy");
        string sharedRoot = Path.Combine(workRoot, "shared");
        string targetRoot = Path.Combine(sharedRoot, "databases", "anime.123456789abc");
        string firstRelativePath = Path.Combine("Grid", "first.jpg");
        string secondRelativePath = Path.Combine("List", "second.jpg");
        string markerRelativePath = Path.Combine("Grid", "first.#ERROR.jpg");
        byte[] jpegBytes = [0xFF, 0xD8, 0x45, 0x67, 0xFF, 0xD9];

        try
        {
            Directory.CreateDirectory(Path.Combine(sourceRoot, "Grid"));
            Directory.CreateDirectory(Path.Combine(sourceRoot, "List"));
            File.WriteAllBytes(Path.Combine(sourceRoot, firstRelativePath), jpegBytes);
            File.WriteAllBytes(Path.Combine(sourceRoot, secondRelativePath), jpegBytes);
            File.WriteAllText(Path.Combine(sourceRoot, markerRelativePath), "failed");

            SharedThumbnailMigrationResult result =
                SharedThumbnailStoragePolicy.MigrateThumbnailTreeToSharedStore(
                    sourceRoot,
                    targetRoot,
                    sharedRoot
                );

            Assert.Multiple(() =>
            {
                Assert.That(result.Started, Is.True, result.Reason);
                Assert.That(result.Succeeded, Is.True, result.Reason);
                Assert.That(result.TotalFiles, Is.EqualTo(3));
                Assert.That(result.CopiedFiles, Is.EqualTo(3));
                Assert.That(result.DeduplicatedJpegs, Is.EqualTo(3));
                Assert.That(result.FailedFiles, Is.Zero);
                Assert.That(File.Exists(Path.Combine(sourceRoot, firstRelativePath)), Is.True);
                Assert.That(File.Exists(Path.Combine(sourceRoot, secondRelativePath)), Is.True);
                Assert.That(File.Exists(Path.Combine(sourceRoot, markerRelativePath)), Is.True);
                Assert.That(
                    File.ReadAllBytes(Path.Combine(targetRoot, firstRelativePath)),
                    Is.EqualTo(jpegBytes)
                );
                Assert.That(
                    File.ReadAllText(Path.Combine(targetRoot, markerRelativePath)),
                    Is.EqualTo("failed")
                );
                Assert.That(
                    Directory
                        .EnumerateFiles(
                            Path.Combine(sharedRoot, "objects"),
                            "*.jpg",
                            SearchOption.AllDirectories
                        )
                        .Count(),
                    Is.EqualTo(2)
                );
                Assert.That(
                    Directory.EnumerateFiles(
                        targetRoot,
                        "*.migrate-*.tmp",
                        SearchOption.AllDirectories
                    ),
                    Is.Empty
                );
            });
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    [Test]
    public void 一発移行の再実行は既存移行先を安全に置換して完了する()
    {
        string workRoot = CreateTempDirectory("imm-shared-thumbnail-migration-retry");
        string sourceRoot = Path.Combine(workRoot, "legacy");
        string sharedRoot = Path.Combine(workRoot, "shared");
        string targetRoot = Path.Combine(sharedRoot, "databases", "anime.123456789abc");
        string relativePath = Path.Combine("Grid", "sample.jpg");
        byte[] firstBytes = [0xFF, 0xD8, 0x10, 0xFF, 0xD9];
        byte[] secondBytes = [0xFF, 0xD8, 0x20, 0xFF, 0xD9];

        try
        {
            Directory.CreateDirectory(Path.Combine(sourceRoot, "Grid"));
            File.WriteAllBytes(Path.Combine(sourceRoot, relativePath), firstBytes);
            SharedThumbnailMigrationResult first =
                SharedThumbnailStoragePolicy.MigrateThumbnailTreeToSharedStore(
                    sourceRoot,
                    targetRoot,
                    sharedRoot
                );

            File.WriteAllBytes(Path.Combine(sourceRoot, relativePath), secondBytes);
            SharedThumbnailMigrationResult second =
                SharedThumbnailStoragePolicy.MigrateThumbnailTreeToSharedStore(
                    sourceRoot,
                    targetRoot,
                    sharedRoot
                );

            Assert.Multiple(() =>
            {
                Assert.That(first.Succeeded, Is.True, first.Reason);
                Assert.That(second.Succeeded, Is.True, second.Reason);
                Assert.That(second.CopiedFiles, Is.EqualTo(1));
                Assert.That(
                    File.ReadAllBytes(Path.Combine(targetRoot, relativePath)),
                    Is.EqualTo(secondBytes)
                );
                Assert.That(
                    File.ReadAllBytes(Path.Combine(sourceRoot, relativePath)),
                    Is.EqualTo(secondBytes)
                );
            });
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    [Test]
    public void 一発移行は一部ファイルを読めない時に未完了を返し成功済みコピーを残す()
    {
        string workRoot = CreateTempDirectory("imm-shared-thumbnail-migration-partial");
        string sourceRoot = Path.Combine(workRoot, "legacy");
        string sharedRoot = Path.Combine(workRoot, "shared");
        string targetRoot = Path.Combine(sharedRoot, "databases", "anime.123456789abc");
        string readablePath = Path.Combine(sourceRoot, "Grid", "readable.jpg");
        string lockedPath = Path.Combine(sourceRoot, "Grid", "locked.jpg");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(readablePath)!);
            File.WriteAllBytes(readablePath, [0xFF, 0xD8, 0x10, 0xFF, 0xD9]);
            File.WriteAllBytes(lockedPath, [0xFF, 0xD8, 0x20, 0xFF, 0xD9]);

            using FileStream lockedStream = new(
                lockedPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None
            );
            SharedThumbnailMigrationResult result =
                SharedThumbnailStoragePolicy.MigrateThumbnailTreeToSharedStore(
                    sourceRoot,
                    targetRoot,
                    sharedRoot
                );

            Assert.Multiple(() =>
            {
                Assert.That(result.Started, Is.True);
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.TotalFiles, Is.EqualTo(2));
                Assert.That(result.CopiedFiles, Is.EqualTo(1));
                Assert.That(result.FailedFiles, Is.EqualTo(1));
                Assert.That(result.Reason, Does.Contain("locked.jpg"));
                Assert.That(File.Exists(Path.Combine(targetRoot, "Grid", "readable.jpg")), Is.True);
                Assert.That(File.Exists(readablePath), Is.True);
                Assert.That(File.Exists(lockedPath), Is.True);
            });
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    [Test]
    public void 一発移行は共有書込み中のJPEGを成功扱いしない()
    {
        string workRoot = CreateTempDirectory("imm-shared-thumbnail-migration-writing");
        string sourceRoot = Path.Combine(workRoot, "legacy");
        string sharedRoot = Path.Combine(workRoot, "shared");
        string targetRoot = Path.Combine(sharedRoot, "databases", "anime.123456789abc");
        string sourcePath = Path.Combine(sourceRoot, "Grid", "writing.jpg");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllBytes(sourcePath, [0xFF, 0xD8, 0x41, 0xFF, 0xD9]);

            using FileStream writerStream = new(
                sourcePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete
            );
            SharedThumbnailMigrationResult result =
                SharedThumbnailStoragePolicy.MigrateThumbnailTreeToSharedStore(
                    sourceRoot,
                    targetRoot,
                    sharedRoot
                );

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.CopiedFiles, Is.Zero);
                Assert.That(result.FailedFiles, Is.EqualTo(1));
                Assert.That(File.Exists(sourcePath), Is.True);
                Assert.That(File.Exists(Path.Combine(targetRoot, "Grid", "writing.jpg")), Is.False);
            });
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    [Test]
    public void 一発移行はコピー元とコピー先が重なる構成を拒否する()
    {
        string sharedRoot = CreateTempDirectory("imm-shared-thumbnail-overlap");
        string sourceRoot = Path.Combine(sharedRoot, "databases");
        string targetRoot = Path.Combine(sourceRoot, "anime.123456789abc");

        try
        {
            Directory.CreateDirectory(sourceRoot);

            SharedThumbnailMigrationResult result =
                SharedThumbnailStoragePolicy.MigrateThumbnailTreeToSharedStore(
                    sourceRoot,
                    targetRoot,
                    sharedRoot
                );

            Assert.Multiple(() =>
            {
                Assert.That(result.Started, Is.False);
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Reason, Is.EqualTo("source-target-overlap"));
                Assert.That(Directory.Exists(targetRoot), Is.False);
            });
        }
        finally
        {
            TryDeleteDirectory(sharedRoot);
        }
    }

    [Test]
    public void 移動済み削除は内容一致した旧ファイルだけを消し共通ストアを維持する()
    {
        string workRoot = CreateTempDirectory("imm-shared-thumbnail-source-cleanup");
        string sourceRoot = Path.Combine(workRoot, "legacy");
        string sharedRoot = Path.Combine(workRoot, "shared");
        string targetRoot = Path.Combine(sharedRoot, "databases", "anime.123456789abc");
        string relativePath = Path.Combine("Grid", "sample.jpg");
        byte[] jpegBytes = [0xFF, 0xD8, 0x42, 0xFF, 0xD9];

        try
        {
            Directory.CreateDirectory(Path.Combine(sourceRoot, "Grid"));
            File.WriteAllBytes(Path.Combine(sourceRoot, relativePath), jpegBytes);
            SharedThumbnailMigrationResult migration =
                SharedThumbnailStoragePolicy.MigrateThumbnailTreeToSharedStore(
                    sourceRoot,
                    targetRoot,
                    sharedRoot
                );

            SharedThumbnailSourceCleanupResult cleanup =
                SharedThumbnailStoragePolicy.DeleteMigratedSourceFiles(
                    sourceRoot,
                    targetRoot,
                    sharedRoot
                );

            Assert.Multiple(() =>
            {
                Assert.That(migration.Succeeded, Is.True, migration.Reason);
                Assert.That(cleanup.Started, Is.True, cleanup.Reason);
                Assert.That(cleanup.Succeeded, Is.True, cleanup.Reason);
                Assert.That(cleanup.ScannedFiles, Is.EqualTo(1));
                Assert.That(cleanup.DeletedFiles, Is.EqualTo(1));
                Assert.That(cleanup.SkippedFiles, Is.Zero);
                Assert.That(cleanup.FailedFiles, Is.Zero);
                Assert.That(Directory.Exists(sourceRoot), Is.False);
                Assert.That(
                    File.ReadAllBytes(Path.Combine(targetRoot, relativePath)),
                    Is.EqualTo(jpegBytes)
                );
                Assert.That(
                    Directory
                        .EnumerateFiles(
                            Path.Combine(sharedRoot, "objects"),
                            "*.jpg",
                            SearchOption.AllDirectories
                        )
                        .Count(),
                    Is.EqualTo(1)
                );
            });
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    [Test]
    public void 移行後に内容が変わった旧ファイルは削除せず保留する()
    {
        string workRoot = CreateTempDirectory("imm-shared-thumbnail-source-changed");
        string sourceRoot = Path.Combine(workRoot, "legacy");
        string sharedRoot = Path.Combine(workRoot, "shared");
        string targetRoot = Path.Combine(sharedRoot, "databases", "anime.123456789abc");
        string relativePath = Path.Combine("Grid", "sample.jpg");
        byte[] migratedBytes = [0xFF, 0xD8, 0x10, 0xFF, 0xD9];
        byte[] changedBytes = [0xFF, 0xD8, 0x20, 0xFF, 0xD9];

        try
        {
            Directory.CreateDirectory(Path.Combine(sourceRoot, "Grid"));
            File.WriteAllBytes(Path.Combine(sourceRoot, relativePath), migratedBytes);
            SharedThumbnailMigrationResult migration =
                SharedThumbnailStoragePolicy.MigrateThumbnailTreeToSharedStore(
                    sourceRoot,
                    targetRoot,
                    sharedRoot
                );
            File.WriteAllBytes(Path.Combine(sourceRoot, relativePath), changedBytes);

            SharedThumbnailSourceCleanupResult cleanup =
                SharedThumbnailStoragePolicy.DeleteMigratedSourceFiles(
                    sourceRoot,
                    targetRoot,
                    sharedRoot
                );

            Assert.Multiple(() =>
            {
                Assert.That(migration.Succeeded, Is.True, migration.Reason);
                Assert.That(cleanup.Succeeded, Is.True, cleanup.Reason);
                Assert.That(cleanup.DeletedFiles, Is.Zero);
                Assert.That(cleanup.SkippedFiles, Is.EqualTo(1));
                Assert.That(cleanup.Reason, Is.EqualTo("unmatched-source-files-remain"));
                Assert.That(
                    File.ReadAllBytes(Path.Combine(sourceRoot, relativePath)),
                    Is.EqualTo(changedBytes)
                );
                Assert.That(
                    File.ReadAllBytes(Path.Combine(targetRoot, relativePath)),
                    Is.EqualTo(migratedBytes)
                );
            });
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    [Test]
    public void 移動済み削除は外部書込み中の旧JPEGを削除しない()
    {
        string workRoot = CreateTempDirectory("imm-shared-thumbnail-cleanup-writing");
        string sourceRoot = Path.Combine(workRoot, "legacy");
        string sharedRoot = Path.Combine(workRoot, "shared");
        string targetRoot = Path.Combine(sharedRoot, "databases", "anime.123456789abc");
        string relativePath = Path.Combine("Grid", "writing.jpg");
        string sourcePath = Path.Combine(sourceRoot, relativePath);
        byte[] jpegBytes = [0xFF, 0xD8, 0x51, 0xFF, 0xD9];

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllBytes(sourcePath, jpegBytes);
            SharedThumbnailMigrationResult migration =
                SharedThumbnailStoragePolicy.MigrateThumbnailTreeToSharedStore(
                    sourceRoot,
                    targetRoot,
                    sharedRoot
                );

            using FileStream writerStream = new(
                sourcePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete
            );
            SharedThumbnailSourceCleanupResult cleanup =
                SharedThumbnailStoragePolicy.DeleteMigratedSourceFiles(
                    sourceRoot,
                    targetRoot,
                    sharedRoot
                );
            writerStream.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(migration.Succeeded, Is.True, migration.Reason);
                Assert.That(cleanup.Succeeded, Is.False);
                Assert.That(cleanup.DeletedFiles, Is.Zero);
                Assert.That(cleanup.FailedFiles, Is.EqualTo(1));
                Assert.That(File.Exists(sourcePath), Is.True);
                Assert.That(File.ReadAllBytes(sourcePath), Is.EqualTo(jpegBytes));
                Assert.That(
                    Directory.EnumerateFiles(Path.GetDirectoryName(sourcePath)!, "*.cleanup-*.tmp"),
                    Is.Empty
                );
            });
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    [Test]
    public void 一発移行と旧ファイル削除はJPEG以外を対象にしない()
    {
        string workRoot = CreateTempDirectory("imm-shared-thumbnail-managed-files-only");
        string sourceRoot = Path.Combine(workRoot, "legacy");
        string sharedRoot = Path.Combine(workRoot, "shared");
        string targetRoot = Path.Combine(sharedRoot, "databases", "anime.123456789abc");
        string thumbnailPath = Path.Combine(sourceRoot, "Grid", "sample.jpg");
        string protectedPath = Path.Combine(sourceRoot, "do-not-touch.wb");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(thumbnailPath)!);
            File.WriteAllBytes(thumbnailPath, [0xFF, 0xD8, 0x10, 0xFF, 0xD9]);
            File.WriteAllText(protectedPath, "database");

            SharedThumbnailMigrationResult migration =
                SharedThumbnailStoragePolicy.MigrateThumbnailTreeToSharedStore(
                    sourceRoot,
                    targetRoot,
                    sharedRoot
                );
            SharedThumbnailSourceCleanupResult cleanup =
                SharedThumbnailStoragePolicy.DeleteMigratedSourceFiles(
                    sourceRoot,
                    targetRoot,
                    sharedRoot
                );

            Assert.Multiple(() =>
            {
                Assert.That(migration.Succeeded, Is.True, migration.Reason);
                Assert.That(migration.TotalFiles, Is.EqualTo(1));
                Assert.That(cleanup.Succeeded, Is.True, cleanup.Reason);
                Assert.That(cleanup.ScannedFiles, Is.EqualTo(1));
                Assert.That(cleanup.DeletedFiles, Is.EqualTo(1));
                Assert.That(File.Exists(thumbnailPath), Is.False);
                Assert.That(File.ReadAllText(protectedPath), Is.EqualTo("database"));
                Assert.That(File.Exists(Path.Combine(targetRoot, "do-not-touch.wb")), Is.False);
            });
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    [Test]
    public void 保存方式だけでなく共通ストア保存先の変更も再起動対象にする()
    {
        string localRoot = Path.Combine(Path.GetTempPath(), "imm-config-root");

        Assert.Multiple(() =>
        {
            Assert.That(
                SharedThumbnailStoragePolicy.HasConfigurationChanged(
                    SharedThumbnailStoragePolicy.PerDatabaseMode,
                    Path.Combine(localRoot, "one"),
                    SharedThumbnailStoragePolicy.SharedStoreMode,
                    Path.Combine(localRoot, "one"),
                    localRoot
                ),
                Is.True
            );
            Assert.That(
                SharedThumbnailStoragePolicy.HasConfigurationChanged(
                    SharedThumbnailStoragePolicy.SharedStoreMode,
                    Path.Combine(localRoot, "one"),
                    SharedThumbnailStoragePolicy.SharedStoreMode,
                    Path.Combine(localRoot, "two"),
                    localRoot
                ),
                Is.True
            );
            Assert.That(
                SharedThumbnailStoragePolicy.HasConfigurationChanged(
                    SharedThumbnailStoragePolicy.SharedStoreMode,
                    Path.Combine(localRoot, "one"),
                    SharedThumbnailStoragePolicy.SharedStoreMode,
                    Path.Combine(localRoot, "ONE"),
                    localRoot
                ),
                Is.False
            );
        });
    }

    [TestCase(false, false, false)]
    [TestCase(false, true, false)]
    [TestCase(true, true, false)]
    [TestCase(true, false, true)]
    public void 設定画面の後続操作はClose成功かつ保存構成変更なしの時だけ開始する(
        bool closeAccepted,
        bool thumbnailStorageChanged,
        bool expected
    )
    {
        Assert.That(
            CommonSettingsWindow.ShouldRunFollowUpAction(closeAccepted, thumbnailStorageChanged),
            Is.EqualTo(expected)
        );
    }

    [TestCase(true, true, false, true, true)]
    [TestCase(false, true, false, true, false)]
    [TestCase(true, false, false, true, false)]
    [TestCase(true, true, true, true, false)]
    [TestCase(true, true, false, false, false)]
    public void 移動済み削除は共通モードで移行完了した待機中でない現在DBだけ有効になる(
        bool isShared,
        bool hasCurrentDb,
        bool isFileOperationRunning,
        bool hasCompletedMigration,
        bool expected
    )
    {
        Assert.That(
            CommonSettingsWindow.ShouldEnableMigratedSourceCleanup(
                isShared,
                hasCurrentDb,
                isFileOperationRunning,
                hasCompletedMigration
            ),
            Is.EqualTo(expected)
        );
    }

    [Test]
    public void 設定画面と説明HTMLは切替注意と復帰条件を明示する()
    {
        string settingsXaml = GetRepoText("Views", "Settings", "CommonSettingsWindow.xaml");
        string settingsSource = GetRepoText("Views", "Settings", "CommonSettingsWindow.xaml.cs");
        string helpHtml = GetRepoText("Docs", "Help", "共通サムネイルストア_利用ガイド.html");
        int settingsSaveGuardIndex = settingsSource.IndexOf(
            "if (!settingsSaved)",
            StringComparison.Ordinal
        );
        int cleanupActivationIndex = settingsSource.IndexOf(
            "_completedMigrationSourceRoot = result.SourceRoot;",
            StringComparison.Ordinal
        );

        Assert.Multiple(() =>
        {
            Assert.That(settingsXaml, Does.Contain("x:Name=\"ThumbnailStorageModeSelector\""));
            Assert.That(settingsXaml, Does.Contain("x:Name=\"SharedThumbnailWarning\""));
            Assert.That(settingsXaml, Does.Contain("x:Name=\"SharedThumbnailMigrationPanel\""));
            Assert.That(settingsXaml, Does.Contain("現在DBのサムネイルを一括移動"));
            Assert.That(
                settingsXaml,
                Does.Contain("x:Name=\"DeleteMigratedSourceThumbnailsButton\"")
            );
            Assert.That(settingsXaml, Does.Contain("Content=\"移動済みサムネイル削除\""));
            Assert.That(settingsXaml, Does.Contain("IsEnabled=\"False\""));
            Assert.That(settingsXaml, Does.Contain("詳しい説明・リスク・戻し方を開く（HTML）"));
            Assert.That(settingsSource, Does.Contain("TryConfirmThumbnailStorageModeChange"));
            Assert.That(settingsSource, Does.Contain("TryCloseBeforeFollowUpAction"));
            Assert.That(
                settingsSource,
                Does.Contain("MigrateCurrentDbThumbnailsToSharedStore_Click")
            );
            Assert.That(
                settingsSource,
                Does.Contain("await mainWindow.RunSharedThumbnailMigrationFromSettingsAsync(")
            );
            Assert.That(
                settingsSource,
                Does.Contain("SaveApplicationSettingsFromSettingsWindowAsync(")
            );
            Assert.That(settingsSource, Does.Contain("if (!settingsSaved)"));
            Assert.That(settingsSaveGuardIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(cleanupActivationIndex, Is.GreaterThan(settingsSaveGuardIndex));
            Assert.That(settingsSource, Does.Contain("DeleteMigratedSourceThumbnails_Click"));
            Assert.That(
                settingsSource,
                Does.Contain("RunSharedThumbnailSourceCleanupFromSettingsAsync(")
            );
            Assert.That(settingsSource, Does.Contain("HasCompletedThumbnailMigrationContext"));
            Assert.That(settingsSource, Does.Contain("_originalSharedThumbnailRoot"));
            Assert.That(settingsSource, Does.Contain("UseShellExecute = true"));
            Assert.That(helpHtml, Does.Contain("一括移動と、初回に再構築が必要な場合"));
            Assert.That(helpHtml, Does.Contain("全件成功時は新方式の設定も自動保存"));
            Assert.That(helpHtml, Does.Contain("移動済みサムネイル削除"));
            Assert.That(helpHtml, Does.Contain("DB別モードへ戻す場合"));
            Assert.That(helpHtml, Does.Contain("WhiteBrowserとの関係"));
            Assert.That(helpHtml, Does.Contain("リスクと制限"));
            Assert.That(helpHtml, Does.Contain("DBファイルの移動・改名"));
            Assert.That(helpHtml, Does.Contain("外部ツールでの直接編集"));
            Assert.That(helpHtml, Does.Contain("旧フォルダーをすぐ削除しないでください"));
        });
    }

    [Test]
    public void 実行時経路は共通ルート解決と通常生成rescued生成の重複排除を使う()
    {
        string thumbnailPaths = GetRepoText("Thumbnail", "MainWindow.ThumbnailPaths.cs");
        string mainDbRuntime = GetRepoText("Views", "Main", "MainWindow.MainDbRuntime.cs");
        string thumbnailCreation = GetRepoText("Thumbnail", "MainWindow.ThumbnailCreation.cs");
        string thumbnailFailureSync = GetRepoText(
            "Thumbnail",
            "MainWindow.ThumbnailFailureSync.cs"
        );
        string thumbnailRescueLane = GetRepoText("Thumbnail", "MainWindow.ThumbnailRescueLane.cs");
        string upperTabRescue = GetRepoText(
            "UpperTabs",
            "Rescue",
            "MainWindow.UpperTabs.RescueTab.cs"
        );
        string watchThumbnailQueuePolicy = GetRepoText(
            "Watcher",
            "MainWindow.WatchThumbnailQueuePolicy.cs"
        );
        string projectFile = GetRepoText("IndigoMovieManager.csproj");

        Assert.Multiple(() =>
        {
            Assert.That(
                thumbnailPaths,
                Does.Contain("SharedThumbnailStoragePolicy.ResolveEffectiveThumbnailRoot")
            );
            Assert.That(
                CountOccurrences(mainDbRuntime, "ResolveRuntimeThumbnailRoot("),
                Is.GreaterThanOrEqualTo(2)
            );
            Assert.That(thumbnailCreation, Does.Contain("TryDeduplicateGeneratedThumbnail("));
            Assert.That(
                thumbnailCreation,
                Does.Contain("PrepareExistingThumbnailForGeneration(queueObj);")
            );
            Assert.That(
                thumbnailCreation.IndexOf(
                    "PrepareExistingThumbnailForGeneration(queueObj);",
                    StringComparison.Ordinal
                ),
                Is.LessThan(
                    thumbnailCreation.IndexOf(
                        "_thumbnailCreationService.CreateThumbAsync(",
                        StringComparison.Ordinal
                    )
                )
            );
            Assert.That(
                CountOccurrences(thumbnailFailureSync, "TryDeduplicateGeneratedThumbnail("),
                Is.GreaterThanOrEqualTo(2)
            );
            Assert.That(
                thumbnailRescueLane,
                Does.Contain("TryPrepareExistingThumbnailForRescueOverwrite")
            );
            Assert.That(upperTabRescue, Does.Contain("TryPrepareThumbnailForOverwrite("));
            Assert.That(
                upperTabRescue.IndexOf(
                    "TryPrepareThumbnailForOverwrite(",
                    StringComparison.Ordinal
                ),
                Is.LessThan(
                    upperTabRescue.IndexOf(
                        "File.Move(tempPath, outputThumbPath, overwrite: true)",
                        StringComparison.Ordinal
                    )
                )
            );
            Assert.That(
                watchThumbnailQueuePolicy,
                Does.Contain("RunSharedThumbnailMigrationFromSettingsAsync")
            );
            Assert.That(
                watchThumbnailQueuePolicy,
                Does.Contain("BeginUserPriorityWork(priorityReason);")
            );
            Assert.That(
                watchThumbnailQueuePolicy,
                Does.Contain("WaitForThumbnailQueueIdleForSettingsMigrationAsync")
            );
            Assert.That(
                watchThumbnailQueuePolicy,
                Does.Contain("EndUserPriorityWork(priorityReason);")
            );
            Assert.That(
                watchThumbnailQueuePolicy,
                Does.Contain("RunSharedThumbnailSourceCleanupFromSettingsAsync")
            );
            Assert.That(
                projectFile,
                Does.Contain("Docs\\Help\\共通サムネイルストア_利用ガイド.html")
            );
            Assert.That(projectFile, Does.Contain("<CopyToOutputDirectory>PreserveNewest"));
        });
    }

    private static string CreateTempDirectory(string prefix)
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
        catch
        {
            // 一時フォルダーの後始末失敗は、検証結果を上書きしない。
        }
    }

    private static string GetRepoText(params string[] relativePathParts)
    {
        string[] startDirectories =
        [
            TestContext.CurrentContext.TestDirectory,
            TestContext.CurrentContext.WorkDirectory,
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
            GetCurrentSourceDirectory(),
        ];
        foreach (
            string startDirectory in startDirectories.Distinct(StringComparer.OrdinalIgnoreCase)
        )
        {
            DirectoryInfo? current = new(startDirectory);
            while (current != null)
            {
                string candidate = Path.Combine([current.FullName, .. relativePathParts]);
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }

                current = current.Parent;
            }
        }

        Assert.Fail($"{Path.Combine(relativePathParts)} をrepo rootから解決できませんでした。");
        return string.Empty;
    }

    private static string GetCurrentSourceDirectory(
        [CallerFilePath] string sourceFilePath = ""
    ) => Path.GetDirectoryName(sourceFilePath) ?? "";

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int startIndex = 0;
        while ((startIndex = source.IndexOf(value, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += value.Length;
        }

        return count;
    }
}
