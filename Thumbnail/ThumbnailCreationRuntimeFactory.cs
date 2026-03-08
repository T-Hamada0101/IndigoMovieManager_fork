using IndigoMovieManager.Thumbnail.Engines;
using IndigoMovieManager.Thumbnail.Engines.IndexRepair;
using IndigoMovieManager.Thumbnail.Swf;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイル生成に必要な依存束を一箇所で組み立てる。
    /// service と worker で同じ配線を使い、責務の二重化を防ぐ。
    /// </summary>
    public static class ThumbnailCreationRuntimeFactory
    {
        public static ThumbnailCreationRuntime CreateDefault(
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger
        )
        {
            return CreateDefault(
                videoMetadataProvider,
                logger,
                videoIndexRepairService: null,
                swfThumbnailGenerationService: null
            );
        }

        internal static ThumbnailCreationRuntime CreateDefault(
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger,
            IVideoIndexRepairService videoIndexRepairService,
            SwfThumbnailGenerationService swfThumbnailGenerationService
        )
        {
            return Create(
                new FfMediaToolkitThumbnailGenerationEngine(),
                new FfmpegOnePassThumbnailGenerationEngine(),
                new OpenCvThumbnailGenerationEngine(),
                new FfmpegAutoGenThumbnailGenerationEngine(),
                videoMetadataProvider,
                logger,
                videoIndexRepairService,
                swfThumbnailGenerationService
            );
        }

        internal static ThumbnailCreationRuntime Create(
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
            IThumbnailGenerationEngine resolvedFfMediaToolkitEngine =
                ffMediaToolkitEngine
                ?? throw new ArgumentNullException(nameof(ffMediaToolkitEngine));
            IThumbnailGenerationEngine resolvedFfmpegOnePassEngine =
                ffmpegOnePassEngine ?? throw new ArgumentNullException(nameof(ffmpegOnePassEngine));
            IThumbnailGenerationEngine resolvedOpenCvEngine =
                openCvEngine ?? throw new ArgumentNullException(nameof(openCvEngine));
            IThumbnailGenerationEngine resolvedAutogenEngine =
                autogenEngine ?? throw new ArgumentNullException(nameof(autogenEngine));
            IVideoMetadataProvider resolvedVideoMetadataProvider =
                videoMetadataProvider
                ?? throw new ArgumentNullException(nameof(videoMetadataProvider));
            IThumbnailLogger resolvedLogger =
                logger ?? throw new ArgumentNullException(nameof(logger));
            IVideoIndexRepairService resolvedVideoIndexRepairService =
                videoIndexRepairService ?? new VideoIndexRepairService();
            SwfThumbnailGenerationService resolvedSwfThumbnailGenerationService =
                swfThumbnailGenerationService ?? new SwfThumbnailGenerationService();

            ThumbnailEngineRouter engineRouter = new([
                resolvedFfMediaToolkitEngine,
                resolvedFfmpegOnePassEngine,
                resolvedOpenCvEngine,
                resolvedAutogenEngine,
            ]);
            ThumbnailEngineCatalog engineCatalog = new(
                resolvedFfMediaToolkitEngine,
                resolvedFfmpegOnePassEngine,
                resolvedOpenCvEngine,
                resolvedAutogenEngine
            );
            ThumbnailEngineExecutionCoordinator engineExecutionCoordinator = new(
                engineRouter: engineRouter,
                engineCatalog: engineCatalog,
                ffmpegOnePassEngine: resolvedFfmpegOnePassEngine
            );
            ThumbnailJobMaterialBuilder jobMaterialBuilder = new(
                resolvedVideoMetadataProvider
            );
            ThumbnailRepairWorkflowCoordinator repairWorkflowCoordinator = new(
                resolvedVideoMetadataProvider,
                resolvedVideoIndexRepairService
            );
            ThumbnailRepairRerunCoordinator repairRerunCoordinator = new(
                engineExecutionCoordinator
            );
            ThumbnailRepairExecutionCoordinator repairExecutionCoordinator = new(
                repairWorkflowCoordinator,
                repairRerunCoordinator
            );
            ThumbnailExecutionFlowCoordinator executionFlowCoordinator = new(
                engineExecutionCoordinator,
                repairExecutionCoordinator
            );
            SwfThumbnailRouteHandler swfThumbnailRouteHandler = new(
                resolvedSwfThumbnailGenerationService
            );
            ThumbnailCreationOrchestrationCoordinator creationOrchestrationCoordinator = new(
                swfThumbnailRouteHandler,
                jobMaterialBuilder,
                repairExecutionCoordinator,
                executionFlowCoordinator
            );

            ThumbnailRuntimeLog.SetLogger(resolvedLogger);

            return new ThumbnailCreationRuntime(
                engineRouter,
                new ThumbnailCreationFacade(creationOrchestrationCoordinator),
                resolvedVideoIndexRepairService
            );
        }
    }
}
