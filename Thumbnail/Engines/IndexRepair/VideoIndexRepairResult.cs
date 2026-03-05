namespace IndigoMovieManager.Thumbnail.Engines.IndexRepair
{
    /// <summary>
    /// 動画インデックス修復の実行結果を返すDTO。
    /// </summary>
    public sealed class VideoIndexRepairResult
    {
        public bool IsSuccess { get; set; }
        public string InputPath { get; set; } = "";
        public string OutputPath { get; set; } = "";
        public bool UsedTemporaryRemux { get; set; }
        public string ErrorMessage { get; set; } = "";
    }
}
