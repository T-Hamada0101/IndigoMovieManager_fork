using System.IO;
using IndigoMovieManager.Thumbnail.Engines.IndexRepair;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイル生成の実行面をまとめたランタイム束。
    /// service と worker が同じ入口規約を共有するための受け皿。
    /// </summary>
    public sealed class ThumbnailCreationRuntime
    {
        private readonly ThumbnailCreationFacade creationFacade;

        internal ThumbnailEngineRouter EngineRouter { get; }

        internal ThumbnailCreationFacade CreationFacade => creationFacade;

        internal IVideoIndexRepairService VideoIndexRepairService { get; }

        internal ThumbnailCreationRuntime(
            ThumbnailEngineRouter engineRouter,
            ThumbnailCreationFacade creationFacade,
            IVideoIndexRepairService videoIndexRepairService
        )
        {
            EngineRouter = engineRouter ?? throw new ArgumentNullException(nameof(engineRouter));
            this.creationFacade =
                creationFacade ?? throw new ArgumentNullException(nameof(creationFacade));
            VideoIndexRepairService =
                videoIndexRepairService
                ?? throw new ArgumentNullException(nameof(videoIndexRepairService));
        }

        public Task<ThumbnailCreateResult> CreateThumbAsync(
            QueueObj queueObj,
            string dbName,
            string thumbFolder,
            bool isResizeThumb,
            bool isManual = false,
            CancellationToken cts = default
        )
        {
            return creationFacade.ExecuteAsync(
                queueObj,
                dbName,
                thumbFolder,
                isResizeThumb,
                isManual,
                cts
            );
        }

        public async Task<bool> CreateBookmarkThumbAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePos,
            CancellationToken cts = default
        )
        {
            if (!Path.Exists(movieFullPath))
            {
                return false;
            }

            IThumbnailGenerationEngine engine = EngineRouter.ResolveForBookmark();
            try
            {
                return await engine.CreateBookmarkAsync(
                    movieFullPath,
                    saveThumbPath,
                    capturePos,
                    cts
                );
            }
            catch (Exception ex)
            {
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"bookmark create failed: engine={engine.EngineId}, movie='{movieFullPath}', err='{ex.Message}'"
                );
                return false;
            }
        }

        public Task<VideoIndexProbeResult> ProbeVideoIndexAsync(
            string moviePath,
            CancellationToken cts = default
        )
        {
            return VideoIndexRepairService.ProbeAsync(moviePath, cts);
        }

        public Task<VideoIndexRepairResult> RepairVideoIndexAsync(
            string moviePath,
            string outputPath,
            CancellationToken cts = default
        )
        {
            return VideoIndexRepairService.RepairAsync(moviePath, outputPath, cts);
        }
    }
}
