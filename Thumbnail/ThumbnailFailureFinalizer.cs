namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 最終失敗時の後始末をまとめる。
    /// まずは error marker 出力だけをここへ寄せる。
    /// </summary>
    internal static class ThumbnailFailureFinalizer
    {
        public static void WriteErrorMarkerIfNeeded(
            bool isManual,
            ThumbnailCreateResult result,
            TabInfo tabInfo,
            string movieFullPath
        )
        {
            if (isManual || result?.IsSuccess != false || tabInfo == null)
            {
                return;
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
                }
            }
            catch (Exception markerEx)
            {
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"error marker write failed: '{markerEx.Message}'"
                );
            }
        }
    }
}
