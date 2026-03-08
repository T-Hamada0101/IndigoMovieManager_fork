namespace IndigoMovieManager.Thumbnail.Ipc
{
    // 管理者サービスとの往復は、コマンド名 + 必要引数だけを薄く持つ。
    public sealed record AdminTelemetryPipeRequest
    {
        public string Command { get; init; } = "";
        public AdminTelemetryRequestContext RequestContext { get; init; } = new();
        public string DiskId { get; init; } = "";
        public string VolumeName { get; init; } = "";
        public AdminFileIndexQueryDto FileIndexQuery { get; init; } = new();
    }

    // 返却値は成功時のDTOと失敗種別を同じ器へまとめ、client側で例外へ戻す。
    public sealed record AdminTelemetryPipeResponse
    {
        public string ErrorKind { get; init; } = "";
        public string ErrorMessage { get; init; } = "";
        public AdminTelemetryServiceCapabilities Capabilities { get; init; } = new();
        public SystemLoadSnapshotDto SystemLoadSnapshot { get; init; } = new();
        public DiskThermalSnapshotDto DiskThermalSnapshot { get; init; } = new();
        public UsnMftStatusDto UsnMftStatus { get; init; } = new();
        public AdminFileIndexMovieResultDto FileIndexMovieResult { get; init; } = new();
    }

    public static class AdminTelemetryPipeCommands
    {
        public const string GetCapabilities = "get-capabilities";
        public const string GetSystemLoadSnapshot = "get-system-load-snapshot";
        public const string GetDiskThermalSnapshot = "get-disk-thermal-snapshot";
        public const string GetUsnMftStatus = "get-usnmft-status";
        public const string CollectMoviePaths = "collect-movie-paths";
    }

    public static class AdminTelemetryPipeErrorKinds
    {
        public const string None = "";
        public const string AccessDenied = "access-denied";
        public const string InvalidRequest = "invalid-request";
        public const string InternalError = "internal-error";
    }

    // Watcher から管理者サービスへ渡す動画候補収集条件。
    public sealed record AdminFileIndexQueryDto
    {
        private DateTime? changedSinceUtc;

        public string RootPath { get; init; } = "";
        public bool IncludeSubdirectories { get; init; }
        public string CheckExt { get; init; } = "";
        public DateTime? ChangedSinceUtc
        {
            get => changedSinceUtc;
            init => changedSinceUtc = value.HasValue
                ? ThumbnailIpcDateTimeNormalizer.NormalizeUtc(value.Value)
                : null;
        }
    }

    // 動画候補は service 側で絞り込んで返し、pipe 転送量を抑える。
    public sealed record AdminFileIndexMovieResultDto
    {
        private DateTime? maxObservedChangedUtc;

        public bool Success { get; init; }
        public List<string> MoviePaths { get; init; } = [];
        public DateTime? MaxObservedChangedUtc
        {
            get => maxObservedChangedUtc;
            init => maxObservedChangedUtc = value.HasValue
                ? ThumbnailIpcDateTimeNormalizer.NormalizeUtc(value.Value)
                : null;
        }
        public string Reason { get; init; } = "";
    }
}
