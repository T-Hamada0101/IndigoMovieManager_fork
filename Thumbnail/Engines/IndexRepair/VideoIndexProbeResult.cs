namespace IndigoMovieManager.Thumbnail.Engines.IndexRepair
{
    /// <summary>
    /// 動画インデックス破損の判定結果を呼び出し側へ返すDTO。
    /// </summary>
    public sealed class VideoIndexProbeResult
    {
        public string MoviePath { get; set; } = "";
        public bool IsIndexCorruptionDetected { get; set; }
        public string DetectionReason { get; set; } = "";
        public string ContainerFormat { get; set; } = "";
        public string ErrorCode { get; set; } = "";
    }
}
