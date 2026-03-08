namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// UI が保存するサムネイル設定の生値。
    /// Worker はこの内容から role ごとの実効値を決める。
    /// </summary>
    public sealed class ThumbnailWorkerSettingsSnapshot
    {
        public string MainDbFullPath { get; init; } = "";
        public string DbName { get; init; } = "";
        public string ThumbFolder { get; init; } = "";
        public string Preset { get; init; } = "";
        public int RequestedParallelism { get; init; } = 8;
        public int SlowLaneMinGb { get; init; } = 50;
        public bool GpuDecodeEnabled { get; init; }
        public bool ResizeThumb { get; init; }
        public bool AllowFallbackInProcess { get; init; }
        public int BasePollIntervalMs { get; init; } = 3000;
        public int LeaseMinutes { get; init; } = 5;
        public int CoordinatorNormalParallelismOverride { get; init; }
        public int CoordinatorIdleParallelismOverride { get; init; }
        public string VersionToken { get; init; } = "";
        public DateTime UpdatedAtUtc { get; init; }
    }

    /// <summary>
    /// Worker 起動時に必要な参照情報だけを返す。
    /// </summary>
    public sealed class ThumbnailWorkerSettingsSaveResult
    {
        public string SnapshotFilePath { get; init; } = "";
        public string VersionToken { get; init; } = "";
    }
}
