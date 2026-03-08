using IndigoMovieManager.Thumbnail.Engines;
using IndigoMovieManager.Thumbnail.Engines.IndexRepair;
using IndigoMovieManager.Thumbnail.Swf;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// service shell が使う runtime 生成窓口。
    /// constructor 群から組み立て責務を外し、入口クラスを薄く保つ。
    /// </summary>
    internal sealed class ThumbnailCreationRuntimeProvider
    {
        private readonly ThumbnailCreationRuntime runtime;

        public ThumbnailCreationRuntimeProvider(
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger,
            IVideoIndexRepairService videoIndexRepairService = null,
            SwfThumbnailGenerationService swfThumbnailGenerationService = null
        )
        {
            runtime = ThumbnailCreationRuntimeFactory.CreateDefault(
                videoMetadataProvider,
                logger,
                videoIndexRepairService,
                swfThumbnailGenerationService
            );
        }

        public ThumbnailCreationRuntimeProvider(
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
            runtime = ThumbnailCreationRuntimeFactory.Create(
                ffMediaToolkitEngine,
                ffmpegOnePassEngine,
                openCvEngine,
                autogenEngine,
                videoMetadataProvider,
                logger,
                videoIndexRepairService,
                swfThumbnailGenerationService
            );
        }

        public ThumbnailCreationRuntime GetRuntime()
        {
            return runtime;
        }
    }
}
