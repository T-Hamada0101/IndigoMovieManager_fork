namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイル生成結果の生成を一箇所へ寄せる。
    /// engine / coordinator から service 本体を経由せず結果を作れるようにする。
    /// </summary>
    internal static class ThumbnailResultFactory
    {
        public static ThumbnailCreateResult CreateSuccess(
            string saveThumbFileName,
            double? durationSec,
            ThumbnailPreviewFrame previewFrame = null
        )
        {
            return new ThumbnailCreateResult
            {
                SaveThumbFileName = saveThumbFileName,
                DurationSec = durationSec,
                IsSuccess = true,
                PreviewFrame = previewFrame,
            };
        }

        public static ThumbnailCreateResult CreateFailed(
            string saveThumbFileName,
            double? durationSec,
            string errorMessage,
            ThumbnailPreviewFrame previewFrame = null
        )
        {
            return new ThumbnailCreateResult
            {
                SaveThumbFileName = saveThumbFileName,
                DurationSec = durationSec,
                IsSuccess = false,
                ErrorMessage = errorMessage ?? "",
                PreviewFrame = previewFrame,
            };
        }
    }
}
