namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 専用Workerがどの種類のジョブを担当するかを表す。
    /// </summary>
    public enum ThumbnailQueueWorkerRole
    {
        All = 0,
        Normal = 1,
        Idle = 2,
    }
}
