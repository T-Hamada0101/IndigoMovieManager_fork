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
        public string Status { get; set; } = "";
        public int AttemptCount { get; set; }
        public string LastError { get; set; } = "";
        public string OwnerInstanceId { get; set; } = "";
        public string LeaseUntilUtc { get; set; } = "";
        public string CreatedAtUtc { get; set; } = "";
        public string UpdatedAtUtc { get; set; } = "";
    }
}
