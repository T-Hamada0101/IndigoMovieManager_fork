namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 最終失敗時の後始末をまとめる。
    /// まずは error marker 出力だけをここへ寄せる。
    /// </summary>
    internal static class ThumbnailFailureFinalizer
    {
        private const int FinalFailureAttemptThreshold = 5;

        public static string WriteErrorMarkerIfNeeded(
            bool isManual,
            ThumbnailCreateResult result,
            TabInfo tabInfo,
            string movieFullPath,
            int attemptCount
        )
        {
            if (isManual || result?.IsSuccess != false || tabInfo == null)
            {
                return "skip";
            }

            // 失敗中の途中経過では固定化せず、最終失敗だけ ERROR マーカーを置く。
            if (attemptCount + 1 < FinalFailureAttemptThreshold)
            {
                return "skip-not-final";
            }

            try
            {
                string errorMarkerPath = ThumbnailPathResolver.BuildErrorMarkerPath(
                    tabInfo.OutPath,
                    movieFullPath
                );
                if (!Path.Exists(errorMarkerPath))
                {
                    File.WriteAllBytes(errorMarkerPath, []);
                    ThumbnailRuntimeLog.Write(
                        "thumbnail",
                        $"error marker created: '{errorMarkerPath}'"
                    );
                    return "created";
                }
                return "already-exists";
            }
            catch (Exception markerEx)
            {
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"error marker write failed: '{markerEx.Message}'"
                );
                return $"write-failed:{markerEx.GetType().Name}";
            }
        }

        public static string DeleteErrorMarkerIfExists(TabInfo tabInfo, string movieFullPath)
        {
            if (tabInfo == null || string.IsNullOrWhiteSpace(movieFullPath))
            {
                return "skip";
            }

            try
            {
                string errorMarkerPath = ThumbnailPathResolver.BuildErrorMarkerPath(
                    tabInfo.OutPath,
                    movieFullPath
                );
                if (!Path.Exists(errorMarkerPath))
                {
                    return "not-found";
                }

                File.Delete(errorMarkerPath);
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"error marker deleted: '{errorMarkerPath}'"
                );
                return "deleted";
            }
            catch (Exception markerEx)
            {
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"error marker delete failed: '{markerEx.Message}'"
                );
                return $"delete-failed:{markerEx.GetType().Name}";
            }
        }
    }
}
