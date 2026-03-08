using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// Worker 設定スナップショットをファイルへ保存し、UI と Worker の橋渡しを行う。
    /// </summary>
    public static class ThumbnailWorkerSettingsStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

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
                string tempFilePath = snapshotFilePath + ".tmp";
                File.WriteAllText(tempFilePath, json, new UTF8Encoding(false));
                File.Move(tempFilePath, snapshotFilePath, true);
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
    }
}
