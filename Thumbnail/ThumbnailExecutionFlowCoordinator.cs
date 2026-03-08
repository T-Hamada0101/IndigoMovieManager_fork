using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// material 解決後の主フローをまとめる。
    /// context 組み立て、engine 実行、repair 再試行、post process を一箇所で扱う。
    /// </summary>
    internal sealed class ThumbnailExecutionFlowCoordinator
    {
        private readonly ThumbnailEngineExecutionCoordinator engineExecutionCoordinator;
        private readonly ThumbnailRepairExecutionCoordinator repairExecutionCoordinator;

        public ThumbnailExecutionFlowCoordinator(
            ThumbnailEngineExecutionCoordinator engineExecutionCoordinator,
            ThumbnailRepairExecutionCoordinator repairExecutionCoordinator
        )
        {
            this.engineExecutionCoordinator =
                engineExecutionCoordinator
                ?? throw new ArgumentNullException(nameof(engineExecutionCoordinator));
            this.repairExecutionCoordinator =
                repairExecutionCoordinator
                ?? throw new ArgumentNullException(nameof(repairExecutionCoordinator));
        }

        public async Task<ThumbnailExecutionFlowResult> ExecuteAsync(
            ThumbnailExecutionFlowRequest request,
            CancellationToken cts
        )
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ThumbnailJobContext context = ThumbnailJobContextFactory.Create(
                request.QueueObj,
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
            ThumbnailJobContext originalMovieContext = ThumbnailJobContextFactory.Create(
                request.QueueObj,
                request.TabInfo,
                request.ThumbInfo,
                request.MovieFullPath,
                request.SaveThumbFileName,
                request.IsResizeThumb,
                request.IsManual,
                request.DurationSec,
                request.FileSizeBytes,
                request.AverageBitrateMbps,
                request.VideoCodec
            );

            ThumbnailEngineExecutionResult execution = await engineExecutionCoordinator
                .ExecuteAsync(context, request.SaveThumbFileName, request.DurationSec, cts)
                .ConfigureAwait(false);
            ThumbnailCreateResult result = execution.Result;
            string processEngineId = execution.ProcessEngineId;
            List<string> engineErrorMessages = execution.EngineErrorMessages;
            ThumbnailRepairExecutionState repairState = request.RepairState;

            ThumbnailRepairExecutionApplyResult repairApply = await repairExecutionCoordinator
                .TryRepairAndRerunAsync(
                    new ThumbnailRepairExecutionApplyRequest
                    {
                        State = repairState,
                        QueueObj = request.QueueObj,
                        TabInfo = request.TabInfo,
                        ThumbInfo = request.ThumbInfo,
                        MovieFullPath = request.MovieFullPath,
                        SaveThumbFileName = request.SaveThumbFileName,
                        IsResizeThumb = request.IsResizeThumb,
                        IsManual = request.IsManual,
                        DurationSec = request.DurationSec,
                        FileSizeBytes = request.FileSizeBytes,
                        AverageBitrateMbps = request.AverageBitrateMbps,
                        Result = result,
                        EngineErrorMessages = engineErrorMessages,
                    },
                    cts
                )
                .ConfigureAwait(false);
            repairState = repairApply.State;
            if (repairApply.WasApplied)
            {
                context = repairApply.Context;
                result = repairApply.Result;
                processEngineId = repairApply.ProcessEngineId;
                engineErrorMessages = repairApply.EngineErrorMessages;
            }

            ThumbnailEnginePostProcessResult postProcess = await engineExecutionCoordinator
                .ApplyPostExecutionFallbacksAsync(
                    new ThumbnailEnginePostProcessRequest
                    {
                        Context = context,
                        OriginalMovieContext = originalMovieContext,
                        Result = result,
                        ProcessEngineId = processEngineId,
                        EngineErrorMessages = engineErrorMessages,
                        IsManual = request.IsManual,
                        IsRecoveryLane = repairState.IsRecoveryLane,
                        IsIndexRepairTargetMovie = repairState.IsIndexRepairTargetMovie,
                        RepairedByProbe = repairState.RepairedByProbe,
                        MovieFullPath = request.MovieFullPath,
                        SaveThumbFileName = request.SaveThumbFileName,
                        DurationSec = request.DurationSec,
                    },
                    cts
                )
                .ConfigureAwait(false);

            return new ThumbnailExecutionFlowResult(
                repairState,
                postProcess.Context,
                postProcess.Result,
                postProcess.ProcessEngineId,
                postProcess.EngineErrorMessages
            );
        }
    }

    internal sealed class ThumbnailExecutionFlowRequest
    {
        public ThumbnailRepairExecutionState RepairState { get; init; }

        public QueueObj QueueObj { get; init; }

        public TabInfo TabInfo { get; init; }

        public ThumbInfo ThumbInfo { get; init; }

        public string MovieFullPath { get; init; } = "";

        public string WorkingMovieFullPath { get; init; } = "";

        public string SaveThumbFileName { get; init; } = "";

        public bool IsResizeThumb { get; init; }

        public bool IsManual { get; init; }

        public double? DurationSec { get; init; }

        public long FileSizeBytes { get; init; }

        public double? AverageBitrateMbps { get; init; }

        public string VideoCodec { get; init; } = "";
    }

    internal sealed class ThumbnailExecutionFlowResult
    {
        public ThumbnailExecutionFlowResult(
            ThumbnailRepairExecutionState repairState,
            ThumbnailJobContext context,
            ThumbnailCreateResult result,
            string processEngineId,
            List<string> engineErrorMessages
        )
        {
            RepairState = repairState;
            Context = context;
            Result = result;
            ProcessEngineId = processEngineId ?? "";
            EngineErrorMessages = engineErrorMessages ?? [];
        }

        public ThumbnailRepairExecutionState RepairState { get; }

        public ThumbnailJobContext Context { get; }

        public ThumbnailCreateResult Result { get; }

        public string ProcessEngineId { get; }

        public List<string> EngineErrorMessages { get; }
    }
}
