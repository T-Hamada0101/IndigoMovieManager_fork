using System.Drawing;
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
                string fallbackReason = ThumbnailExecutionPolicy.ResolveRecoveryOnePassFallbackReason(
                    request.IsRecoveryLane,
                    request.DurationSec,
                    engineErrorMessages
                );
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"engine fallback: category=fallback from=autogen, to=ffmpeg1pass, reason='{fallbackReason}'"
                );

                ThumbnailCreateResult onePassResult = await ffmpegOnePassEngine
                    .CreateAsync(context, cts)
                    .ConfigureAwait(false);
                if (onePassResult?.IsSuccess == true)
                {
                    onePassResult.FailureStage = "postprocess-recovery-onepass";
                    onePassResult.PolicyDecision = fallbackReason;
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

            bool shouldTryRecoverySingleFrameFallback =
                ThumbnailExecutionPolicy.ShouldTryRecoverySingleFrameFallback(
                    request.IsManual,
                    request.IsRecoveryLane,
                    result.IsSuccess,
                    engineErrorMessages
                );
            if (shouldTryRecoverySingleFrameFallback)
            {
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"engine fallback: category=fallback from={processEngineId}, to=single-frame, reason='recovery-bookmark-1sec'"
                );

                ThumbnailSingleFrameFallbackResult singleFrameFallback = await TryCreateSingleFrameFallbackAsync(
                    context,
                    request.SaveThumbFileName,
                    request.DurationSec,
                    cts
                ).ConfigureAwait(false);
                if (singleFrameFallback.IsSuccess)
                {
                    result = singleFrameFallback.Result;
                    processEngineId = singleFrameFallback.ProcessEngineId;
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
                    if (placeholderKind == FailurePlaceholderKind.None)
                    {
                        result.FailureStage = "postprocess-placeholder";
                        result.PolicyDecision = "placeholder-suppressed";
                        result.PlaceholderAction = "skipped";
                        result.PlaceholderKind = "";

                        ThumbnailRuntimeLog.Write(
                            "thumbnail",
                            $"failure placeholder suppressed: category=fallback movie='{request.MovieFullPath}', codec='{context.VideoCodec}', reason='no-placeholder-classification'"
                        );
                    }
                    else if (
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

            return new ThumbnailEnginePostProcessResult(
                result,
                processEngineId,
                engineErrorMessages,
                context,
                shouldTryRecoveryOnePassFallback
            );
        }

        // 救済レーンの終端だけ、1秒1枚の bookmark 取得で代表画像を拾いに行く。
        private async Task<ThumbnailSingleFrameFallbackResult> TryCreateSingleFrameFallbackAsync(
            ThumbnailJobContext context,
            string saveThumbFileName,
            double? durationSec,
            CancellationToken cts
        )
        {
            if (context == null || string.IsNullOrWhiteSpace(context.MovieFullPath))
            {
                return ThumbnailSingleFrameFallbackResult.NoChange();
            }

            IReadOnlyList<string> engineOrder =
                ThumbnailExecutionPolicy.BuildRecoverySingleFrameEngineOrderIds();
            for (int i = 0; i < engineOrder.Count; i++)
            {
                IThumbnailGenerationEngine engine = engineCatalog.ResolveById(engineOrder[i], null);
                if (engine == null)
                {
                    continue;
                }

                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"bookmark fallback try: engine={engine.EngineId}, movie='{context.MovieFullPath}', capture_sec=1"
                );
                string representativeImagePath = BuildSingleFrameTempPath(
                    saveThumbFileName,
                    engine.EngineId
                );
                bool created = await engine
                    .CreateBookmarkAsync(context.MovieFullPath, representativeImagePath, 1, cts)
                    .ConfigureAwait(false);
                if (!created)
                {
                    ThumbnailRuntimeLog.Write(
                        "thumbnail",
                        $"bookmark fallback failed: engine={engine.EngineId}, movie='{context.MovieFullPath}', capture_sec=1"
                    );
                    TryDeleteFileQuietly(representativeImagePath);
                    continue;
                }

                try
                {
                    if (
                        !TryCreateSingleFrameTileThumbnail(
                            representativeImagePath,
                            saveThumbFileName,
                            context.TabInfo,
                            context.ThumbInfo,
                            context.DurationSec,
                            out ThumbnailPreviewFrame previewFrame,
                            out string errorMessage
                        )
                    )
                    {
                        ThumbnailRuntimeLog.Write(
                            "thumbnail",
                            $"bookmark fallback tile failed: engine={engine.EngineId}, movie='{context.MovieFullPath}', err='{errorMessage}'"
                        );
                        continue;
                    }

                    string processEngineId = $"{engine.EngineId}-bookmark";
                    ThumbnailRuntimeLog.Write(
                        "thumbnail",
                        $"bookmark fallback success: engine={engine.EngineId}, movie='{context.MovieFullPath}', path='{saveThumbFileName}'"
                    );
                    TryFanOutSingleFrameRescueThumbnails(representativeImagePath, context);
                    return ThumbnailSingleFrameFallbackResult.Success(
                        ThumbnailResultFactory.CreateSuccess(
                            saveThumbFileName,
                            durationSec,
                            previewFrame: previewFrame,
                            engineAttempted: processEngineId,
                            failureStage: "postprocess-single-frame",
                            policyDecision: "recovery-bookmark-1sec",
                            placeholderAction: "single-frame"
                        ),
                        processEngineId
                    );
                }
                finally
                {
                    TryDeleteFileQuietly(representativeImagePath);
                }
            }

            return ThumbnailSingleFrameFallbackResult.NoChange();
        }

        // 代表1枚を現在のTabInfo寸法へ揃え、既存タイル形式へ複製して保存する。
        private static bool TryCreateSingleFrameTileThumbnail(
            string representativeImagePath,
            string saveThumbFileName,
            TabInfo tabInfo,
            ThumbInfo sourceThumbInfo,
            double? durationSec,
            out ThumbnailPreviewFrame previewFrame,
            out string errorMessage
        )
        {
            previewFrame = null;
            errorMessage = "";

            if (tabInfo == null)
            {
                errorMessage = "tab info is missing";
                return false;
            }

            if (string.IsNullOrWhiteSpace(representativeImagePath) || !Path.Exists(representativeImagePath))
            {
                errorMessage = "representative image is missing";
                return false;
            }

            try
            {
                using Bitmap representativeBitmap = new(representativeImagePath);
                Rectangle cropRect = ThumbnailImageUtility.GetAspectRect(
                    representativeBitmap.Width,
                    representativeBitmap.Height
                );
                using Bitmap croppedBitmap = ThumbnailImageUtility.CropBitmap(
                    representativeBitmap,
                    cropRect
                );
                using Bitmap resizedBitmap = ThumbnailImageUtility.ResizeBitmap(
                    croppedBitmap,
                    new Size(tabInfo.Width, tabInfo.Height)
                );
                previewFrame = ThumbnailImageUtility.CreatePreviewFrameFromBitmap(resizedBitmap, 120);

                int frameCount = Math.Max(1, tabInfo.Columns * tabInfo.Rows);
                List<Bitmap> frames = [];
                try
                {
                    for (int i = 0; i < frameCount; i++)
                    {
                        frames.Add(new Bitmap(resizedBitmap));
                    }

                    if (
                        !ThumbnailImageUtility.SaveCombinedThumbnail(
                            saveThumbFileName,
                            frames,
                            tabInfo.Columns,
                            tabInfo.Rows
                        )
                    )
                    {
                        errorMessage = "combined thumbnail save failed";
                        return false;
                    }
                }
                finally
                {
                    for (int i = 0; i < frames.Count; i++)
                    {
                        frames[i]?.Dispose();
                    }
                }

                ThumbInfo rebuiltThumbInfo =
                    ThumbnailImageUtility.RebuildThumbInfoWithCaptureSeconds(
                        sourceThumbInfo
                            ?? ThumbnailImageUtility.BuildAutoThumbInfo(tabInfo, durationSec),
                        Enumerable.Repeat(1d, frameCount).ToList()
                    );
                if (rebuiltThumbInfo?.SecBuffer != null && rebuiltThumbInfo.InfoBuffer != null)
                {
                    using FileStream dest = new(saveThumbFileName, FileMode.Append, FileAccess.Write);
                    dest.Write(rebuiltThumbInfo.SecBuffer);
                    dest.Write(rebuiltThumbInfo.InfoBuffer);
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        // 単発救済で拾えた代表画像を、一般タブの複数パネル表示にも横展開しておく。
        private static void TryFanOutSingleFrameRescueThumbnails(
            string representativeImagePath,
            ThumbnailJobContext context
        )
        {
            if (context?.QueueObj == null || string.IsNullOrWhiteSpace(context.SaveThumbFileName))
            {
                return;
            }

            string currentDirectory = Path.GetDirectoryName(context.SaveThumbFileName) ?? "";
            string thumbRoot = Directory.GetParent(currentDirectory)?.FullName ?? "";
            if (string.IsNullOrWhiteSpace(thumbRoot))
            {
                return;
            }

            string fileName = Path.GetFileName(context.SaveThumbFileName) ?? "";
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            string dbName = new DirectoryInfo(thumbRoot).Name;
            int[] siblingTabs = [0, 1, 2, 3, 4];
            foreach (int tabIndex in siblingTabs)
            {
                if (tabIndex == context.QueueObj.Tabindex)
                {
                    continue;
                }

                TabInfo siblingTab = new(tabIndex, dbName, thumbRoot);
                string siblingPath = Path.Combine(siblingTab.OutPath, fileName);
                if (
                    !TryCreateSingleFrameTileThumbnail(
                        representativeImagePath,
                        siblingPath,
                        siblingTab,
                        ThumbnailImageUtility.BuildAutoThumbInfo(siblingTab, context.DurationSec),
                        context.DurationSec,
                        out _,
                        out string siblingError
                    )
                )
                {
                    ThumbnailRuntimeLog.Write(
                        "thumbnail",
                        $"bookmark fallback sibling skipped: tab={tabIndex}, movie='{context.MovieFullPath}', err='{siblingError}'"
                    );
                    continue;
                }

                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"bookmark fallback sibling created: tab={tabIndex}, movie='{context.MovieFullPath}', path='{siblingPath}'"
                );
            }
        }

        private static string BuildSingleFrameTempPath(string saveThumbFileName, string engineId)
        {
            string directory = Path.GetDirectoryName(saveThumbFileName) ?? "";
            string fileName = Path.GetFileNameWithoutExtension(saveThumbFileName) ?? "thumb";
            string safeEngineId = string.IsNullOrWhiteSpace(engineId) ? "engine" : engineId;
            return Path.Combine(
                directory,
                $"{fileName}.single-frame.{safeEngineId}.{Guid.NewGuid():N}.jpg"
            );
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
            ThumbnailJobContext context,
            bool recoveryOnePassAttempted
        )
        {
            Result = result;
            ProcessEngineId = processEngineId ?? "";
            EngineErrorMessages = engineErrorMessages ?? [];
            Context = context;
            RecoveryOnePassAttempted = recoveryOnePassAttempted;
        }

        public ThumbnailCreateResult Result { get; }

        public string ProcessEngineId { get; }

        public List<string> EngineErrorMessages { get; }

        public ThumbnailJobContext Context { get; }

        public bool RecoveryOnePassAttempted { get; }
    }

    internal sealed class ThumbnailSingleFrameFallbackResult
    {
        private ThumbnailSingleFrameFallbackResult(
            bool isSuccess,
            ThumbnailCreateResult result,
            string processEngineId
        )
        {
            IsSuccess = isSuccess;
            Result = result;
            ProcessEngineId = processEngineId ?? "";
        }

        public bool IsSuccess { get; }

        public ThumbnailCreateResult Result { get; }

        public string ProcessEngineId { get; }

        public static ThumbnailSingleFrameFallbackResult NoChange()
        {
            return new ThumbnailSingleFrameFallbackResult(false, null, "");
        }

        public static ThumbnailSingleFrameFallbackResult Success(
            ThumbnailCreateResult result,
            string processEngineId
        )
        {
            return new ThumbnailSingleFrameFallbackResult(true, result, processEngineId);
        }
    }
}
