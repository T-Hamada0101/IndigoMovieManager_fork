using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// Worker 設定スナップショットをファイルへ保存し、UI と Worker の橋渡しを行う。
    /// </summary>
    public static class ThumbnailWorkerSettingsStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
        private const int SaveRetryCount = 3;
        private static readonly TimeSpan SaveRetryDelay = TimeSpan.FromMilliseconds(40);

        public static ThumbnailWorkerSettingsSaveResult SaveSnapshot(
            ThumbnailWorkerSettingsSnapshot snapshot
        )
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            string snapshotFilePath = ResolveSnapshotFilePath(snapshot.MainDbFullPath, snapshot.DbName);
            Directory.CreateDirectory(ResolveSnapshotDirectoryPath());

            string versionToken = ComputeVersionToken(snapshot);
            ThumbnailWorkerSettingsSnapshot persistedSnapshot = new()
            {
                MainDbFullPath = snapshot.MainDbFullPath ?? "",
                DbName = snapshot.DbName ?? "",
                ThumbFolder = snapshot.ThumbFolder ?? "",
                Preset = snapshot.Preset ?? "",
                RequestedParallelism = snapshot.RequestedParallelism,
                SlowLaneMinGb = snapshot.SlowLaneMinGb,
                GpuDecodeEnabled = snapshot.GpuDecodeEnabled,
                ResizeThumb = snapshot.ResizeThumb,
                BasePollIntervalMs = snapshot.BasePollIntervalMs,
                LeaseMinutes = snapshot.LeaseMinutes,
                CoordinatorNormalParallelismOverride =
                    snapshot.CoordinatorNormalParallelismOverride,
                CoordinatorIdleParallelismOverride =
                    snapshot.CoordinatorIdleParallelismOverride,
                VersionToken = versionToken,
                UpdatedAtUtc = DateTime.UtcNow,
            };

            bool shouldWrite = true;
            if (File.Exists(snapshotFilePath))
            {
                try
                {
                    ThumbnailWorkerSettingsSnapshot existing = LoadSnapshot(snapshotFilePath);
                    shouldWrite = existing == null
                        || !string.Equals(existing.VersionToken, versionToken, StringComparison.Ordinal);
                }
                catch
                {
                    shouldWrite = true;
                }
            }

            if (shouldWrite)
            {
                string json = JsonSerializer.Serialize(persistedSnapshot, JsonOptions);
                SaveSnapshotFile(snapshotFilePath, json, versionToken);
            }

            return new ThumbnailWorkerSettingsSaveResult
            {
                SnapshotFilePath = snapshotFilePath,
                VersionToken = versionToken,
            };
        }

        public static ThumbnailWorkerSettingsSnapshot LoadSnapshot(string snapshotFilePath)
        {
            if (string.IsNullOrWhiteSpace(snapshotFilePath))
            {
                throw new ArgumentException(
                    "snapshotFilePath is required.",
                    nameof(snapshotFilePath)
                );
            }

            string json = File.ReadAllText(snapshotFilePath, Encoding.UTF8);
            ThumbnailWorkerSettingsSnapshot snapshot =
                JsonSerializer.Deserialize<ThumbnailWorkerSettingsSnapshot>(json, JsonOptions);
            if (snapshot == null)
            {
                throw new InvalidOperationException("worker settings snapshot deserialize failed.");
            }

            if (string.IsNullOrWhiteSpace(snapshot.VersionToken))
            {
                snapshot = new ThumbnailWorkerSettingsSnapshot
                {
                    MainDbFullPath = snapshot.MainDbFullPath,
                    DbName = snapshot.DbName,
                    ThumbFolder = snapshot.ThumbFolder,
                    Preset = snapshot.Preset,
                    RequestedParallelism = snapshot.RequestedParallelism,
                    SlowLaneMinGb = snapshot.SlowLaneMinGb,
                    GpuDecodeEnabled = snapshot.GpuDecodeEnabled,
                    ResizeThumb = snapshot.ResizeThumb,
                    BasePollIntervalMs = snapshot.BasePollIntervalMs,
                    LeaseMinutes = snapshot.LeaseMinutes,
                    CoordinatorNormalParallelismOverride =
                        snapshot.CoordinatorNormalParallelismOverride,
                    CoordinatorIdleParallelismOverride =
                        snapshot.CoordinatorIdleParallelismOverride,
                    VersionToken = ComputeVersionToken(snapshot),
                    UpdatedAtUtc = snapshot.UpdatedAtUtc,
                };
            }

            return snapshot;
        }

        public static string ResolveSnapshotDirectoryPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IndigoMovieManager_fork",
                "worker-settings"
            );
        }

        private static string ResolveSnapshotFilePath(string mainDbFullPath, string dbName)
        {
            string normalizedDbPath = (mainDbFullPath ?? "").Trim().ToLowerInvariant();
            string dbHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(normalizedDbPath))
            )[..16].ToLowerInvariant();
            string safeDbName = ToSafeFileName(string.IsNullOrWhiteSpace(dbName) ? "db" : dbName);
            return Path.Combine(
                ResolveSnapshotDirectoryPath(),
                $"thumbnail-worker-settings-{safeDbName}-{dbHash}.json"
            );
        }

        private static string ComputeVersionToken(ThumbnailWorkerSettingsSnapshot snapshot)
        {
            byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    MainDbFullPath = snapshot.MainDbFullPath ?? "",
                    DbName = snapshot.DbName ?? "",
                    ThumbFolder = snapshot.ThumbFolder ?? "",
                    Preset = snapshot.Preset ?? "",
                    RequestedParallelism = snapshot.RequestedParallelism,
                    SlowLaneMinGb = snapshot.SlowLaneMinGb,
                    GpuDecodeEnabled = snapshot.GpuDecodeEnabled,
                    ResizeThumb = snapshot.ResizeThumb,
                    BasePollIntervalMs = snapshot.BasePollIntervalMs,
                    LeaseMinutes = snapshot.LeaseMinutes,
                    CoordinatorNormalParallelismOverride =
                        snapshot.CoordinatorNormalParallelismOverride,
                    CoordinatorIdleParallelismOverride =
                        snapshot.CoordinatorIdleParallelismOverride,
                },
                JsonOptions
            );
            return Convert.ToHexString(SHA256.HashData(jsonBytes))[..16].ToLowerInvariant();
        }

        private static string ToSafeFileName(string raw)
        {
            StringBuilder builder = new(raw?.Length ?? 0);
            foreach (char ch in raw ?? "")
            {
                builder.Append(char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_');
            }

            return builder.Length > 0 ? builder.ToString() : "db";
        }

        // UI と Coordinator が同じ snapshot を更新しても、競合で即死しないように吸収する。
        private static void SaveSnapshotFile(
            string snapshotFilePath,
            string json,
            string versionToken
        )
        {
            Exception lastException = null;
            for (int attempt = 0; attempt < SaveRetryCount; attempt++)
            {
                string tempFilePath = BuildTempFilePath(snapshotFilePath);
                try
                {
                    File.WriteAllText(tempFilePath, json, new UTF8Encoding(false));
                    File.Move(tempFilePath, snapshotFilePath, true);
                    return;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    lastException = ex;
                    if (TryAcceptExistingSnapshot(snapshotFilePath, versionToken))
                    {
                        return;
                    }
                }
                finally
                {
                    TryDelete(tempFilePath);
                }

                if (attempt + 1 < SaveRetryCount)
                {
                    Thread.Sleep(SaveRetryDelay);
                }
            }

            throw lastException ?? new IOException("worker settings snapshot save failed.");
        }

        private static string BuildTempFilePath(string snapshotFilePath)
        {
            string directoryPath = Path.GetDirectoryName(snapshotFilePath) ?? ResolveSnapshotDirectoryPath();
            string fileName = Path.GetFileName(snapshotFilePath);
            return Path.Combine(
                directoryPath,
                $"{fileName}.{Environment.ProcessId}.{Environment.CurrentManagedThreadId}.{Guid.NewGuid():N}.tmp"
            );
        }

        // 競合相手が同じ内容を書き終えていれば、その結果を成功扱いに寄せる。
        private static bool TryAcceptExistingSnapshot(string snapshotFilePath, string versionToken)
        {
            try
            {
                if (!File.Exists(snapshotFilePath))
                {
                    return false;
                }

                ThumbnailWorkerSettingsSnapshot existing = LoadSnapshot(snapshotFilePath);
                return existing != null
                    && string.Equals(existing.VersionToken, versionToken, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
