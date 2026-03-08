using System.Text;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// Worker専用の実行ログをファイルへ残す。
    /// </summary>
    internal sealed class ThumbnailWorkerRuntimeLog
    {
        private readonly object writeLock = new();
        private readonly string logFilePath;

        public ThumbnailWorkerRuntimeLog(string ownerInstanceId)
        {
            string safeOwner = string.IsNullOrWhiteSpace(ownerInstanceId)
                ? "worker"
                : ownerInstanceId.Replace(':', '_');
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IndigoMovieManager_fork",
                "logs"
            );
            Directory.CreateDirectory(logDir);
            logFilePath = Path.Combine(logDir, $"thumbnail-worker-{safeOwner}.log");
        }

        public void Write(string category, string message)
        {
            string line =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\t{category}\t{message ?? ""}";
            lock (writeLock)
            {
                using StreamWriter writer = new(
                    logFilePath,
                    append: true,
                    encoding: new UTF8Encoding(false)
                );
                writer.WriteLine(line);
            }
        }
    }
}
