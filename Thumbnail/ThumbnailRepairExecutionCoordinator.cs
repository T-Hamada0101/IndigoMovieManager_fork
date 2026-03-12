using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// repair 準備から forced repair 後 rerun までの流れを束ねる。
    /// service は state を受け渡し、repair 固有の判断と更新はここへ寄せる。
    /// </summary>
    internal sealed class ThumbnailRepairExecutionCoordinator
    {
        private readonly ThumbnailRepairWorkflowCoordinator repairWorkflowCoordinator;
        private readonly ThumbnailRepairRerunCoordinator repairRerunCoordinator;

        public ThumbnailRepairExecutionCoordinator(
            ThumbnailRepairWorkflowCoordinator repairWorkflowCoordinator,
            ThumbnailRepairRerunCoordinator repairRerunCoordinator
        )
        {
            this.repairWorkflowCoordinator =
                repairWorkflowCoordinator
                ?? throw new ArgumentNullException(nameof(repairWorkflowCoordinator));
            this.repairRerunCoordinator =
                repairRerunCoordinator
                ?? throw new ArgumentNullException(nameof(repairRerunCoordinator));
        }

        public async Task<ThumbnailRepairExecutionState> PrepareAsync(
            QueueObj queueObj,
            string movieFullPath,
            CancellationToken cts
        )
        {
            bool isRecoveryLane = ThumbnailExecutionPolicy.IsRecoveryLaneAttempt(queueObj);
            bool isIndexRepairTargetMovie = ThumbnailExecutionPolicy.IsIndexRepairTargetMovie(
                movieFullPath
            );

            ThumbnailRepairPreparationResult initialRepair = await repairWorkflowCoordinator
                .PrepareWorkingMovieAsync(
                    movieFullPath,
                    isRecoveryLane,
                    isIndexRepairTargetMovie,
                    cts
                )
                .ConfigureAwait(false);

            return new ThumbnailRepairExecutionState(
                isRecoveryLane,
                isIndexRepairTargetMovie,
                initialRepair.RepairedByProbe,
                initialRepair.WorkingMovieFullPath,
                initialRepair.RepairedMovieTempPath
            );
        }

        public async Task<ThumbnailRepairExecutionApplyResult> TryRepairAndRerunAsync(
            ThumbnailRepairExecutionApplyRequest request,
            CancellationToken cts
        )
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ThumbnailForcedRepairResult forcedRepair = await repairWorkflowCoordinator
                .TryRepairAfterFailureAsync(
                    new ThumbnailForcedRepairRequest
                    {
                        IsManual = request.IsManual,
                        ResultIsSuccess = request.Result.IsSuccess,
                        IsRecoveryLane = request.State.IsRecoveryLane,
                        IsIndexRepairTargetMovie = request.State.IsIndexRepairTargetMovie,
                        RepairedByProbe = request.State.RepairedByProbe,
                        InitialOnePassAttempted = request.InitialOnePassAttempted,
                        DurationSec = request.DurationSec,
                        ResultErrorMessage = request.Result.ErrorMessage,
                        EngineErrorMessages = request.EngineErrorMessages,
                        MovieFullPath = request.MovieFullPath,
                    },
                    cts
                )
                .ConfigureAwait(false);

            if (!forcedRepair.WasApplied)
            {
                return ThumbnailRepairExecutionApplyResult.NoChange(request.State);
            }

            ThumbnailRepairRerunResult rerun = await repairRerunCoordinator
                .RerunAsync(
                    new ThumbnailRepairRerunRequest
                    {
                        QueueObj = request.QueueObj,
                        TabInfo = request.TabInfo,
                        ThumbInfo = request.ThumbInfo,
                        WorkingMovieFullPath = forcedRepair.WorkingMovieFullPath,
                        SaveThumbFileName = request.SaveThumbFileName,
                        IsResizeThumb = request.IsResizeThumb,
                        IsManual = request.IsManual,
                        DurationSec = request.DurationSec,
                        FileSizeBytes = request.FileSizeBytes,
                        AverageBitrateMbps = request.AverageBitrateMbps,
                        VideoCodec = forcedRepair.VideoCodec,
                    },
                    cts
                )
                .ConfigureAwait(false);

            ThumbnailRepairExecutionState nextState = request.State.WithAppliedRepair(
                forcedRepair.WorkingMovieFullPath,
                forcedRepair.RepairedMovieTempPath
            );
            return ThumbnailRepairExecutionApplyResult.Applied(
                nextState,
                rerun.Context,
                rerun.Result,
                rerun.ProcessEngineId,
                rerun.EngineErrorMessages
            );
        }
    }

    internal sealed class ThumbnailRepairExecutionState
    {
        public ThumbnailRepairExecutionState(
            bool isRecoveryLane,
            bool isIndexRepairTargetMovie,
            bool repairedByProbe,
            string workingMovieFullPath,
            string repairedMovieTempPath
        )
        {
            IsRecoveryLane = isRecoveryLane;
            IsIndexRepairTargetMovie = isIndexRepairTargetMovie;
            RepairedByProbe = repairedByProbe;
            WorkingMovieFullPath = workingMovieFullPath ?? "";
            RepairedMovieTempPath = repairedMovieTempPath ?? "";
        }

        public bool IsRecoveryLane { get; }

        public bool IsIndexRepairTargetMovie { get; }

        public bool RepairedByProbe { get; }

        public string WorkingMovieFullPath { get; }

        public string RepairedMovieTempPath { get; }

        public ThumbnailRepairExecutionState WithAppliedRepair(
            string workingMovieFullPath,
            string repairedMovieTempPath
        )
        {
            return new ThumbnailRepairExecutionState(
                IsRecoveryLane,
                IsIndexRepairTargetMovie,
                repairedByProbe: true,
                workingMovieFullPath,
                repairedMovieTempPath
            );
        }
    }

    internal sealed class ThumbnailRepairExecutionApplyRequest
    {
        public ThumbnailRepairExecutionState State { get; init; }

        public QueueObj QueueObj { get; init; }

        public TabInfo TabInfo { get; init; }

        public ThumbInfo ThumbInfo { get; init; }

        public string MovieFullPath { get; init; } = "";

        public string SaveThumbFileName { get; init; } = "";

        public bool IsResizeThumb { get; init; }

        public bool IsManual { get; init; }

        public bool InitialOnePassAttempted { get; init; }

        public double? DurationSec { get; init; }

        public long FileSizeBytes { get; init; }

        public double? AverageBitrateMbps { get; init; }

        public ThumbnailCreateResult Result { get; init; }

        public List<string> EngineErrorMessages { get; init; } = [];
    }

    internal sealed class ThumbnailRepairExecutionApplyResult
    {
        private ThumbnailRepairExecutionApplyResult(
            bool wasApplied,
            ThumbnailRepairExecutionState state,
            ThumbnailJobContext context,
            ThumbnailCreateResult result,
            string processEngineId,
            List<string> engineErrorMessages
        )
        {
            WasApplied = wasApplied;
            State = state;
            Context = context;
            Result = result;
            ProcessEngineId = processEngineId ?? "";
            EngineErrorMessages = engineErrorMessages ?? [];
        }

        public bool WasApplied { get; }

        public ThumbnailRepairExecutionState State { get; }

        public ThumbnailJobContext Context { get; }

        public ThumbnailCreateResult Result { get; }

        public string ProcessEngineId { get; }

        public List<string> EngineErrorMessages { get; }

        public static ThumbnailRepairExecutionApplyResult NoChange(
            ThumbnailRepairExecutionState state
        )
        {
            return new ThumbnailRepairExecutionApplyResult(
                false,
                state,
                null,
                null,
                "",
                []
            );
        }

        public static ThumbnailRepairExecutionApplyResult Applied(
            ThumbnailRepairExecutionState state,
            ThumbnailJobContext context,
            ThumbnailCreateResult result,
            string processEngineId,
            List<string> engineErrorMessages
        )
        {
            return new ThumbnailRepairExecutionApplyResult(
                true,
                state,
                context,
                result,
                processEngineId,
                engineErrorMessages
            );
        }
    }
}
