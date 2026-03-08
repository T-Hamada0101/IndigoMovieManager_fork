using System.Text;
using System.Text.Json;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// DB単位の総作成枚数を永続化する。
    /// Worker複数プロセスから加算されても値が壊れないよう、名前付きMutexで直列化する。
    /// </summary>
    public static class ThumbnailCreatedCountStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

        public static int Load(string mainDbFullPath)
        {
            if (string.IsNullOrWhiteSpace(mainDbFullPath))
            {
                return 0;
            }

            return ExecuteSynchronized(mainDbFullPath, () =>
            {
                ThumbnailCreatedCountSnapshot snapshot = LoadCore(mainDbFullPath);
                return Math.Max(0, snapshot?.TotalCreatedCount ?? 0);
            });
        }

        public static int Increment(string mainDbFullPath, int delta = 1)
        {
            if (string.IsNullOrWhiteSpace(mainDbFullPath))
            {
                return 0;
            }

            return ExecuteSynchronized(mainDbFullPath, () =>
            {
                ThumbnailCreatedCountSnapshot current = LoadCore(mainDbFullPath) ?? new();
                int nextCount = Math.Max(0, current.TotalCreatedCount + Math.Max(0, delta));
                SaveCore(
                    mainDbFullPath,
                    new ThumbnailCreatedCountSnapshot
                    {
                        MainDbFullPath = mainDbFullPath,
                        TotalCreatedCount = nextCount,
                        UpdatedAtUtc = DateTime.UtcNow,
                    }
                );
                return nextCount;
            });
        }

        private static T ExecuteSynchronized<T>(string mainDbFullPath, Func<T> action)
        {
            string hash8 = QueueDbPathResolver.GetMainDbPathHash8(mainDbFullPath);
            using Mutex mutex = new(false, $@"Local\IndigoMovieManager_fork_ThumbCreated_{hash8}");
            bool lockTaken = false;
            try
            {
                try
                {
                    lockTaken = mutex.WaitOne(TimeSpan.FromSeconds(5));
                }
                catch (AbandonedMutexException)
                {
                    lockTaken = true;
                }

                return action != null ? action() : default;
            }
            finally
            {
                if (lockTaken)
                {
                    try
                    {
                        mutex.ReleaseMutex();
                    }
                    catch
                    {
                        // 解放失敗で本体処理は止めない。
                    }
                }
            }
        }

        private static ThumbnailCreatedCountSnapshot LoadCore(string mainDbFullPath)
        {
            string filePath = ResolveSnapshotPath(mainDbFullPath);
            if (!File.Exists(filePath))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(filePath, Encoding.UTF8);
                ThumbnailCreatedCountSnapshot snapshot =
                    JsonSerializer.Deserialize<ThumbnailCreatedCountSnapshot>(json, JsonOptions);
                if (snapshot == null)
                {
                    return null;
                }

                if (
                    !string.IsNullOrWhiteSpace(snapshot.MainDbFullPath)
                    && !string.Equals(
                        snapshot.MainDbFullPath,
                        mainDbFullPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return null;
                }

                return snapshot;
            }
            catch
            {
                return null;
            }
        }

        private static void SaveCore(string mainDbFullPath, ThumbnailCreatedCountSnapshot snapshot)
        {
            string directoryPath = ResolveDirectoryPath();
            string filePath = ResolveSnapshotPath(mainDbFullPath);
            string tempPath = filePath + ".tmp";
            Directory.CreateDirectory(directoryPath);
            string json = JsonSerializer.Serialize(snapshot, JsonOptions);
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));
            File.Move(tempPath, filePath, true);
        }

        private static string ResolveSnapshotPath(string mainDbFullPath)
        {
            string hash8 = QueueDbPathResolver.GetMainDbPathHash8(mainDbFullPath);
            return Path.Combine(ResolveDirectoryPath(), $"thumbnail-created-count-{hash8}.json");
        }

        private static string ResolveDirectoryPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IndigoMovieManager_fork",
                "progress"
            );
        }

        private sealed class ThumbnailCreatedCountSnapshot
        {
            public string MainDbFullPath { get; init; } = "";
            public int TotalCreatedCount { get; init; }
            public DateTime UpdatedAtUtc { get; init; }
        }
    }
}
