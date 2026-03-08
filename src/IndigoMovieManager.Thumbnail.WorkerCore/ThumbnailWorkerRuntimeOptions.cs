namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// Worker起動時に必要な固定設定をひとまとめにする。
    /// </summary>
    public sealed class ThumbnailWorkerRuntimeOptions
    {
        public string MainDbFullPath { get; init; } = "";
        public string OwnerInstanceId { get; init; } = "";
        public string SettingsSnapshotPath { get; init; } = "";
        public ThumbnailQueueWorkerRole WorkerRole { get; init; } =
            ThumbnailQueueWorkerRole.All;
        public int ParentProcessId { get; init; }
    }
}
