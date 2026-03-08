namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイル処理の最終ログ記録をまとめる。
    /// service 側は戻り値の決定に集中し、CSV出力の詳細はここへ寄せる。
    /// </summary>
    internal static class ThumbnailProcessLogFinalizer
    {
        private const string ThumbnailProcessLogFileName = "thumbnail-create-process.csv";
        private static readonly object SyncRoot = new();

        public static void Write(
            string engineId,
            string movieFullPath,
            string codec,
            double? durationSec,
            long fileSizeBytes,
            ThumbnailCreateResult result
        )
        {
            if (result == null)
            {
                return;
            }

            ThumbnailCsvUtility.WriteThumbnailCreateProcessLog(
                logFileName: ThumbnailProcessLogFileName,
                syncRoot: SyncRoot,
                engineId,
                movieFullPath,
                codec,
                durationSec,
                fileSizeBytes,
                result.SaveThumbFileName,
                result.IsSuccess,
                result.ErrorMessage
            );
        }
    }
}
