namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// DB切替後に旧DBジョブが流れ込んだ時の識別例外。
    /// Failed へ落とさず Pending へ戻すために使う。
    /// </summary>
    public sealed class ThumbnailMainDbScopeChangedException : InvalidOperationException
    {
        public ThumbnailMainDbScopeChangedException(
            string requestedMainDbFullPath,
            string currentMainDbFullPath,
            string movieFullPath
        )
            : base(
                $"thumbnail main-db scope changed: requested='{requestedMainDbFullPath}' current='{currentMainDbFullPath}' movie='{movieFullPath}'"
            )
        {
            RequestedMainDbFullPath = requestedMainDbFullPath ?? "";
            CurrentMainDbFullPath = currentMainDbFullPath ?? "";
            MovieFullPath = movieFullPath ?? "";
        }

        public string RequestedMainDbFullPath { get; }

        public string CurrentMainDbFullPath { get; }

        public string MovieFullPath { get; }
    }
}
