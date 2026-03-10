namespace IndigoMovieManager.ModelViews
{
    // サムネ失敗タブの1行表示に必要な情報を保持する。
    public sealed class ThumbnailFailedRecordViewModel
    {
        public long QueueId { get; set; }
        public string MainDbPathHash { get; set; } = "";
        public string MoviePath { get; set; } = "";
        public string MoviePathKey { get; set; } = "";
        public int TabIndex { get; set; }
        public long MovieSizeBytes { get; set; }
        public int? ThumbPanelPos { get; set; }
        public int? ThumbTimePos { get; set; }
        public string PanelType { get; set; } = "";
        public string Status { get; set; } = "";
        public string FailureKind { get; set; } = "";
        public string Reason { get; set; } = "";
        public int AttemptCount { get; set; }
        public string LastError { get; set; } = "";
        public string OwnerInstanceId { get; set; } = "";
        public string WorkerRole { get; set; } = "";
        public string EngineId { get; set; } = "";
        public string LeaseUntilUtc { get; set; } = "";
        public string StartedAtUtc { get; set; } = "";
        public string FailureKindSource { get; set; } = "";
        public double? MaterialDurationSec { get; set; }
        public string EngineAttempted { get; set; } = "";
        public bool EngineSucceeded { get; set; }
        public string SeekStrategy { get; set; } = "";
        public double? SeekSec { get; set; }
        public bool RepairAttempted { get; set; }
        public bool RepairSucceeded { get; set; }
        public string PreflightBranch { get; set; } = "";
        public string ResultSignature { get; set; } = "";
        public bool ReproConfirmed { get; set; }
        public string RecoveryRoute { get; set; } = "";
        public string DecisionBasis { get; set; } = "";
        public bool WasRunning { get; set; }
        public int? AttemptCountAfter { get; set; }
        public bool MovieExists { get; set; }
        public string ResultFailureStage { get; set; } = "";
        public string ResultPolicyDecision { get; set; } = "";
        public string ResultPlaceholderAction { get; set; } = "";
        public string ResultPlaceholderKind { get; set; } = "";
        public string ResultFinalizerAction { get; set; } = "";
        public string ResultFinalizerDetail { get; set; } = "";
        public string CreatedAtUtc { get; set; } = "";
        public string UpdatedAtUtc { get; set; } = "";
    }
}
