using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// エンジン選択後の実行ループをまとめる coordinator。
    /// service は repair や placeholder 判断に集中し、ここは純粋に engine 順実行を担う。
    /// </summary>
    internal sealed class ThumbnailEngineExecutionCoordinator
    {
        private readonly ThumbnailEngineRouter engineRouter;
        private readonly ThumbnailEngineCatalog engineCatalog;
        private readonly IThumbnailGenerationEngine ffmpegOnePassEngine;

        public ThumbnailEngineExecutionCoordinator(
            ThumbnailEngineRouter engineRouter,
            ThumbnailEngineCatalog engineCatalog,
            IThumbnailGenerationEngine ffmpegOnePassEngine
        )
        {
            this.engineRouter = engineRouter ?? throw new ArgumentNullException(nameof(engineRouter));
            this.engineCatalog =
                engineCatalog ?? throw new ArgumentNullException(nameof(engineCatalog));
            this.ffmpegOnePassEngine =
                ffmpegOnePassEngine ?? throw new ArgumentNullException(nameof(ffmpegOnePassEngine));
        }

        public async Task<ThumbnailEngineExecutionResult> ExecuteAsync(
            ThumbnailJobContext context,
            string saveThumbFileName,
            double? durationSec,
            CancellationToken cts
        )
        {
            IThumbnailGenerationEngine selectedEngine = engineRouter.ResolveForThumbnail(context);
            List<IThumbnailGenerationEngine> engineOrder = engineCatalog.BuildExecutionOrder(
                selectedEngine,
                context
            );
            ThumbnailCreateResult runtimeResult = null;
            string runtimeProcessEngineId = selectedEngine?.EngineId ?? "unknown";
            List<string> runtimeEngineErrorMessages = [];

            for (int i = 0; i < engineOrder.Count; i++)
            {
                IThumbnailGenerationEngine candidate = engineOrder[i];
                runtimeProcessEngineId = candidate.EngineId;
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    i == 0
                        ? $"engine selected: id={candidate.EngineId}, panel={context.PanelCount}, size={context.FileSizeBytes}, avg_mbps={context.AverageBitrateMbps:0.###}, emoji={context.HasEmojiPath}, manual={context.IsManual}"
                        : $"engine fallback: category=fallback from={selectedEngine.EngineId}, to={candidate.EngineId}, attempt={i + 1}/{engineOrder.Count}"
                );
                if (
                    i > 0
                    && string.Equals(
                        selectedEngine?.EngineId,
                        "autogen",
                        StringComparison.OrdinalIgnoreCase
                    )
                    && string.Equals(
                        candidate.EngineId,
                        "ffmpeg1pass",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    ThumbnailEngineRuntimeStats.RecordFallbackToFfmpegOnePass();
                }

                // 先行エンジンで入力破損が確定している場合、重いffmpeg1pass起動を省略する。
                if (
                    !context.IsManual
                    && string.Equals(
                        candidate.EngineId,
                        "ffmpeg1pass",
                        StringComparison.OrdinalIgnoreCase
                    )
                    && ThumbnailExecutionPolicy.ShouldSkipFfmpegOnePassByKnownInvalidInput(
                        runtimeEngineErrorMessages
                    )
                )
                {
                    const string skipReason = "known invalid input signature";
                    ThumbnailRuntimeLog.Write(
                        "thumbnail",
                        $"engine skipped: id=ffmpeg1pass, reason='{skipReason}'"
                    );
                    runtimeResult = ThumbnailResultFactory.CreateFailed(
                        saveThumbFileName,
                        durationSec,
                        $"ffmpeg1pass skipped: {skipReason}",
                        engineAttempted: candidate.EngineId
                    );
                    runtimeEngineErrorMessages.Add($"[ffmpeg1pass] skipped: {skipReason}");
                    break;
                }

                bool isAutogenCandidate = string.Equals(
                    candidate.EngineId,
                    "autogen",
                    StringComparison.OrdinalIgnoreCase
                );
                int autogenRetryCount = 0;
                int maxAutogenRetryCount = ThumbnailExecutionPolicy.ResolveAutogenRetryCount();
                bool transientFailureRecorded = false;
                while (true)
                {
                    try
                    {
                        runtimeResult = await candidate.CreateAsync(context, cts).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        runtimeResult = ThumbnailResultFactory.CreateFailed(
                            saveThumbFileName,
                            durationSec,
                            ex.Message,
                            engineAttempted: candidate.EngineId
                        );
                    }

                    if (runtimeResult == null)
                    {
                        runtimeResult = ThumbnailResultFactory.CreateFailed(
                            saveThumbFileName,
                            durationSec,
                            "thumbnail engine returned null result",
                            engineAttempted: candidate.EngineId
                        );
                    }

                    runtimeResult.EngineAttempted = candidate.EngineId;

                    bool isTransientAutogenFailure =
                        isAutogenCandidate
                        && !runtimeResult.IsSuccess
                        && ThumbnailExecutionPolicy.IsAutogenTransientRetryError(
                            runtimeResult.ErrorMessage
                        );
                    if (isTransientAutogenFailure && !transientFailureRecorded)
                    {
                        transientFailureRecorded = true;
                        ThumbnailEngineRuntimeStats.RecordAutogenTransientFailure();
                    }

                    bool canRetryAutogen =
                        isTransientAutogenFailure
                        && autogenRetryCount < maxAutogenRetryCount
                        && ThumbnailExecutionPolicy.IsAutogenRetryEnabled();
                    if (canRetryAutogen)
                    {
                        autogenRetryCount++;
                        int retryDelayMs = ThumbnailExecutionPolicy.ResolveAutogenRetryDelayMs();
                        ThumbnailRuntimeLog.Write(
                            "thumbnail",
                            $"engine retry scheduled: category=error id=autogen, attempt={autogenRetryCount}/{maxAutogenRetryCount}, delay_ms={retryDelayMs}, reason='{runtimeResult.ErrorMessage}'"
                        );
                        if (retryDelayMs > 0)
                        {
                            await Task.Delay(retryDelayMs, cts).ConfigureAwait(false);
                        }
                        continue;
                    }

                    if (isAutogenCandidate && autogenRetryCount > 0 && runtimeResult.IsSuccess)
                    {
                        ThumbnailEngineRuntimeStats.RecordAutogenRetrySuccess();
                        ThumbnailRuntimeLog.Write(
                            "thumbnail",
                            "engine retry success: category=error id=autogen"
                        );
                    }
                    break;
                }

                if (!runtimeResult.IsSuccess && !string.IsNullOrWhiteSpace(runtimeResult.ErrorMessage))
                {
                    runtimeEngineErrorMessages.Add(
                        $"[{candidate.EngineId}] {runtimeResult.ErrorMessage}"
                    );
                }

                if (runtimeResult.IsSuccess)
                {
                    if (
                        ThumbnailExecutionPolicy.ShouldTreatAutogenSuccessAsFailure(
                            candidate.EngineId,
                            runtimeResult.PreviewFrame
                        )
                    )
                    {
                        runtimeEngineErrorMessages.Add(
                            "[autogen] Autogen produced a near-black thumbnail"
                        );
                        TryDeleteFileQuietly(context.SaveThumbFileName);
                        runtimeResult = ThumbnailResultFactory.CreateFailed(
                            saveThumbFileName,
                            durationSec,
                            "Autogen produced a near-black thumbnail",
                            engineAttempted: candidate.EngineId
                        );
                        ThumbnailRuntimeLog.Write(
                            "thumbnail",
                            "engine failed: category=error id=autogen, reason='near-black thumbnail detected', try_next=True"
                        );
                    }

                    if (runtimeResult.IsSuccess)
                    {
                        break;
                    }
                }

                if (i < engineOrder.Count - 1)
                {
                    ThumbnailRuntimeLog.Write(
                        "thumbnail",
                        $"engine failed: category=error id={candidate.EngineId}, reason='{runtimeResult.ErrorMessage}', try_next=True"
                    );
                }
            }

            if (runtimeResult == null)
            {
                runtimeResult = ThumbnailResultFactory.CreateFailed(
                    saveThumbFileName,
                    durationSec,
                    "thumbnail engine was not executed",
                    engineAttempted: runtimeProcessEngineId
                );
            }

            return new ThumbnailEngineExecutionResult(
                runtimeResult,
                runtimeProcessEngineId,
                runtimeEngineErrorMessages
            );
        }

        // 実行後の終端救済をここへ寄せ、service 側は文脈の組み立てだけに近づける。
        public async Task<ThumbnailEnginePostProcessResult> ApplyPostExecutionFallbacksAsync(
            ThumbnailEnginePostProcessRequest request,
            CancellationToken cts
        )
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ThumbnailJobContext context = request.Context;
            ThumbnailCreateResult result = request.Result;
            string processEngineId = request.ProcessEngineId ?? "";
            List<string> engineErrorMessages = request.EngineErrorMessages ?? [];

            bool shouldTryRecoveryOnePassFallback =
                ThumbnailExecutionPolicy.ShouldTryRecoveryOnePassFallback(
                    request.IsManual,
                    request.IsRecoveryLane,
                    result.IsSuccess,
                    processEngineId,
                    request.DurationSec,
                    engineErrorMessages
                );
            if (shouldTryRecoveryOnePassFallback)
            {
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    "engine fallback: category=fallback from=autogen, to=ffmpeg1pass, reason='recovery-no-frames-decoded'"
                );

                ThumbnailCreateResult onePassResult = await ffmpegOnePassEngine
                    .CreateAsync(context, cts)
                    .ConfigureAwait(false);
                if (onePassResult?.IsSuccess == true)
                {
                    onePassResult.FailureStage = "postprocess-recovery-onepass";
                    onePassResult.PolicyDecision = "recovery-no-frames-decoded";
                    onePassResult.EngineAttempted = ffmpegOnePassEngine.EngineId;
                    result = onePassResult;
                    processEngineId = ffmpegOnePassEngine.EngineId;
                }
                else if (onePassResult != null && !string.IsNullOrWhiteSpace(onePassResult.ErrorMessage))
                {
                    engineErrorMessages.Add(
                        $"[{ffmpegOnePassEngine.EngineId}] {onePassResult.ErrorMessage}"
                    );
                    ThumbnailRuntimeLog.Write(
                        "thumbnail",
                        $"engine failed: category=error id={ffmpegOnePassEngine.EngineId}, reason='{onePassResult.ErrorMessage}', try_next=False"
                    );
                }
            }

            bool shouldTryOriginalOnePassAfterRepairFailure =
                ThumbnailExecutionPolicy.ShouldTryOriginalOnePassAfterRepairFailure(
                    request.IsManual,
                    request.RepairedByProbe,
                    result.IsSuccess,
                    processEngineId,
                    ffmpegOnePassEngine.EngineId,
                    engineErrorMessages
                );
            if (shouldTryOriginalOnePassAfterRepairFailure)
            {
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"engine fallback: category=fallback from={processEngineId}, to=ffmpeg1pass, reason='repair-output-failed-use-original'"
                );

                ThumbnailCreateResult originalOnePassResult = await ffmpegOnePassEngine
                    .CreateAsync(request.OriginalMovieContext, cts)
                    .ConfigureAwait(false);
                if (originalOnePassResult?.IsSuccess == true)
                {
                    originalOnePassResult.FailureStage = "postprocess-original-onepass";
                    originalOnePassResult.PolicyDecision = "repair-output-failed-use-original";
                    originalOnePassResult.EngineAttempted = ffmpegOnePassEngine.EngineId;
                    result = originalOnePassResult;
                    processEngineId = ffmpegOnePassEngine.EngineId;
                    context = request.OriginalMovieContext;
                }
                else if (
                    originalOnePassResult != null
                    && !string.IsNullOrWhiteSpace(originalOnePassResult.ErrorMessage)
                )
                {
                    engineErrorMessages.Add(
                        $"[{ffmpegOnePassEngine.EngineId}] {originalOnePassResult.ErrorMessage}"
                    );
                    ThumbnailRuntimeLog.Write(
                        "thumbnail",
                        $"engine failed: category=error id={ffmpegOnePassEngine.EngineId}, reason='{originalOnePassResult.ErrorMessage}', try_next=False"
                    );
                }
            }

            if (!result.IsSuccess && !request.IsManual)
            {
                bool skipPlaceholderForInitialRetryRouting =
                    ThumbnailExecutionPolicy.ShouldSkipFailurePlaceholder(
                        request.IsManual,
                        result.IsSuccess,
                        request.IsRecoveryLane
                    );
                if (skipPlaceholderForInitialRetryRouting)
                {
                    string skipReason =
                        ThumbnailExecutionPolicy.ResolveFailurePlaceholderSkipReason(
                            request.IsIndexRepairTargetMovie
                        );
                    result.FailureStage = "postprocess-placeholder";
                    result.PolicyDecision = skipReason;
                    result.PlaceholderAction = "skipped";
                    result.PlaceholderKind = "";

                    ThumbnailRuntimeLog.Write(
                        "thumbnail",
                        $"failure placeholder skipped: category=fallback movie='{request.MovieFullPath}', reason='{skipReason}'"
                    );
                }
                else
                {
                    FailurePlaceholderKind placeholderKind =
                        ThumbnailPlaceholderUtility.ClassifyFailure(
                            context.VideoCodec,
                            engineErrorMessages
                        );
                    if (
                        ThumbnailPlaceholderUtility.TryCreateFailurePlaceholderThumbnail(
                            context,
                            placeholderKind,
                            out string placeholderDetail
                        )
                    )
                    {
                        processEngineId = placeholderKind switch
                        {
                            FailurePlaceholderKind.DrmSuspected =>
                                "placeholder-drm",
                            FailurePlaceholderKind.UnsupportedCodec =>
                                "placeholder-unsupported",
                            _ => "placeholder-unknown",
                        };
                        ThumbnailRuntimeLog.Write(
                            "thumbnail",
                            $"failure placeholder created: category=fallback kind={placeholderKind}, movie='{request.MovieFullPath}', path='{request.SaveThumbFileName}', detail='{placeholderDetail}'"
                        );
                        result = ThumbnailResultFactory.CreateSuccess(
                            request.SaveThumbFileName,
                            request.DurationSec,
                            engineAttempted: processEngineId,
                            failureStage: "postprocess-placeholder",
                            policyDecision: "placeholder-created",
                            placeholderAction: "created",
                            placeholderKind: placeholderKind.ToString()
                        );
                    }
                    else
                    {
                        result.FailureStage = "postprocess-placeholder";
                        result.PolicyDecision = "placeholder-create-failed";
                        result.PlaceholderAction = "failed";
                        result.PlaceholderKind = placeholderKind.ToString();
                    }
                }
            }

            return new ThumbnailEnginePostProcessResult(result, processEngineId, engineErrorMessages, context);
        }

        private static void TryDeleteFileQuietly(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
                // 一時ファイル削除失敗は後続処理を優先する。
            }
        }
    }

    internal sealed class ThumbnailEngineExecutionResult
    {
        public ThumbnailEngineExecutionResult(
            ThumbnailCreateResult result,
            string processEngineId,
            List<string> engineErrorMessages
        )
        {
            Result = result;
            ProcessEngineId = processEngineId ?? "";
            EngineErrorMessages = engineErrorMessages ?? [];
        }

        public ThumbnailCreateResult Result { get; }

        public string ProcessEngineId { get; }

        public List<string> EngineErrorMessages { get; }
    }

    internal sealed class ThumbnailEnginePostProcessRequest
    {
        public ThumbnailJobContext Context { get; init; }

        public ThumbnailJobContext OriginalMovieContext { get; init; }

        public ThumbnailCreateResult Result { get; init; }

        public string ProcessEngineId { get; init; }

        public List<string> EngineErrorMessages { get; init; }

        public bool IsManual { get; init; }

        public bool IsRecoveryLane { get; init; }

        public bool IsIndexRepairTargetMovie { get; init; }

        public bool RepairedByProbe { get; init; }

        public string MovieFullPath { get; init; }

        public string SaveThumbFileName { get; init; }

        public double? DurationSec { get; init; }
    }

    internal sealed class ThumbnailEnginePostProcessResult
    {
        public ThumbnailEnginePostProcessResult(
            ThumbnailCreateResult result,
            string processEngineId,
            List<string> engineErrorMessages,
            ThumbnailJobContext context
        )
        {
            Result = result;
            ProcessEngineId = processEngineId ?? "";
            EngineErrorMessages = engineErrorMessages ?? [];
            Context = context;
        }

        public ThumbnailCreateResult Result { get; }

        public string ProcessEngineId { get; }

        public List<string> EngineErrorMessages { get; }

        public ThumbnailJobContext Context { get; }
    }
}
