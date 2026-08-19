using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイル保存方式を解決し、共通ストアでは同一JPEGの物理実体を共有する。
    /// DB別の表示フォルダーは残すため、削除・改名・ERRORマーカーの影響は現在DB内に閉じる。
    /// </summary>
    internal static class SharedThumbnailStoragePolicy
    {
        internal const int PerDatabaseMode = 0;
        internal const int SharedStoreMode = 1;
        internal const string SharedDatabasesFolderName = "databases";
        internal const string SharedObjectsFolderName = "objects";

        internal static int NormalizeMode(int mode)
        {
            return mode == SharedStoreMode ? SharedStoreMode : PerDatabaseMode;
        }

        internal static bool IsSharedStoreEnabled(int mode)
        {
            return NormalizeMode(mode) == SharedStoreMode;
        }

        internal static string GetDefaultSharedRoot(string localApplicationDataPath = "")
        {
            string localRoot = string.IsNullOrWhiteSpace(localApplicationDataPath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : localApplicationDataPath;
            string appRootName = AppIdentityRuntime.ResolveStorageRootName();
            return Path.Combine(localRoot, appRootName, "SharedThumbnails");
        }

        internal static string ResolveSharedRoot(
            string configuredSharedRoot,
            string localApplicationDataPath = ""
        )
        {
            string candidate = string.IsNullOrWhiteSpace(configuredSharedRoot)
                ? GetDefaultSharedRoot(localApplicationDataPath)
                : Environment.ExpandEnvironmentVariables(configuredSharedRoot.Trim());

            try
            {
                return Path.GetFullPath(candidate);
            }
            catch
            {
                // 不正な設定値は使用せず、必ず再生成可能な既定領域へ戻す。
                return Path.GetFullPath(GetDefaultSharedRoot(localApplicationDataPath));
            }
        }

        internal static bool HasConfigurationChanged(
            int originalMode,
            string originalSharedRoot,
            int selectedMode,
            string selectedSharedRoot,
            string localApplicationDataPath = ""
        )
        {
            if (NormalizeMode(originalMode) != NormalizeMode(selectedMode))
            {
                return true;
            }

            string originalRoot = ResolveSharedRoot(originalSharedRoot, localApplicationDataPath);
            string selectedRoot = ResolveSharedRoot(selectedSharedRoot, localApplicationDataPath);
            return !string.Equals(originalRoot, selectedRoot, StringComparison.OrdinalIgnoreCase);
        }

        internal static string ResolveEffectiveThumbnailRoot(
            string dbFullPath,
            string dbName,
            string perDatabaseThumbnailRoot,
            int storageMode,
            string configuredSharedRoot,
            string localApplicationDataPath = ""
        )
        {
            if (!IsSharedStoreEnabled(storageMode))
            {
                return perDatabaseThumbnailRoot ?? "";
            }

            string sharedRoot = ResolveSharedRoot(configuredSharedRoot, localApplicationDataPath);
            string databaseFolderName = BuildDatabaseFolderName(dbFullPath, dbName);
            return Path.Combine(sharedRoot, SharedDatabasesFolderName, databaseFolderName);
        }

        internal static string BuildDatabaseFolderName(string dbFullPath, string dbName)
        {
            string safeDbName = SanitizeFileName(
                string.IsNullOrWhiteSpace(dbName)
                    ? Path.GetFileNameWithoutExtension(dbFullPath) ?? "database"
                    : dbName.Trim()
            );
            if (string.IsNullOrWhiteSpace(safeDbName))
            {
                safeDbName = "database";
            }

            string normalizedDbPath = NormalizePathForIdentity(dbFullPath);
            byte[] pathHash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedDbPath));
            string pathHash12 = Convert.ToHexString(pathHash)[..12].ToLowerInvariant();
            return $"{safeDbName}.{pathHash12}";
        }

        internal static SharedThumbnailDeduplicationResult TryDeduplicateGeneratedThumbnail(
            string generatedThumbnailPath,
            int storageMode,
            string configuredSharedRoot,
            string localApplicationDataPath = ""
        )
        {
            if (!IsSharedStoreEnabled(storageMode))
            {
                return SharedThumbnailDeduplicationResult.Skip("per-database-mode");
            }

            if (
                string.IsNullOrWhiteSpace(generatedThumbnailPath)
                || !File.Exists(generatedThumbnailPath)
            )
            {
                return SharedThumbnailDeduplicationResult.Skip("thumbnail-missing");
            }

            string sharedRoot = ResolveSharedRoot(configuredSharedRoot, localApplicationDataPath);
            string databasesRoot = Path.Combine(sharedRoot, SharedDatabasesFolderName);
            if (!IsPathUnderRoot(generatedThumbnailPath, databasesRoot))
            {
                return SharedThumbnailDeduplicationResult.Skip("outside-shared-database-root");
            }

            try
            {
                string contentHash = ComputeFileSha256(generatedThumbnailPath);
                string objectDirectory = Path.Combine(
                    sharedRoot,
                    SharedObjectsFolderName,
                    contentHash[..2]
                );
                Directory.CreateDirectory(objectDirectory);
                string objectPath = Path.Combine(objectDirectory, contentHash + ".jpg");

                if (!File.Exists(objectPath))
                {
                    if (CreateHardLink(objectPath, generatedThumbnailPath, IntPtr.Zero))
                    {
                        return SharedThumbnailDeduplicationResult.Linked(
                            objectPath,
                            "object-created"
                        );
                    }

                    // 別プロセスが先に同じobjectを作った競合だけは、既存object利用へ続ける。
                    if (!File.Exists(objectPath))
                    {
                        return SharedThumbnailDeduplicationResult.Failed(
                            objectPath,
                            $"hard-link-unsupported-or-failed:{Marshal.GetLastWin32Error()}"
                        );
                    }
                }

                if (
                    !string.Equals(
                        ComputeFileSha256(objectPath),
                        contentHash,
                        StringComparison.Ordinal
                    )
                )
                {
                    // 共有objectの内容が名前と一致しない時は触らず、現在DBの生成結果を残す。
                    return SharedThumbnailDeduplicationResult.Failed(
                        objectPath,
                        "shared-object-hash-mismatch"
                    );
                }

                string temporaryLinkPath =
                    generatedThumbnailPath + $".shared-{Guid.NewGuid():N}.tmp";
                try
                {
                    if (!CreateHardLink(temporaryLinkPath, objectPath, IntPtr.Zero))
                    {
                        return SharedThumbnailDeduplicationResult.Failed(
                            objectPath,
                            $"hard-link-reuse-failed:{Marshal.GetLastWin32Error()}"
                        );
                    }

                    // 同じディレクトリ内の一時リンクで置換し、生成済み画像が欠ける時間を作らない。
                    File.Move(temporaryLinkPath, generatedThumbnailPath, overwrite: true);
                    return SharedThumbnailDeduplicationResult.Linked(objectPath, "object-reused");
                }
                finally
                {
                    TryDeleteTemporaryLink(temporaryLinkPath);
                }
            }
            catch (Exception ex)
            {
                // 共通化に失敗しても生成済みJPEGはそのまま使い、表示を失敗させない。
                return SharedThumbnailDeduplicationResult.Failed(
                    "",
                    $"{ex.GetType().Name}:{ex.Message}"
                );
            }
        }

        /// <summary>
        /// 生成先に既存JPEGがある時は、同じ内容の独立ファイルへ差し替えてから上書きへ渡す。
        /// ハードリンクかどうかを推測せず常に分離することで、設定変更直後の旧共通ルートも守る。
        /// </summary>
        internal static SharedThumbnailWritePreparationResult TryPrepareThumbnailForOverwrite(
            string thumbnailPath,
            int storageMode,
            string configuredSharedRoot,
            string localApplicationDataPath = ""
        )
        {
            if (string.IsNullOrWhiteSpace(thumbnailPath) || !File.Exists(thumbnailPath))
            {
                return SharedThumbnailWritePreparationResult.Ready(
                    detached: false,
                    "thumbnail-missing"
                );
            }

            string sharedRoot = ResolveSharedRoot(configuredSharedRoot, localApplicationDataPath);
            string sharedDatabasesRoot = Path.Combine(sharedRoot, SharedDatabasesFolderName);
            if (
                !IsSharedStoreEnabled(storageMode)
                && !IsPathUnderRoot(thumbnailPath, sharedDatabasesRoot)
            )
            {
                // 従来のDB別ルートは共有objectを持たないため、上書き前コピーを増やさない。
                return SharedThumbnailWritePreparationResult.Ready(
                    detached: false,
                    "per-database-path"
                );
            }

            string temporaryCopyPath = thumbnailPath + $".private-{Guid.NewGuid():N}.tmp";
            try
            {
                // File.Copyで別実体を作り、同一ディレクトリ内の置換で共有objectとのリンクを切る。
                File.Copy(thumbnailPath, temporaryCopyPath, overwrite: false);
                File.Move(temporaryCopyPath, thumbnailPath, overwrite: true);
                return SharedThumbnailWritePreparationResult.Ready(
                    detached: true,
                    "existing-thumbnail-detached"
                );
            }
            catch (Exception ex)
            {
                return SharedThumbnailWritePreparationResult.Failed(
                    $"{ex.GetType().Name}:{ex.Message}"
                );
            }
            finally
            {
                TryDeleteTemporaryLink(temporaryCopyPath);
            }
        }

        /// <summary>
        /// 現在DBの旧JPEG一式を、DB別の相対構造を保ったまま共通ストアへ移行する。
        /// 元フォルダーは削除せず、全件成功後に設定側が新方式を確定できる契約にする。
        /// </summary>
        internal static SharedThumbnailMigrationResult MigrateThumbnailTreeToSharedStore(
            string sourceRoot,
            string targetDatabaseRoot,
            string configuredSharedRoot,
            string localApplicationDataPath = ""
        )
        {
            if (
                string.IsNullOrWhiteSpace(sourceRoot)
                || string.IsNullOrWhiteSpace(targetDatabaseRoot)
            )
            {
                return SharedThumbnailMigrationResult.Rejected(
                    sourceRoot,
                    targetDatabaseRoot,
                    "source-or-target-empty"
                );
            }

            string normalizedSourceRoot;
            string normalizedTargetRoot;
            string normalizedSharedRoot;
            try
            {
                normalizedSourceRoot = Path.GetFullPath(sourceRoot?.Trim() ?? "");
                normalizedTargetRoot = Path.GetFullPath(targetDatabaseRoot?.Trim() ?? "");
                normalizedSharedRoot = ResolveSharedRoot(
                    configuredSharedRoot,
                    localApplicationDataPath
                );
            }
            catch (Exception ex)
            {
                return SharedThumbnailMigrationResult.Rejected(
                    sourceRoot,
                    targetDatabaseRoot,
                    $"invalid-path:{ex.GetType().Name}:{ex.Message}"
                );
            }

            if (IsFileSystemRoot(normalizedSourceRoot))
            {
                return SharedThumbnailMigrationResult.Rejected(
                    normalizedSourceRoot,
                    normalizedTargetRoot,
                    "source-root-too-broad"
                );
            }

            string databasesRoot = Path.Combine(normalizedSharedRoot, SharedDatabasesFolderName);
            if (!IsPathUnderRoot(normalizedTargetRoot, databasesRoot))
            {
                return SharedThumbnailMigrationResult.Rejected(
                    normalizedSourceRoot,
                    normalizedTargetRoot,
                    "target-outside-shared-database-root"
                );
            }

            if (AreSameOrNestedRoots(normalizedSourceRoot, normalizedTargetRoot))
            {
                return SharedThumbnailMigrationResult.Rejected(
                    normalizedSourceRoot,
                    normalizedTargetRoot,
                    "source-target-overlap"
                );
            }

            if (!Directory.Exists(normalizedSourceRoot))
            {
                // 既存サムネイルがないDBも、新方式の選択自体は一発で完了できる。
                return SharedThumbnailMigrationResult.Completed(
                    normalizedSourceRoot,
                    normalizedTargetRoot,
                    totalFiles: 0,
                    copiedFiles: 0,
                    deduplicatedJpegs: 0,
                    fallbackJpegs: 0
                );
            }

            string[] sourceFiles;
            try
            {
                // 移行先を作る前に管理対象JPEGを固定し、処理中の追加を再帰列挙へ混ぜない。
                sourceFiles = EnumerateManagedThumbnailFiles(normalizedSourceRoot);
            }
            catch (Exception ex)
            {
                return SharedThumbnailMigrationResult.Rejected(
                    normalizedSourceRoot,
                    normalizedTargetRoot,
                    $"source-enumeration-failed:{ex.GetType().Name}:{ex.Message}"
                );
            }

            int copiedFiles = 0;
            int deduplicatedJpegs = 0;
            int fallbackJpegs = 0;
            int failedFiles = 0;
            string firstFailure = "";

            foreach (string sourceFilePath in sourceFiles)
            {
                string relativePath = Path.GetRelativePath(normalizedSourceRoot, sourceFilePath);
                string targetFilePath = Path.Combine(normalizedTargetRoot, relativePath);
                if (
                    !TryCopyFileAtomically(sourceFilePath, targetFilePath, out string failureReason)
                )
                {
                    failedFiles++;
                    if (string.IsNullOrWhiteSpace(firstFailure))
                    {
                        firstFailure = $"{relativePath}: {failureReason}";
                    }

                    continue;
                }

                copiedFiles++;
                if (!IsManagedThumbnailFile(targetFilePath))
                {
                    continue;
                }

                SharedThumbnailDeduplicationResult deduplicationResult =
                    TryDeduplicateGeneratedThumbnail(
                        targetFilePath,
                        SharedStoreMode,
                        normalizedSharedRoot,
                        localApplicationDataPath
                    );
                if (deduplicationResult.HardLinked)
                {
                    deduplicatedJpegs++;
                }
                else
                {
                    // 非対応ドライブでもコピー済みJPEGは有効なので、移行失敗にはしない。
                    fallbackJpegs++;
                }
            }

            if (failedFiles > 0)
            {
                return SharedThumbnailMigrationResult.Partial(
                    normalizedSourceRoot,
                    normalizedTargetRoot,
                    sourceFiles.Length,
                    copiedFiles,
                    deduplicatedJpegs,
                    fallbackJpegs,
                    failedFiles,
                    firstFailure
                );
            }

            return SharedThumbnailMigrationResult.Completed(
                normalizedSourceRoot,
                normalizedTargetRoot,
                sourceFiles.Length,
                copiedFiles,
                deduplicatedJpegs,
                fallbackJpegs
            );
        }

        /// <summary>
        /// 共通ストア側と内容が一致する旧ファイルだけを削除する。
        /// 一致しないファイルは未移行または移行後更新の可能性があるため、必ず旧側へ残す。
        /// </summary>
        internal static SharedThumbnailSourceCleanupResult DeleteMigratedSourceFiles(
            string sourceRoot,
            string targetDatabaseRoot,
            string configuredSharedRoot,
            string localApplicationDataPath = ""
        )
        {
            if (
                string.IsNullOrWhiteSpace(sourceRoot)
                || string.IsNullOrWhiteSpace(targetDatabaseRoot)
            )
            {
                return SharedThumbnailSourceCleanupResult.Rejected(
                    sourceRoot,
                    targetDatabaseRoot,
                    "source-or-target-empty"
                );
            }

            string normalizedSourceRoot;
            string normalizedTargetRoot;
            string normalizedSharedRoot;
            try
            {
                normalizedSourceRoot = Path.GetFullPath(sourceRoot.Trim());
                normalizedTargetRoot = Path.GetFullPath(targetDatabaseRoot.Trim());
                normalizedSharedRoot = ResolveSharedRoot(
                    configuredSharedRoot,
                    localApplicationDataPath
                );
            }
            catch (Exception ex)
            {
                return SharedThumbnailSourceCleanupResult.Rejected(
                    sourceRoot,
                    targetDatabaseRoot,
                    $"invalid-path:{ex.GetType().Name}:{ex.Message}"
                );
            }

            if (IsFileSystemRoot(normalizedSourceRoot))
            {
                return SharedThumbnailSourceCleanupResult.Rejected(
                    normalizedSourceRoot,
                    normalizedTargetRoot,
                    "source-root-too-broad"
                );
            }

            string databasesRoot = Path.Combine(normalizedSharedRoot, SharedDatabasesFolderName);
            if (!IsPathUnderRoot(normalizedTargetRoot, databasesRoot))
            {
                return SharedThumbnailSourceCleanupResult.Rejected(
                    normalizedSourceRoot,
                    normalizedTargetRoot,
                    "target-outside-shared-database-root"
                );
            }

            if (AreSameOrNestedRoots(normalizedSourceRoot, normalizedTargetRoot))
            {
                return SharedThumbnailSourceCleanupResult.Rejected(
                    normalizedSourceRoot,
                    normalizedTargetRoot,
                    "source-target-overlap"
                );
            }

            if (!Directory.Exists(normalizedSourceRoot))
            {
                return SharedThumbnailSourceCleanupResult.Completed(
                    normalizedSourceRoot,
                    normalizedTargetRoot,
                    scannedFiles: 0,
                    deletedFiles: 0,
                    skippedFiles: 0,
                    failedFiles: 0,
                    removedDirectories: 0,
                    "source-missing"
                );
            }

            string[] sourceFiles;
            try
            {
                sourceFiles = EnumerateManagedThumbnailFiles(normalizedSourceRoot);
            }
            catch (Exception ex)
            {
                return SharedThumbnailSourceCleanupResult.Rejected(
                    normalizedSourceRoot,
                    normalizedTargetRoot,
                    $"source-enumeration-failed:{ex.GetType().Name}:{ex.Message}"
                );
            }

            int deletedFiles = 0;
            int skippedFiles = 0;
            int failedFiles = 0;
            string firstFailure = "";
            foreach (string sourceFilePath in sourceFiles)
            {
                string relativePath = Path.GetRelativePath(normalizedSourceRoot, sourceFilePath);
                string targetFilePath = Path.Combine(normalizedTargetRoot, relativePath);
                try
                {
                    SourceCleanupFileOutcome outcome = TryDeleteMatchedSourceFileSafely(
                        sourceFilePath,
                        targetFilePath,
                        out string fileFailureReason
                    );
                    if (outcome == SourceCleanupFileOutcome.Skipped)
                    {
                        skippedFiles++;
                        continue;
                    }

                    if (outcome == SourceCleanupFileOutcome.Deleted)
                    {
                        deletedFiles++;
                        continue;
                    }

                    failedFiles++;
                    if (string.IsNullOrWhiteSpace(firstFailure))
                    {
                        firstFailure = $"{relativePath}: {fileFailureReason}";
                    }
                }
                catch (Exception ex)
                {
                    failedFiles++;
                    if (string.IsNullOrWhiteSpace(firstFailure))
                    {
                        firstFailure = $"{relativePath}: {ex.GetType().Name}:{ex.Message}";
                    }
                }
            }

            int removedDirectories = TryDeleteEmptySourceDirectories(normalizedSourceRoot);
            string reason =
                failedFiles > 0 ? firstFailure
                : skippedFiles > 0 ? "unmatched-source-files-remain"
                : "completed";
            return SharedThumbnailSourceCleanupResult.Completed(
                normalizedSourceRoot,
                normalizedTargetRoot,
                sourceFiles.Length,
                deletedFiles,
                skippedFiles,
                failedFiles,
                removedDirectories,
                reason
            );
        }

        private static bool TryCopyFileAtomically(
            string sourceFilePath,
            string targetFilePath,
            out string failureReason
        )
        {
            string temporaryPath = targetFilePath + $".migrate-{Guid.NewGuid():N}.tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath) ?? "");
                using (
                    FileStream sourceStream = new(
                        sourceFilePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read
                    )
                )
                using (
                    FileStream targetStream = new(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None
                    )
                )
                {
                    sourceStream.CopyTo(targetStream);
                    if (sourceStream.Position != sourceStream.Length)
                    {
                        throw new IOException("source-length-changed-during-copy");
                    }

                    targetStream.Flush(flushToDisk: true);
                    if (targetStream.Length != sourceStream.Length)
                    {
                        throw new IOException("target-length-mismatch-after-copy");
                    }
                }

                // 同一ディレクトリ内で置換し、途中状態のファイルを表示側へ見せない。
                File.Move(temporaryPath, targetFilePath, overwrite: true);
                failureReason = "";
                return true;
            }
            catch (Exception ex)
            {
                failureReason = $"{ex.GetType().Name}:{ex.Message}";
                return false;
            }
            finally
            {
                TryDeleteTemporaryLink(temporaryPath);
            }
        }

        private enum SourceCleanupFileOutcome
        {
            Deleted,
            Skipped,
            Failed,
        }

        private static SourceCleanupFileOutcome TryDeleteMatchedSourceFileSafely(
            string sourceFilePath,
            string targetFilePath,
            out string failureReason
        )
        {
            failureReason = "";
            if (!TryFilesHaveSameStableContent(sourceFilePath, targetFilePath))
            {
                return SourceCleanupFileOutcome.Skipped;
            }

            string quarantinePath = sourceFilePath + $".cleanup-{Guid.NewGuid():N}.tmp";
            bool sourceWasQuarantined = false;
            try
            {
                // 元パスから先に退避し、照合後に外部処理が作った新ファイルを削除しない。
                File.Move(sourceFilePath, quarantinePath, overwrite: false);
                sourceWasQuarantined = true;

                bool contentStillMatches;
                using (
                    FileStream sourceStream = new(
                        quarantinePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read | FileShare.Delete
                    )
                )
                using (
                    FileStream targetStream = new(
                        targetFilePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read
                    )
                )
                {
                    contentStillMatches = sourceStream.Length == targetStream.Length;
                    if (contentStillMatches)
                    {
                        string sourceHash = ComputeFileSha256(sourceStream);
                        string targetHash = ComputeFileSha256(targetStream);
                        contentStillMatches = string.Equals(
                            sourceHash,
                            targetHash,
                            StringComparison.Ordinal
                        );
                    }

                    if (contentStillMatches)
                    {
                        // sourceは書込み禁止、targetは書込み・置換禁止のまま隔離済み実体だけを消す。
                        File.Delete(quarantinePath);
                        sourceWasQuarantined = false;
                        return SourceCleanupFileOutcome.Deleted;
                    }
                }

                if (
                    TryRestoreQuarantinedSource(
                        sourceFilePath,
                        quarantinePath,
                        out string restoreFailureReason
                    )
                )
                {
                    sourceWasQuarantined = false;
                    return SourceCleanupFileOutcome.Skipped;
                }

                sourceWasQuarantined = false;
                failureReason = restoreFailureReason;
                return SourceCleanupFileOutcome.Failed;
            }
            catch (Exception ex)
            {
                failureReason = $"{ex.GetType().Name}:{ex.Message}";
                if (
                    sourceWasQuarantined
                    && !TryRestoreQuarantinedSource(
                        sourceFilePath,
                        quarantinePath,
                        out string restoreFailureReason
                    )
                )
                {
                    failureReason += $"; {restoreFailureReason}";
                }

                return SourceCleanupFileOutcome.Failed;
            }
        }

        private static bool TryRestoreQuarantinedSource(
            string sourceFilePath,
            string quarantinePath,
            out string failureReason
        )
        {
            failureReason = "";
            try
            {
                if (!File.Exists(quarantinePath))
                {
                    return true;
                }

                if (!File.Exists(sourceFilePath))
                {
                    File.Move(quarantinePath, sourceFilePath, overwrite: false);
                    return true;
                }

                string sourceDirectory = Path.GetDirectoryName(sourceFilePath) ?? "";
                string preservedPath = Path.Combine(
                    sourceDirectory,
                    $"{Path.GetFileNameWithoutExtension(sourceFilePath)}.cleanup-preserved-{Guid.NewGuid():N}{Path.GetExtension(sourceFilePath)}"
                );
                File.Move(quarantinePath, preservedPath, overwrite: false);
                failureReason = $"source-recreated-during-cleanup:preserved='{preservedPath}'";
                return false;
            }
            catch (Exception ex)
            {
                failureReason = $"source-restore-failed:{ex.GetType().Name}:{ex.Message}";
                return false;
            }
        }

        private static bool TryFilesHaveSameStableContent(
            string sourceFilePath,
            string targetFilePath
        )
        {
            if (!File.Exists(sourceFilePath) || !File.Exists(targetFilePath))
            {
                return false;
            }

            FileInfo sourceBefore = new(sourceFilePath);
            FileInfo targetBefore = new(targetFilePath);
            long sourceLength = sourceBefore.Length;
            long targetLength = targetBefore.Length;
            DateTime sourceWriteTimeUtc = sourceBefore.LastWriteTimeUtc;
            DateTime targetWriteTimeUtc = targetBefore.LastWriteTimeUtc;
            if (sourceLength != targetLength)
            {
                return false;
            }

            string sourceHash = ComputeFileSha256(sourceFilePath);
            string targetHash = ComputeFileSha256(targetFilePath);
            sourceBefore.Refresh();
            targetBefore.Refresh();
            if (!sourceBefore.Exists || !targetBefore.Exists)
            {
                return false;
            }

            return sourceBefore.Length == sourceLength
                && targetBefore.Length == targetLength
                && sourceBefore.LastWriteTimeUtc == sourceWriteTimeUtc
                && targetBefore.LastWriteTimeUtc == targetWriteTimeUtc
                && string.Equals(sourceHash, targetHash, StringComparison.Ordinal);
        }

        private static bool IsManagedThumbnailFile(string filePath)
        {
            string extension = Path.GetExtension(filePath);
            return string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);
        }

        private static string[] EnumerateManagedThumbnailFiles(string sourceRoot)
        {
            EnumerationOptions options = new()
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = false,
                ReturnSpecialDirectories = false,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
            return Directory
                .GetFiles(sourceRoot, "*", options)
                .Where(IsManagedThumbnailFile)
                .ToArray();
        }

        private static int TryDeleteEmptySourceDirectories(string sourceRoot)
        {
            int removedDirectories = 0;
            try
            {
                EnumerationOptions options = new()
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = false,
                    ReturnSpecialDirectories = false,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                };
                string[] directories = Directory.GetDirectories(sourceRoot, "*", options);
                Array.Sort(directories, (left, right) => right.Length.CompareTo(left.Length));
                foreach (string directoryPath in directories)
                {
                    if (!Directory.EnumerateFileSystemEntries(directoryPath).Any())
                    {
                        Directory.Delete(directoryPath, recursive: false);
                        removedDirectories++;
                    }
                }

                if (!Directory.EnumerateFileSystemEntries(sourceRoot).Any())
                {
                    Directory.Delete(sourceRoot, recursive: false);
                    removedDirectories++;
                }
            }
            catch
            {
                // ファイル削除の成否を正本とし、空フォルダー整理失敗は次回へ回す。
            }

            return removedDirectories;
        }

        private static string ComputeFileSha256(string filePath)
        {
            using FileStream stream = new(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            return ComputeFileSha256(stream);
        }

        private static string ComputeFileSha256(FileStream stream)
        {
            stream.Position = 0;
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        private static string NormalizePathForIdentity(string rawPath)
        {
            string normalized = rawPath?.Trim() ?? "";
            try
            {
                normalized = Path.GetFullPath(normalized);
            }
            catch
            {
                // 空値や不正値も決定的な文字列としてハッシュ化し、例外でDB切替を止めない。
            }

            return normalized.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
        }

        private static string SanitizeFileName(string value)
        {
            string result = value ?? "";
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(invalidChar, '_');
            }

            return result.Trim();
        }

        private static bool IsPathUnderRoot(string candidatePath, string rootPath)
        {
            try
            {
                string candidate = Path.GetFullPath(candidatePath);
                string root =
                    Path.GetFullPath(rootPath)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static bool AreSameOrNestedRoots(string firstPath, string secondPath)
        {
            try
            {
                string first = Path.GetFullPath(firstPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string second = Path.GetFullPath(secondPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(first, second, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                string firstPrefix = first + Path.DirectorySeparatorChar;
                string secondPrefix = second + Path.DirectorySeparatorChar;
                return first.StartsWith(secondPrefix, StringComparison.OrdinalIgnoreCase)
                    || second.StartsWith(firstPrefix, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsFileSystemRoot(string path)
        {
            string normalizedPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string rootPath = (Path.GetPathRoot(normalizedPath) ?? "").TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            );
            return !string.IsNullOrWhiteSpace(rootPath)
                && string.Equals(normalizedPath, rootPath, StringComparison.OrdinalIgnoreCase);
        }

        private static void TryDeleteTemporaryLink(string path)
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
                // 一時リンクの後始末失敗は、生成済みサムネイルの利用を妨げない。
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLink(
            string newFileName,
            string existingFileName,
            IntPtr securityAttributes
        );
    }

    internal readonly record struct SharedThumbnailDeduplicationResult(
        bool Attempted,
        bool HardLinked,
        string ObjectPath,
        string Reason
    )
    {
        internal static SharedThumbnailDeduplicationResult Skip(string reason) =>
            new(false, false, "", reason ?? "");

        internal static SharedThumbnailDeduplicationResult Linked(
            string objectPath,
            string reason
        ) => new(true, true, objectPath ?? "", reason ?? "");

        internal static SharedThumbnailDeduplicationResult Failed(
            string objectPath,
            string reason
        ) => new(true, false, objectPath ?? "", reason ?? "");
    }

    internal readonly record struct SharedThumbnailWritePreparationResult(
        bool SafeToWrite,
        bool Detached,
        string Reason
    )
    {
        internal static SharedThumbnailWritePreparationResult Ready(bool detached, string reason) =>
            new(true, detached, reason ?? "");

        internal static SharedThumbnailWritePreparationResult Failed(string reason) =>
            new(false, false, reason ?? "");
    }

    internal readonly record struct SharedThumbnailMigrationResult(
        bool Started,
        bool Succeeded,
        string SourceRoot,
        string TargetRoot,
        int TotalFiles,
        int CopiedFiles,
        int DeduplicatedJpegs,
        int FallbackJpegs,
        int FailedFiles,
        string Reason
    )
    {
        internal static SharedThumbnailMigrationResult Rejected(
            string sourceRoot,
            string targetRoot,
            string reason
        ) => new(false, false, sourceRoot ?? "", targetRoot ?? "", 0, 0, 0, 0, 0, reason ?? "");

        internal static SharedThumbnailMigrationResult Completed(
            string sourceRoot,
            string targetRoot,
            int totalFiles,
            int copiedFiles,
            int deduplicatedJpegs,
            int fallbackJpegs
        ) =>
            new(
                true,
                true,
                sourceRoot ?? "",
                targetRoot ?? "",
                totalFiles,
                copiedFiles,
                deduplicatedJpegs,
                fallbackJpegs,
                0,
                "completed"
            );

        internal static SharedThumbnailMigrationResult Partial(
            string sourceRoot,
            string targetRoot,
            int totalFiles,
            int copiedFiles,
            int deduplicatedJpegs,
            int fallbackJpegs,
            int failedFiles,
            string reason
        ) =>
            new(
                true,
                false,
                sourceRoot ?? "",
                targetRoot ?? "",
                totalFiles,
                copiedFiles,
                deduplicatedJpegs,
                fallbackJpegs,
                failedFiles,
                reason ?? ""
            );
    }

    internal readonly record struct SharedThumbnailSourceCleanupResult(
        bool Started,
        bool Succeeded,
        string SourceRoot,
        string TargetRoot,
        int ScannedFiles,
        int DeletedFiles,
        int SkippedFiles,
        int FailedFiles,
        int RemovedDirectories,
        string Reason
    )
    {
        internal static SharedThumbnailSourceCleanupResult Rejected(
            string sourceRoot,
            string targetRoot,
            string reason
        ) => new(false, false, sourceRoot ?? "", targetRoot ?? "", 0, 0, 0, 0, 0, reason ?? "");

        internal static SharedThumbnailSourceCleanupResult Completed(
            string sourceRoot,
            string targetRoot,
            int scannedFiles,
            int deletedFiles,
            int skippedFiles,
            int failedFiles,
            int removedDirectories,
            string reason
        ) =>
            new(
                true,
                failedFiles == 0,
                sourceRoot ?? "",
                targetRoot ?? "",
                scannedFiles,
                deletedFiles,
                skippedFiles,
                failedFiles,
                removedDirectories,
                reason ?? ""
            );
    }
}
