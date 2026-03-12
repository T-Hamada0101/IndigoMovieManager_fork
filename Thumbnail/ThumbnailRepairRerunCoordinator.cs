using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// forced repair 成功後の文脈再構成と engine 再実行をまとめる。
    /// service 側は rerun 実施判断だけを持ち、つなぎ込みはここへ寄せる。
    /// </summary>
    internal sealed class ThumbnailRepairRerunCoordinator
    {
        private readonly ThumbnailEngineExecutionCoordinator engineExecutionCoordinator;

        public ThumbnailRepairRerunCoordinator(
            ThumbnailEngineExecutionCoordinator engineExecutionCoordinator
        )
        {
            this.engineExecutionCoordinator =
                engineExecutionCoordinator
                ?? throw new ArgumentNullException(nameof(engineExecutionCoordinator));
        }

        public async Task<ThumbnailRepairRerunResult> RerunAsync(
            ThumbnailRepairRerunRequest request,
            CancellationToken cts
        )
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            QueueObj rerunQueueObj = BuildRecoveryRerunQueueObj(request.QueueObj);
            ThumbnailJobContext repairedContext = ThumbnailJobContextFactory.Create(
                rerunQueueObj,
                request.TabInfo,
                request.ThumbInfo,
                request.WorkingMovieFullPath,
                request.SaveThumbFileName,
                request.IsResizeThumb,
                request.IsManual,
                request.DurationSec,
                request.FileSizeBytes,
                request.AverageBitrateMbps,
                request.VideoCodec
            );

            ThumbnailEngineExecutionResult repairedExecution = await engineExecutionCoordinator
                .ExecuteAsync(
                    repairedContext,
                    request.SaveThumbFileName,
                    request.DurationSec,
                    cts
                )
                .ConfigureAwait(false);

            return new ThumbnailRepairRerunResult(
                repairedContext,
                repairedExecution.Result,
                repairedExecution.ProcessEngineId,
                repairedExecution.EngineErrorMessages
            );
        }

        // forced repair 後の再実行は recovery lane と同じ条件へ寄せる。
        private static QueueObj BuildRecoveryRerunQueueObj(QueueObj source)
        {
            if (source == null)
            {
                return null;
            }

            return new QueueObj
            {
                Tabindex = source.Tabindex,
                MovieId = source.MovieId,
                MovieFullPath = source.MovieFullPath,
                MainDbFullPath = source.MainDbFullPath,
                Hash = source.Hash,
                MovieSizeBytes = source.MovieSizeBytes,
                AttemptCount = Math.Max(1, source.AttemptCount),
                ThumbPanelPos = source.ThumbPanelPos,
                ThumbTimePos = source.ThumbTimePos,
                IsRescueRequest = source.IsRescueRequest,
            };
        }
    }

    internal sealed class ThumbnailRepairRerunRequest
    {
        public QueueObj QueueObj { get; init; }

        public TabInfo TabInfo { get; init; }

        public ThumbInfo ThumbInfo { get; init; }

        public string WorkingMovieFullPath { get; init; } = "";

        public string SaveThumbFileName { get; init; } = "";

        public bool IsResizeThumb { get; init; }

        public bool IsManual { get; init; }

        public double? DurationSec { get; init; }

        public long FileSizeBytes { get; init; }

        public double? AverageBitrateMbps { get; init; }

        public string VideoCodec { get; init; } = "";
    }

    internal sealed class ThumbnailRepairRerunResult
    {
        public ThumbnailRepairRerunResult(
            ThumbnailJobContext context,
            ThumbnailCreateResult result,
            string processEngineId,
            List<string> engineErrorMessages
        )
        {
            Context = context;
            Result = result;
            ProcessEngineId = processEngineId ?? "";
            EngineErrorMessages = engineErrorMessages ?? [];
        }

        public ThumbnailJobContext Context { get; }

        public ThumbnailCreateResult Result { get; }

        public string ProcessEngineId { get; }

        public List<string> EngineErrorMessages { get; }
    }
}
