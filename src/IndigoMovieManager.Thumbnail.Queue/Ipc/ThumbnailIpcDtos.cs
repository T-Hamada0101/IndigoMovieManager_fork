namespace IndigoMovieManager.Thumbnail.Ipc
{
    // IPCで共有するUTC時刻は、未指定/ローカル時刻が来てもここで揃える。
    internal static class ThumbnailIpcDateTimeNormalizer
    {
        internal static DateTime NormalizeUtc(DateTime value)
        {
            if (value == DateTime.MinValue || value == DateTime.MaxValue)
            {
                return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            }

            if (value.Kind == DateTimeKind.Local)
            {
                return value.ToUniversalTime();
            }

            if (value.Kind == DateTimeKind.Unspecified)
            {
                return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            }

            return value;
        }
    }

    public enum EngineJobLaneKind
    {
        Normal = 0,
        Slow = 1,
    }

    public enum EngineFailureKind
    {
        None = 0,
        Io = 1,
        Decode = 2,
        Index = 3,
        Timeout = 4,
        Unknown = 5,
    }

    public enum DiskThermalState
    {
        Normal = 0,
        Warning = 1,
        Critical = 2,
        Unavailable = 3,
    }

    public enum UsnMftStatusKind
    {
        Ready = 0,
        Busy = 1,
        Unavailable = 2,
        AccessDenied = 3,
    }

    public enum ThrottleDecisionKind
    {
        Keep = 0,
        ThrottleDown = 1,
        RecoverUp = 2,
    }

    public enum ThrottleReasonKind
    {
        Error = 0,
        HighLoad = 1,
        Thermal = 2,
        Manual = 3,
        Fallback = 4,
    }

    // エンジンが返す局所メトリクスを、後段のIPCへそのまま流せる形で持つ。
    public sealed record EngineJobMetricsDto
    {
        private DateTime capturedAtUtc = DateTime.UtcNow;

        public string JobId { get; init; } = "";
        public string SourcePath { get; init; } = "";
        public EngineJobLaneKind LaneKind { get; init; } = EngineJobLaneKind.Normal;
        public long ElapsedMs { get; init; }
        public bool Succeeded { get; init; }
        public EngineFailureKind FailureKind { get; init; } = EngineFailureKind.None;
        public int AttemptCount { get; init; }
        public int DecodedFrameCount { get; init; }
        public int PeakWorkingSetMb { get; init; }

        public DateTime CapturedAtUtc
        {
            get => capturedAtUtc;
            init => capturedAtUtc = ThumbnailIpcDateTimeNormalizer.NormalizeUtc(value);
        }
    }

    // 管理者権限サービス未接続時は、この形を内部メトリクス由来で埋める前提にする。
    public sealed record SystemLoadSnapshotDto
    {
        private DateTime capturedAtUtc = DateTime.UtcNow;

        public double CpuUsageRate { get; init; }
        public double IoBusyRate { get; init; }
        public double MemoryPressureRate { get; init; }
        public int QueueBacklogCount { get; init; }
        public int SlowLaneBacklogCount { get; init; }
        public long SampleWindowMs { get; init; }

        public DateTime CapturedAtUtc
        {
            get => capturedAtUtc;
            init => capturedAtUtc = ThumbnailIpcDateTimeNormalizer.NormalizeUtc(value);
        }
    }

    // 温度値が取れなくてもnullへ逃がさず、状態値でUnavailableを返す前提にする。
    public sealed record DiskThermalSnapshotDto
    {
        private DateTime capturedAtUtc = DateTime.UtcNow;

        public string DiskId { get; init; } = "";
        public int TemperatureCelsius { get; init; }
        public int WarningThresholdCelsius { get; init; }
        public int CriticalThresholdCelsius { get; init; }
        public DiskThermalState ThermalState { get; init; } = DiskThermalState.Unavailable;

        public DateTime CapturedAtUtc
        {
            get => capturedAtUtc;
            init => capturedAtUtc = ThumbnailIpcDateTimeNormalizer.NormalizeUtc(value);
        }
    }

    // UsnMft側もnullではなく状態値で返し、アクセス拒否と未接続を分ける。
    public sealed record UsnMftStatusDto
    {
        private DateTime capturedAtUtc = DateTime.UtcNow;

        public string VolumeName { get; init; } = "";
        public bool Available { get; init; }
        public long LastScanLatencyMs { get; init; }
        public int JournalBacklogCount { get; init; }
        public UsnMftStatusKind StatusKind { get; init; } = UsnMftStatusKind.Unavailable;

        public DateTime CapturedAtUtc
        {
            get => capturedAtUtc;
            init => capturedAtUtc = ThumbnailIpcDateTimeNormalizer.NormalizeUtc(value);
        }
    }

    // 縮退判断のスナップショットは、表示とログの両方でそのまま再利用できる形にする。
    public sealed record ThrottleDecisionDto
    {
        private DateTime cooldownUntilUtc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        private DateTime capturedAtUtc = DateTime.UtcNow;

        public int ConfiguredParallelism { get; init; }
        public int EffectiveParallelism { get; init; }
        public ThrottleDecisionKind DecisionKind { get; init; } = ThrottleDecisionKind.Keep;
        public ThrottleReasonKind ReasonKind { get; init; } = ThrottleReasonKind.Manual;
        public string ReasonDetail { get; init; } = "";

        public DateTime CooldownUntilUtc
        {
            get => cooldownUntilUtc;
            init => cooldownUntilUtc = ThumbnailIpcDateTimeNormalizer.NormalizeUtc(value);
        }

        public DateTime CapturedAtUtc
        {
            get => capturedAtUtc;
            init => capturedAtUtc = ThumbnailIpcDateTimeNormalizer.NormalizeUtc(value);
        }
    }

    // Workerへ渡す要求はUIの状態を持たず、ファイル、成果物、診断文脈だけで閉じる。
    public sealed record WorkerJobRequestDto
    {
        private DateTime requestedAtUtc = DateTime.UtcNow;

        public string JobId { get; init; } = "";
        public string Kind { get; init; } = "";
        public List<string> InputFiles { get; init; } = [];
        public string OutputArtifactPath { get; init; } = "";
        public long TimeoutMs { get; init; }
        public List<string> Capabilities { get; init; } = [];
        public Dictionary<string, string> DiagnosticContext { get; init; } = [];

        public DateTime RequestedAtUtc
        {
            get => requestedAtUtc;
            init => requestedAtUtc = ThumbnailIpcDateTimeNormalizer.NormalizeUtc(value);
        }
    }

    // 成果物は種別と実体パスを分け、将来のin-process/sidecar差し替えでも同じ形で扱う。
    public sealed record WorkerJobArtifactDto
    {
        public string ArtifactKind { get; init; } = "";
        public string Path { get; init; } = "";
        public string ContentType { get; init; } = "";
        public long SizeBytes { get; init; }
        public string Sha256 { get; init; } = "";
        public Dictionary<string, string> Metadata { get; init; } = [];
    }

    // 進捗は表示文言ではなく段階と件数を正本にし、UI側の見せ方を契約へ混ぜない。
    public sealed record WorkerJobProgressDto
    {
        private DateTime capturedAtUtc = DateTime.UtcNow;

        public string JobId { get; init; } = "";
        public string Stage { get; init; } = "";
        public int CompletedCount { get; init; }
        public int TotalCount { get; init; }
        public string CurrentInputFile { get; init; } = "";
        public string Message { get; init; } = "";
        public Dictionary<string, string> Metrics { get; init; } = [];

        public DateTime CapturedAtUtc
        {
            get => capturedAtUtc;
            init => capturedAtUtc = ThumbnailIpcDateTimeNormalizer.NormalizeUtc(value);
        }
    }

    // Worker結果はstatus、artifact、failure、retryabilityを分け、ログだけに意味を閉じ込めない。
    public sealed record WorkerJobResultDto
    {
        private DateTime finishedAtUtc = DateTime.UtcNow;

        public string JobId { get; init; } = "";
        public string Status { get; init; } = "";
        public WorkerJobArtifactDto Artifact { get; init; } = new();
        public string FailureReason { get; init; } = "";
        public long ElapsedMs { get; init; }
        public string Retryability { get; init; } = "";
        public List<string> Logs { get; init; } = [];
        public Dictionary<string, string> Metrics { get; init; } = [];

        public DateTime FinishedAtUtc
        {
            get => finishedAtUtc;
            init => finishedAtUtc = ThumbnailIpcDateTimeNormalizer.NormalizeUtc(value);
        }
    }
}
