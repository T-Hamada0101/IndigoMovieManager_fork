using System.Text;
using System.Text.Json;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 本体UIや外部運転席UIが Coordinator へ渡す指示内容。
    /// 接続点を先に固定し、実装側は後からでも並列に進められるようにする。
    /// </summary>
    public sealed class ThumbnailCoordinatorCommandSnapshot
    {
        public int SchemaVersion { get; init; } = 1;
        public string MainDbFullPath { get; init; } = "";
        public string DbName { get; init; } = "";
        public string OwnerInstanceId { get; init; } = "";
        public int RequestedParallelism { get; init; }
        public int TemporaryParallelismDelta { get; init; }
        public int LargeMovieThresholdGb { get; init; }
        public bool GpuDecodeEnabled { get; init; }
        public string OperationMode { get; init; } = "";
        public string IssuedBy { get; init; } = "";
        public DateTime IssuedAtUtc { get; init; }
    }

    /// <summary>
    /// Coordinator command snapshot の保存と読取を扱う。
    /// </summary>
    public static class ThumbnailCoordinatorCommandStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

        public static void Save(ThumbnailCoordinatorCommandSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.OwnerInstanceId))
            {
                return;
            }

            string directoryPath = ThumbnailProgressExternalSnapshotStore.ResolveSnapshotDirectoryPath();
            string safeOwner = ThumbnailProgressExternalSnapshotStore.ToSafeOwnerFileName(
                snapshot.OwnerInstanceId
            );
            string snapshotPath = Path.Combine(directoryPath, $"thumbnail-command-{safeOwner}.json");
            string tempPath = Path.Combine(directoryPath, $"thumbnail-command-{safeOwner}.tmp");

            try
            {
                Directory.CreateDirectory(directoryPath);
                string json = JsonSerializer.Serialize(snapshot, JsonOptions);
                File.WriteAllText(tempPath, json, new UTF8Encoding(false));
                File.Move(tempPath, snapshotPath, true);
            }
            catch
            {
                // command 更新失敗で本処理は止めない。
            }
        }

        public static ThumbnailCoordinatorCommandSnapshot LoadLatest(
            string mainDbFullPath,
            string ownerInstanceId,
            TimeSpan maxAge
        )
        {
            if (string.IsNullOrWhiteSpace(ownerInstanceId))
            {
                return null;
            }

            string directoryPath = ThumbnailProgressExternalSnapshotStore.ResolveSnapshotDirectoryPath();
            if (!Directory.Exists(directoryPath))
            {
                return null;
            }

            string safeOwner = ThumbnailProgressExternalSnapshotStore.ToSafeOwnerFileName(ownerInstanceId);
            string snapshotPath = Path.Combine(directoryPath, $"thumbnail-command-{safeOwner}.json");
            if (!File.Exists(snapshotPath))
            {
                return null;
            }

            try
            {
                string json;
                using (
                    FileStream stream = new(
                        snapshotPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete
                    )
                )
                using (StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    json = reader.ReadToEnd();
                }

                ThumbnailCoordinatorCommandSnapshot snapshot =
                    JsonSerializer.Deserialize<ThumbnailCoordinatorCommandSnapshot>(json, JsonOptions);
                if (snapshot == null)
                {
                    return null;
                }

                if (
                    !string.IsNullOrWhiteSpace(mainDbFullPath)
                    && !string.Equals(
                        snapshot.MainDbFullPath,
                        mainDbFullPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return null;
                }

                if (
                    maxAge > TimeSpan.Zero
                    && snapshot.IssuedAtUtc > DateTime.MinValue
                    && DateTime.UtcNow - snapshot.IssuedAtUtc > maxAge
                )
                {
                    return null;
                }

                return snapshot;
            }
            catch
            {
                // 壊れた command は読み飛ばす。
                return null;
            }
        }
    }
}
