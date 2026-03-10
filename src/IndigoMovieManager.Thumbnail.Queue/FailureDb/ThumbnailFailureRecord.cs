namespace IndigoMovieManager.Thumbnail.FailureDb
{
    // 失敗分類は固定値で持ち、UIや分析側で文字列解析しない前提にする。
    public enum ThumbnailFailureKind
    {
        None = 0,
        DrmProtected = 1,
        FlashContent = 2,
        UnsupportedCodec = 3,
        IndexCorruption = 4,
        ContainerMetadataBroken = 5,
        TransientDecodeFailure = 6,
        ShortClipStillLike = 7,
        NoVideoStream = 8,
        FileLocked = 9,
        FileMissing = 10,
        ZeroByteFile = 11,
        PhysicalCorruption = 12,
        HangSuspected = 13,
        ManualCaptureRequired = 14,
        Unknown = 15,
    }

    // サムネ失敗専用DBへappend保存する1レコード。
    public sealed class ThumbnailFailureRecord
    {
        public long RecordId { get; set; }
        public string DbName { get; set; } = "";
        public string MainDbFullPath { get; set; } = "";
        public string MainDbPathHash { get; set; } = "";
        public string MoviePath { get; set; } = "";
        public string MoviePathKey { get; set; } = "";
        public string PanelType { get; set; } = "";
        public long MovieSizeBytes { get; set; }
        public double? Duration { get; set; }
        public string Reason { get; set; } = "";
        public ThumbnailFailureKind FailureKind { get; set; } = ThumbnailFailureKind.Unknown;
        public int AttemptCount { get; set; }
        public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        public int? TabIndex { get; set; }
        public string OwnerInstanceId { get; set; } = "";
        public string WorkerRole { get; set; } = "";
        public string EngineId { get; set; } = "";
        public string QueueStatus { get; set; } = "";
        public string LeaseUntilUtc { get; set; } = "";
        public string StartedAtUtc { get; set; } = "";
        public string LastError { get; set; } = "";
        public string ExtraJson { get; set; } = "";
    }
}
