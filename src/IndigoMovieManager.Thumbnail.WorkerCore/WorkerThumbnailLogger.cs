namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// エンジンログをWorkerランタイムログへ流す。
    /// </summary>
    internal sealed class WorkerThumbnailLogger : IThumbnailLogger
    {
        private readonly ThumbnailWorkerRuntimeLog runtimeLog;

        public WorkerThumbnailLogger(ThumbnailWorkerRuntimeLog runtimeLog)
        {
            this.runtimeLog = runtimeLog ?? throw new ArgumentNullException(nameof(runtimeLog));
        }

        public void LogDebug(string category, string message)
        {
            runtimeLog.Write(category ?? "thumbnail-debug", message ?? "");
        }

        public void LogInfo(string category, string message)
        {
            runtimeLog.Write(category ?? "thumbnail-info", $"[info] {message ?? ""}");
        }

        public void LogWarning(string category, string message)
        {
            runtimeLog.Write(category ?? "thumbnail-warn", $"[warn] {message ?? ""}");
        }

        public void LogError(string category, string message)
        {
            runtimeLog.Write(category ?? "thumbnail-error", $"[error] {message ?? ""}");
        }
    }
}
