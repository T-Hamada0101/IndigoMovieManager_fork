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
            ThumbnailPreviewFrame previewFrame = null,
            string engineAttempted = "",
            string failureStage = "",
            string policyDecision = "",
            string placeholderAction = "",
            string placeholderKind = ""
        )
        {
            return new ThumbnailCreateResult
            {
                SaveThumbFileName = saveThumbFileName,
                DurationSec = durationSec,
                IsSuccess = true,
                EngineAttempted = engineAttempted ?? "",
                PreviewFrame = previewFrame,
                FailureStage = failureStage ?? "",
                PolicyDecision = policyDecision ?? "",
                PlaceholderAction = placeholderAction ?? "",
                PlaceholderKind = placeholderKind ?? "",
            };
        }

        public static ThumbnailCreateResult CreateFailed(
            string saveThumbFileName,
            double? durationSec,
            string errorMessage,
            ThumbnailPreviewFrame previewFrame = null,
            string engineAttempted = "",
            string failureStage = "",
            string policyDecision = "",
            string placeholderAction = "",
            string placeholderKind = ""
        )
        {
            return new ThumbnailCreateResult
            {
                SaveThumbFileName = saveThumbFileName,
                DurationSec = durationSec,
                IsSuccess = false,
                ErrorMessage = errorMessage ?? "",
                EngineAttempted = engineAttempted ?? "",
                PreviewFrame = previewFrame,
                FailureStage = failureStage ?? "",
                PolicyDecision = policyDecision ?? "",
                PlaceholderAction = placeholderAction ?? "",
                PlaceholderKind = placeholderKind ?? "",
            };
        }
    }
}
