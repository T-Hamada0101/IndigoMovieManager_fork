using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 実行文脈の生成を一箇所へ寄せる。
    /// repair 前後や original movie 再挑戦でも、同じ材料から同じ形を作る。
    /// </summary>
    internal static class ThumbnailJobContextFactory
    {
        public static ThumbnailJobContext Create(
            QueueObj queueObj,
            TabInfo tabInfo,
            ThumbInfo thumbInfo,
            string movieFullPath,
            string saveThumbFileName,
            bool isResizeThumb,
            bool isManual,
            double? durationSec,
            long fileSizeBytes,
            double? averageBitrateMbps,
            string videoCodec
        )
        {
            return new ThumbnailJobContext
            {
                QueueObj = queueObj,
                TabInfo = tabInfo,
                ThumbInfo = thumbInfo,
                MovieFullPath = movieFullPath,
                SaveThumbFileName = saveThumbFileName,
                IsResizeThumb = isResizeThumb,
                IsManual = isManual,
                DurationSec = durationSec,
                FileSizeBytes = fileSizeBytes,
                AverageBitrateMbps = averageBitrateMbps,
                HasEmojiPath = ThumbnailEngineRouter.HasUnmappableAnsiChar(movieFullPath),
                VideoCodec = videoCodec,
            };
        }
    }
}
