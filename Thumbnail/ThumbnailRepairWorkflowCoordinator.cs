using IndigoMovieManager.Thumbnail.Engines.IndexRepair;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// index repair の事前試行と失敗後再試行をまとめる coordinator。
    /// service 側は「次に何を実行するか」へ集中し、repair の実務はここへ寄せる。
    /// </summary>
    internal sealed class ThumbnailRepairWorkflowCoordinator
    {
        private readonly IVideoMetadataProvider videoMetadataProvider;
        private readonly IVideoIndexRepairService videoIndexRepairService;

        public ThumbnailRepairWorkflowCoordinator(
            IVideoMetadataProvider videoMetadataProvider,
            IVideoIndexRepairService videoIndexRepairService
        )
        {
            this.videoMetadataProvider =
                videoMetadataProvider
                ?? throw new ArgumentNullException(nameof(videoMetadataProvider));
            this.videoIndexRepairService =
                videoIndexRepairService
                ?? throw new ArgumentNullException(nameof(videoIndexRepairService));
        }

        public async Task<ThumbnailRepairPreparationResult> PrepareWorkingMovieAsync(
            string movieFullPath,
            bool isRecoveryLane,
            bool isIndexRepairTargetMovie,
            CancellationToken cts
        )
        {
            if (!isRecoveryLane || !isIndexRepairTargetMovie)
            {
                return ThumbnailRepairPreparationResult.NoChange(movieFullPath);
            }

            VideoIndexProbeResult probeResult = await videoIndexRepairService
                .ProbeAsync(movieFullPath, cts)
                .ConfigureAwait(false);
            ThumbnailRuntimeLog.Write(
                "index-probe",
                $"probe result: movie='{movieFullPath}', detected={probeResult.IsIndexCorruptionDetected}, reason='{probeResult.DetectionReason}', code='{probeResult.ErrorCode}'"
            );

            if (!probeResult.IsIndexCorruptionDetected)
            {
                return ThumbnailRepairPreparationResult.NoChange(movieFullPath);
            }

            string tempOutputPath = BuildIndexRepairTempOutputPath(movieFullPath);
            VideoIndexRepairResult repairResult = await videoIndexRepairService
                .RepairAsync(movieFullPath, tempOutputPath, cts)
                .ConfigureAwait(false);
            if (repairResult.IsSuccess && Path.Exists(repairResult.OutputPath))
            {
                ThumbnailRuntimeLog.Write(
                    "index-repair-summary",
                    $"repair applied: movie='{movieFullPath}', fixed='{repairResult.OutputPath}'"
                );
                return new ThumbnailRepairPreparationResult(
                    repairResult.OutputPath,
                    repairResult.OutputPath,
                    true
                );
            }

            ThumbnailRuntimeLog.Write(
                "index-repair-summary",
                $"repair skipped: movie='{movieFullPath}', reason='{repairResult.ErrorMessage}'"
            );
            return ThumbnailRepairPreparationResult.NoChange(movieFullPath);
        }

        public async Task<ThumbnailForcedRepairResult> TryRepairAfterFailureAsync(
            ThumbnailForcedRepairRequest request,
            CancellationToken cts
        )
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            bool shouldForceRepairAfterFailure =
                ThumbnailExecutionPolicy.ShouldForceRepairAfterFailure(
                    request.IsManual,
                    request.ResultIsSuccess,
                    request.IsRecoveryLane,
                    request.IsIndexRepairTargetMovie,
                    request.RepairedByProbe,
                    request.EngineErrorMessages
                );
            if (!shouldForceRepairAfterFailure)
            {
                return ThumbnailForcedRepairResult.NoChange();
            }

            string tempOutputPath = BuildIndexRepairTempOutputPath(request.MovieFullPath);
            VideoIndexRepairResult repairResult = await videoIndexRepairService
                .RepairAsync(request.MovieFullPath, tempOutputPath, cts)
                .ConfigureAwait(false);
            if (repairResult.IsSuccess && Path.Exists(repairResult.OutputPath))
            {
                ThumbnailRuntimeLog.Write(
                    "index-repair-summary",
                    $"repair forced: movie='{request.MovieFullPath}', fixed='{repairResult.OutputPath}', reason='runtime-engine-failure'"
                );
                return new ThumbnailForcedRepairResult(
                    wasApplied: true,
                    workingMovieFullPath: repairResult.OutputPath,
                    repairedMovieTempPath: repairResult.OutputPath,
                    videoCodec: TryResolveVideoCodec(repairResult.OutputPath)
                );
            }

            ThumbnailRuntimeLog.Write(
                "index-repair-summary",
                $"repair forced skipped: movie='{request.MovieFullPath}', reason='{repairResult.ErrorMessage}'"
            );
            return ThumbnailForcedRepairResult.NoChange();
        }

        private string TryResolveVideoCodec(string movieFullPath)
        {
            if (
                videoMetadataProvider.TryGetVideoCodec(movieFullPath, out string providedVideoCodec)
                && !string.IsNullOrWhiteSpace(providedVideoCodec)
            )
            {
                return providedVideoCodec;
            }

            return "";
        }

        private static string BuildIndexRepairTempOutputPath(string movieFullPath)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "IndigoMovieManager_fork", "index-repair");
            Directory.CreateDirectory(tempDir);
            string safeName = Path.GetFileNameWithoutExtension(movieFullPath);
            string fileName =
                $"{safeName}.repair.{Environment.ProcessId}.{Thread.CurrentThread.ManagedThreadId}.{Guid.NewGuid():N}.mkv";
            return Path.Combine(tempDir, fileName);
        }
    }

    internal sealed class ThumbnailRepairPreparationResult
    {
        public ThumbnailRepairPreparationResult(
            string workingMovieFullPath,
            string repairedMovieTempPath,
            bool repairedByProbe
        )
        {
            WorkingMovieFullPath = workingMovieFullPath ?? "";
            RepairedMovieTempPath = repairedMovieTempPath ?? "";
            RepairedByProbe = repairedByProbe;
        }

        public string WorkingMovieFullPath { get; }

        public string RepairedMovieTempPath { get; }

        public bool RepairedByProbe { get; }

        public static ThumbnailRepairPreparationResult NoChange(string movieFullPath)
        {
            return new ThumbnailRepairPreparationResult(movieFullPath, "", false);
        }
    }

    internal sealed class ThumbnailForcedRepairRequest
    {
        public bool IsManual { get; init; }

        public bool ResultIsSuccess { get; init; }

        public bool IsRecoveryLane { get; init; }

        public bool IsIndexRepairTargetMovie { get; init; }

        public bool RepairedByProbe { get; init; }

        public List<string> EngineErrorMessages { get; init; } = [];

        public string MovieFullPath { get; init; } = "";
    }

    internal sealed class ThumbnailForcedRepairResult
    {
        public ThumbnailForcedRepairResult(
            bool wasApplied,
            string workingMovieFullPath,
            string repairedMovieTempPath,
            string videoCodec
        )
        {
            WasApplied = wasApplied;
            WorkingMovieFullPath = workingMovieFullPath ?? "";
            RepairedMovieTempPath = repairedMovieTempPath ?? "";
            VideoCodec = videoCodec ?? "";
        }

        public bool WasApplied { get; }

        public string WorkingMovieFullPath { get; }

        public string RepairedMovieTempPath { get; }

        public string VideoCodec { get; }

        public static ThumbnailForcedRepairResult NoChange()
        {
            return new ThumbnailForcedRepairResult(false, "", "", "");
        }
    }
}
