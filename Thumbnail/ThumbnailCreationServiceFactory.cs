using IndigoMovieManager.Thumbnail.Engines;
using IndigoMovieManager.Thumbnail.Engines.IndexRepair;
using IndigoMovieManager.Thumbnail.Swf;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// テストや内部配線用に service shell を組み立てる。
    /// service 本体から internal constructor 群を外し、組み立てはここへ寄せる。
    /// </summary>
    internal static class ThumbnailCreationServiceFactory
    {
        public static ThumbnailCreationService Create(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine
        )
        {
            return new ThumbnailCreationService(
                new ThumbnailCreationRuntimeProvider(
                    ffMediaToolkitEngine,
                    ffmpegOnePassEngine,
                    openCvEngine,
                    autogenEngine,
                    NoOpVideoMetadataProvider.Instance,
                    NoOpThumbnailLogger.Instance,
                    new VideoIndexRepairService(),
                    null
                )
            );
        }

        public static ThumbnailCreationService Create(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine,
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger,
            IVideoIndexRepairService videoIndexRepairService = null,
            SwfThumbnailGenerationService swfThumbnailGenerationService = null
        )
        {
            return new ThumbnailCreationService(
                new ThumbnailCreationRuntimeProvider(
                    ffMediaToolkitEngine,
                    ffmpegOnePassEngine,
                    openCvEngine,
                    autogenEngine,
                    videoMetadataProvider,
                    logger,
                    videoIndexRepairService,
                    swfThumbnailGenerationService
                )
            );
        }

        public static ThumbnailCreationService Create(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine,
            SwfThumbnailGenerationService swfThumbnailGenerationService
        )
        {
            return new ThumbnailCreationService(
                new ThumbnailCreationRuntimeProvider(
                    ffMediaToolkitEngine,
                    ffmpegOnePassEngine,
                    openCvEngine,
                    autogenEngine,
                    NoOpVideoMetadataProvider.Instance,
                    NoOpThumbnailLogger.Instance,
                    new VideoIndexRepairService(),
                    swfThumbnailGenerationService
                )
            );
        }
    }
}
