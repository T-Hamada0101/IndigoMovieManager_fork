using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 実行前に即時確定できる分岐をまとめる。
    /// SWF の別経路は触らず、manual / missing movie / DRM / unsupported だけを扱う。
    /// </summary>
    internal static class ThumbnailPreflightChecker
    {
        public static ThumbnailPreflightCheckResult Evaluate(ThumbnailPreflightCheckRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (request.IsManual && !Path.Exists(request.SaveThumbFileName))
            {
                return ThumbnailPreflightCheckResult.Complete(
                    ThumbnailResultFactory.CreateFailed(
                        request.SaveThumbFileName,
                        request.DurationSec,
                        "manual target thumbnail does not exist",
                        failureStage: "preflight-manual-target-missing"
                    ),
                    "precheck",
                    "",
                    0
                );
            }

            if (!Path.Exists(request.TabInfo.OutPath))
            {
                Directory.CreateDirectory(request.TabInfo.OutPath);
            }

            if (!Path.Exists(request.MovieFullPath))
            {
                if (!Path.Exists(request.SaveThumbFileName))
                {
                    string noFileJpeg = ResolveMissingMovieThumbnailPath(request.TabIndex);
                    File.Copy(noFileJpeg, request.SaveThumbFileName, true);
                }

                return ThumbnailPreflightCheckResult.Complete(
                    ThumbnailResultFactory.CreateSuccess(
                        request.SaveThumbFileName,
                        request.DurationSec,
                        failureStage: "preflight-missing-movie",
                        policyDecision: "missing-movie-fixed-image"
                    ),
                    "missing-movie",
                    "",
                    0
                );
            }

            if (!request.IsManual && request.CacheMeta.IsDrmSuspected)
            {
                return HandleDrmPrecheck(request);
            }

            if (!request.IsManual && request.CacheMeta.IsUnsupportedPrecheck)
            {
                return HandleUnsupportedPrecheck(request);
            }

            return ThumbnailPreflightCheckResult.Continue();
        }

        private static ThumbnailPreflightCheckResult HandleDrmPrecheck(
            ThumbnailPreflightCheckRequest request
        )
        {
            string drmDetail = string.IsNullOrWhiteSpace(request.CacheMeta.DrmDetail)
                ? "drm_suspected"
                : request.CacheMeta.DrmDetail;
            ThumbnailJobContext context = CreatePlaceholderContext(request);

            if (
                ThumbnailPlaceholderUtility.TryCreateFailurePlaceholderThumbnail(
                    context,
                    FailurePlaceholderKind.DrmSuspected,
                    out string placeholderDetail
                )
            )
            {
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"drm precheck hit: movie='{request.MovieFullPath}', detail='{drmDetail}', placeholder='{placeholderDetail}'"
                );
                return ThumbnailPreflightCheckResult.Complete(
                    ThumbnailResultFactory.CreateSuccess(
                        request.SaveThumbFileName,
                        request.DurationSec,
                        failureStage: "preflight-drm",
                        policyDecision: "drm-precheck-placeholder",
                        placeholderAction: "created",
                        placeholderKind: FailurePlaceholderKind.DrmSuspected.ToString()
                    ),
                    "placeholder-drm-precheck",
                    "",
                    request.FileSizeBytes
                );
            }

            if (
                ThumbnailPlaceholderUtility.TryCopyFixedErrorThumbnailForTab(
                    request.TabIndex,
                    request.SaveThumbFileName,
                    out string fixedDetail
                )
            )
            {
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"drm precheck fixed thumbnail fallback: movie='{request.MovieFullPath}', detail='{drmDetail}', fixed='{fixedDetail}'"
                );
                return ThumbnailPreflightCheckResult.Complete(
                    ThumbnailResultFactory.CreateSuccess(
                        request.SaveThumbFileName,
                        request.DurationSec,
                        failureStage: "preflight-drm",
                        policyDecision: "drm-precheck-fixed-image",
                        placeholderAction: "fixed-image-fallback",
                        placeholderKind: FailurePlaceholderKind.DrmSuspected.ToString()
                    ),
                    "fixed-drm-precheck",
                    "",
                    request.FileSizeBytes
                );
            }

            string error = $"drm precheck hit but placeholder failed: {drmDetail}";
            ThumbnailRuntimeLog.Write(
                "thumbnail",
                $"drm precheck failed: movie='{request.MovieFullPath}', reason='{error}'"
            );
            return ThumbnailPreflightCheckResult.Complete(
                ThumbnailResultFactory.CreateFailed(
                    request.SaveThumbFileName,
                    request.DurationSec,
                    error,
                    failureStage: "preflight-drm",
                    policyDecision: "drm-precheck-placeholder-failed",
                    placeholderAction: "failed",
                    placeholderKind: FailurePlaceholderKind.DrmSuspected.ToString()
                ),
                "drm-precheck",
                "",
                request.FileSizeBytes
            );
        }

        private static ThumbnailPreflightCheckResult HandleUnsupportedPrecheck(
            ThumbnailPreflightCheckRequest request
        )
        {
            string unsupportedDetail = string.IsNullOrWhiteSpace(request.CacheMeta.UnsupportedDetail)
                ? "unsupported_precheck_hit"
                : request.CacheMeta.UnsupportedDetail;
            ThumbnailJobContext context = CreatePlaceholderContext(request);

            if (
                ThumbnailPlaceholderUtility.TryCreateFailurePlaceholderThumbnail(
                    context,
                    FailurePlaceholderKind.FlashVideo,
                    out string placeholderDetail
                )
            )
            {
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"unsupported precheck hit: movie='{request.MovieFullPath}', detail='{unsupportedDetail}', placeholder='{placeholderDetail}'"
                );
                return ThumbnailPreflightCheckResult.Complete(
                    ThumbnailResultFactory.CreateSuccess(
                        request.SaveThumbFileName,
                        request.DurationSec,
                        failureStage: "preflight-unsupported",
                        policyDecision: "unsupported-precheck-placeholder",
                        placeholderAction: "created",
                        placeholderKind: FailurePlaceholderKind.FlashVideo.ToString()
                    ),
                    "placeholder-unsupported-precheck",
                    "",
                    request.FileSizeBytes
                );
            }

            if (
                ThumbnailPlaceholderUtility.TryCopyFixedErrorThumbnailForTab(
                    request.TabIndex,
                    request.SaveThumbFileName,
                    out string fixedDetail
                )
            )
            {
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"unsupported precheck fixed thumbnail fallback: movie='{request.MovieFullPath}', detail='{unsupportedDetail}', fixed='{fixedDetail}'"
                );
                return ThumbnailPreflightCheckResult.Complete(
                    ThumbnailResultFactory.CreateSuccess(
                        request.SaveThumbFileName,
                        request.DurationSec,
                        failureStage: "preflight-unsupported",
                        policyDecision: "unsupported-precheck-fixed-image",
                        placeholderAction: "fixed-image-fallback",
                        placeholderKind: FailurePlaceholderKind.FlashVideo.ToString()
                    ),
                    "fixed-unsupported-precheck",
                    "",
                    request.FileSizeBytes
                );
            }

            string error =
                $"unsupported precheck hit but placeholder failed: {unsupportedDetail}";
            ThumbnailRuntimeLog.Write(
                "thumbnail",
                $"unsupported precheck failed: movie='{request.MovieFullPath}', reason='{error}'"
            );
            return ThumbnailPreflightCheckResult.Complete(
                ThumbnailResultFactory.CreateFailed(
                    request.SaveThumbFileName,
                    request.DurationSec,
                    error,
                    failureStage: "preflight-unsupported",
                    policyDecision: "unsupported-precheck-placeholder-failed",
                    placeholderAction: "failed",
                    placeholderKind: FailurePlaceholderKind.FlashVideo.ToString()
                ),
                "unsupported-precheck",
                "",
                request.FileSizeBytes
            );
        }

        private static ThumbnailJobContext CreatePlaceholderContext(
            ThumbnailPreflightCheckRequest request
        )
        {
            return new ThumbnailJobContext
            {
                QueueObj = request.QueueObj,
                TabInfo = request.TabInfo,
                ThumbInfo = ThumbnailImageUtility.BuildAutoThumbInfo(
                    request.TabInfo,
                    request.DurationSec
                ),
                MovieFullPath = request.MovieFullPath,
                SaveThumbFileName = request.SaveThumbFileName,
                IsResizeThumb = request.IsResizeThumb,
                IsManual = request.IsManual,
                DurationSec = request.DurationSec,
                FileSizeBytes = request.FileSizeBytes,
                AverageBitrateMbps = null,
                HasEmojiPath = false,
                VideoCodec = "",
            };
        }

        private static string ResolveMissingMovieThumbnailPath(int tabIndex)
        {
            string imageDir = Path.Combine(Directory.GetCurrentDirectory(), "Images");
            return tabIndex switch
            {
                0 => Path.Combine(imageDir, "noFileSmall.jpg"),
                1 => Path.Combine(imageDir, "noFileBig.jpg"),
                2 => Path.Combine(imageDir, "noFileGrid.jpg"),
                3 => Path.Combine(imageDir, "noFileList.jpg"),
                4 => Path.Combine(imageDir, "noFileBig.jpg"),
                99 => Path.Combine(imageDir, "noFileGrid.jpg"),
                _ => Path.Combine(imageDir, "noFileSmall.jpg"),
            };
        }
    }

    internal sealed class ThumbnailPreflightCheckRequest
    {
        public QueueObj QueueObj { get; init; }

        public TabInfo TabInfo { get; init; }

        public CachedMovieMeta CacheMeta { get; init; }

        public string MovieFullPath { get; init; } = "";

        public string SaveThumbFileName { get; init; } = "";

        public bool IsResizeThumb { get; init; }

        public bool IsManual { get; init; }

        public double? DurationSec { get; init; }

        public long FileSizeBytes { get; init; }

        public int TabIndex { get; init; }
    }

    internal sealed class ThumbnailPreflightCheckResult
    {
        private ThumbnailPreflightCheckResult(
            bool shouldReturn,
            ThumbnailCreateResult result,
            string processEngineId,
            string videoCodec,
            long fileSizeBytes
        )
        {
            ShouldReturn = shouldReturn;
            Result = result;
            ProcessEngineId = processEngineId ?? "";
            VideoCodec = videoCodec ?? "";
            FileSizeBytes = fileSizeBytes;
        }

        public bool ShouldReturn { get; }

        public ThumbnailCreateResult Result { get; }

        public string ProcessEngineId { get; }

        public string VideoCodec { get; }

        public long FileSizeBytes { get; }

        public static ThumbnailPreflightCheckResult Continue()
        {
            return new ThumbnailPreflightCheckResult(false, null, "", "", 0);
        }

        public static ThumbnailPreflightCheckResult Complete(
            ThumbnailCreateResult result,
            string processEngineId,
            string videoCodec,
            long fileSizeBytes
        )
        {
            return new ThumbnailPreflightCheckResult(
                true,
                result,
                processEngineId,
                videoCodec,
                fileSizeBytes
            );
        }
    }
}
