namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// CreateThumbAsync の本流をまとめる。
    /// preflight、SWF 分岐、material build、主フロー実行、completion までを一箇所でつなぐ。
    /// </summary>
    internal sealed class ThumbnailCreationOrchestrationCoordinator
    {
        private readonly Swf.SwfThumbnailRouteHandler swfThumbnailRouteHandler;
        private readonly ThumbnailJobMaterialBuilder jobMaterialBuilder;
        private readonly ThumbnailRepairExecutionCoordinator repairExecutionCoordinator;
        private readonly ThumbnailExecutionFlowCoordinator executionFlowCoordinator;

        public ThumbnailCreationOrchestrationCoordinator(
            Swf.SwfThumbnailRouteHandler swfThumbnailRouteHandler,
            ThumbnailJobMaterialBuilder jobMaterialBuilder,
            ThumbnailRepairExecutionCoordinator repairExecutionCoordinator,
            ThumbnailExecutionFlowCoordinator executionFlowCoordinator
        )
        {
            this.swfThumbnailRouteHandler =
                swfThumbnailRouteHandler
                ?? throw new ArgumentNullException(nameof(swfThumbnailRouteHandler));
            this.jobMaterialBuilder =
                jobMaterialBuilder ?? throw new ArgumentNullException(nameof(jobMaterialBuilder));
            this.repairExecutionCoordinator =
                repairExecutionCoordinator
                ?? throw new ArgumentNullException(nameof(repairExecutionCoordinator));
            this.executionFlowCoordinator =
                executionFlowCoordinator
                ?? throw new ArgumentNullException(nameof(executionFlowCoordinator));
        }

        public async Task<ThumbnailCreationOrchestrationResult> ExecuteAsync(
            ThumbnailCreationOrchestrationRequest request,
            CancellationToken cts
        )
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            double? durationSec = request.DurationSec;
            long fileSizeBytes = 0;

            ThumbnailPreflightCheckResult preflight = ThumbnailPreflightChecker.Evaluate(
                new ThumbnailPreflightCheckRequest
                {
                    QueueObj = request.QueueObj,
                    TabInfo = request.TabInfo,
                    CacheMeta = request.CacheMeta,
                    MovieFullPath = request.MovieFullPath,
                    SaveThumbFileName = request.SaveThumbFileName,
                    IsResizeThumb = request.IsResizeThumb,
                    IsManual = request.IsManual,
                    DurationSec = durationSec,
                    FileSizeBytes = fileSizeBytes,
                    TabIndex = request.QueueObj?.Tabindex ?? 0,
                }
            );
            if (preflight.ShouldReturn)
            {
                return ThumbnailCreationOrchestrationResult.Completed(
                    request.CompletionCoordinator.Complete(
                        preflight.Result,
                        preflight.ProcessEngineId,
                        preflight.VideoCodec,
                        preflight.FileSizeBytes
                    ),
                    ""
                );
            }

            fileSizeBytes = ResolveFileSizeBytes(request.QueueObj, request.MovieFullPath);
            if (request.QueueObj != null && fileSizeBytes > 0)
            {
                // 後段でも再利用できるよう、取れたサイズを戻す。
                request.QueueObj.MovieSizeBytes = fileSizeBytes;
            }

            if (request.CacheMeta.IsSwfCandidate)
            {
                string swfDetail = string.IsNullOrWhiteSpace(request.CacheMeta.SwfDetail)
                    ? "swf_candidate_hit"
                    : request.CacheMeta.SwfDetail;
                Swf.SwfThumbnailRouteResult swfRoute = await swfThumbnailRouteHandler
                    .HandleAsync(
                        new Swf.SwfThumbnailRouteRequest
                        {
                            QueueObj = request.QueueObj,
                            TabInfo = request.TabInfo,
                            MovieFullPath = request.MovieFullPath,
                            SaveThumbFileName = request.SaveThumbFileName,
                            Detail = swfDetail,
                            IsResizeThumb = request.IsResizeThumb,
                            IsManual = request.IsManual,
                            DurationSec = durationSec,
                            FileSizeBytes = fileSizeBytes,
                        },
                        cts
                    )
                    .ConfigureAwait(false);
                return ThumbnailCreationOrchestrationResult.Completed(
                    request.CompletionCoordinator.Complete(
                        swfRoute.Result,
                        swfRoute.ProcessEngineId,
                        swfRoute.VideoCodec,
                        swfRoute.FileSizeBytes
                    ),
                    ""
                );
            }

            ThumbnailRepairExecutionState repairState = await repairExecutionCoordinator
                .PrepareAsync(request.QueueObj, request.MovieFullPath, cts)
                .ConfigureAwait(false);
            string repairedMovieTempPath = repairState.RepairedMovieTempPath;

            ThumbnailJobMaterialBuildResult materials = jobMaterialBuilder.Build(
                new ThumbnailJobMaterialBuildRequest
                {
                    QueueObj = request.QueueObj,
                    TabInfo = request.TabInfo,
                    WorkingMovieFullPath = repairState.WorkingMovieFullPath,
                    SaveThumbFileName = request.SaveThumbFileName,
                    IsManual = request.IsManual,
                    DurationSec = durationSec,
                    FileSizeBytes = fileSizeBytes,
                }
            );
            durationSec = materials.DurationSec;
            request.CompletionCoordinator.UpdateCachedDuration(durationSec);
            if (!materials.IsSuccess)
            {
                return ThumbnailCreationOrchestrationResult.Completed(
                    request.CompletionCoordinator.Complete(
                        ThumbnailResultFactory.CreateFailed(
                            request.SaveThumbFileName,
                            durationSec,
                            materials.ErrorMessage
                        ),
                        "precheck",
                        "",
                        0
                    ),
                    repairedMovieTempPath
                );
            }

            request.OnResolvedDuration?.Invoke(durationSec);

            ThumbnailExecutionFlowResult flow = await executionFlowCoordinator
                .ExecuteAsync(
                    new ThumbnailExecutionFlowRequest
                    {
                        RepairState = repairState,
                        QueueObj = request.QueueObj,
                        TabInfo = request.TabInfo,
                        ThumbInfo = materials.ThumbInfo,
                        MovieFullPath = request.MovieFullPath,
                        WorkingMovieFullPath = repairState.WorkingMovieFullPath,
                        SaveThumbFileName = request.SaveThumbFileName,
                        IsResizeThumb = request.IsResizeThumb,
                        IsManual = request.IsManual,
                        DurationSec = durationSec,
                        FileSizeBytes = fileSizeBytes,
                        AverageBitrateMbps = materials.AverageBitrateMbps,
                        VideoCodec = materials.VideoCodec,
                    },
                    cts
                )
                .ConfigureAwait(false);

            return ThumbnailCreationOrchestrationResult.Completed(
                request.CompletionCoordinator.Complete(
                    flow.Result,
                    flow.ProcessEngineId,
                    flow.Context.VideoCodec,
                    flow.Context.FileSizeBytes
                ),
                flow.RepairState.RepairedMovieTempPath
            );
        }

        private static long ResolveFileSizeBytes(QueueObj queueObj, string movieFullPath)
        {
            long fileSizeBytes = Math.Max(0, queueObj?.MovieSizeBytes ?? 0);
            if (fileSizeBytes >= 1)
            {
                return fileSizeBytes;
            }

            try
            {
                return new FileInfo(movieFullPath).Length;
            }
            catch
            {
                return 0;
            }
        }
    }

    internal sealed class ThumbnailCreationOrchestrationRequest
    {
        public QueueObj QueueObj { get; init; }

        public TabInfo TabInfo { get; init; }

        public CachedMovieMeta CacheMeta { get; init; }

        public string MovieFullPath { get; init; } = "";

        public string SaveThumbFileName { get; init; } = "";

        public bool IsResizeThumb { get; init; }

        public bool IsManual { get; init; }

        public double? DurationSec { get; init; }

        public ThumbnailResultCompletionCoordinator CompletionCoordinator { get; init; }

        public Action<double?> OnResolvedDuration { get; init; }
    }

    internal sealed class ThumbnailCreationOrchestrationResult
    {
        private ThumbnailCreationOrchestrationResult(
            ThumbnailCreateResult result,
            string repairedMovieTempPath
        )
        {
            Result = result;
            RepairedMovieTempPath = repairedMovieTempPath ?? "";
        }

        public ThumbnailCreateResult Result { get; }

        public string RepairedMovieTempPath { get; }

        public static ThumbnailCreationOrchestrationResult Completed(
            ThumbnailCreateResult result,
            string repairedMovieTempPath
        )
        {
            return new ThumbnailCreationOrchestrationResult(result, repairedMovieTempPath);
        }
    }
}
