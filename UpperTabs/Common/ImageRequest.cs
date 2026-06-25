using System;

namespace IndigoMovieManager.UpperTabs.Common
{
    internal enum ImageRequestThumbnailRole
    {
        UpperTab,
        ExtensionDetail,
        PlayerRightRail,
        ThumbnailProgressPreview,
        ThumbnailErrorList,
    }

    internal enum ImageRequestCachePolicy
    {
        UseConverterCache,
    }

    [Flags]
    internal enum ImageProbeFlags
    {
        None = 0,
        Missing = 1,
        ErrorMarker = 2,
        Stamp = 4,
    }

    internal enum ImageProbeOutcome
    {
        Unknown = 0,
        Found = 1,
        Missing = 2,
        ErrorMarker = 3,
    }

    internal enum ImageLoadOutcome
    {
        Unknown = 0,
        Ready = 1,
        Missing = 2,
        Canceled = 3,
        Failed = 4,
    }

    internal readonly record struct ImageRequest(
        string ThumbnailPath,
        string MoviePathKey,
        ImageRequestThumbnailRole ThumbnailRole,
        bool IsVisiblePriority,
        ImageRequestCachePolicy CachePolicy,
        int RequestRevision
    )
    {
        internal bool ShouldDecode => IsVisiblePriority;

        internal static ImageRequest ForUpperTab(
            string thumbnailPath,
            string moviePathKey,
            bool isVisiblePriority,
            int requestRevision
        )
        {
            return new ImageRequest(
                thumbnailPath ?? "",
                moviePathKey ?? "",
                ImageRequestThumbnailRole.UpperTab,
                isVisiblePriority,
                ImageRequestCachePolicy.UseConverterCache,
                requestRevision
            );
        }

        internal static ImageRequest ForExtensionDetail(
            string thumbnailPath,
            string moviePathKey,
            bool isVisiblePriority,
            int requestRevision
        )
        {
            return new ImageRequest(
                thumbnailPath ?? "",
                moviePathKey ?? "",
                ImageRequestThumbnailRole.ExtensionDetail,
                isVisiblePriority,
                ImageRequestCachePolicy.UseConverterCache,
                requestRevision
            );
        }

        internal static ImageRequest ForPlayerRightRail(
            string thumbnailPath,
            string moviePathKey,
            bool isVisiblePriority,
            int requestRevision
        )
        {
            return new ImageRequest(
                thumbnailPath ?? "",
                moviePathKey ?? "",
                ImageRequestThumbnailRole.PlayerRightRail,
                isVisiblePriority,
                ImageRequestCachePolicy.UseConverterCache,
                requestRevision
            );
        }

        internal static ImageRequest ForThumbnailProgressPreview(
            string thumbnailPath,
            string previewCacheKey,
            int requestRevision
        )
        {
            return new ImageRequest(
                thumbnailPath ?? "",
                previewCacheKey ?? "",
                ImageRequestThumbnailRole.ThumbnailProgressPreview,
                true,
                ImageRequestCachePolicy.UseConverterCache,
                requestRevision
            );
        }

        internal static ImageRequest ForThumbnailErrorList(
            string thumbnailPath,
            string moviePathKey,
            int requestRevision
        )
        {
            return new ImageRequest(
                thumbnailPath ?? "",
                moviePathKey ?? "",
                ImageRequestThumbnailRole.ThumbnailErrorList,
                true,
                ImageRequestCachePolicy.UseConverterCache,
                requestRevision
            );
        }
    }

    internal readonly record struct ImageProbeRequest(
        ImageRequest ImageRequest,
        ImageProbeFlags ProbeFlags,
        string LogReason
    )
    {
        internal bool RequiresMissingProbe => ProbeFlags.HasFlag(ImageProbeFlags.Missing);
        internal bool RequiresErrorMarkerProbe => ProbeFlags.HasFlag(ImageProbeFlags.ErrorMarker);
        internal bool RequiresStampProbe => ProbeFlags.HasFlag(ImageProbeFlags.Stamp);

        internal static ImageProbeRequest ForExtensionDetailStatus(ImageRequest imageRequest)
        {
            return new ImageProbeRequest(
                imageRequest,
                ImageProbeFlags.Missing | ImageProbeFlags.ErrorMarker | ImageProbeFlags.Stamp,
                "image.extension-detail.probe"
            );
        }
    }

    internal readonly record struct ImageProbeResult(
        ImageProbeOutcome Outcome,
        bool IsMissing,
        bool HasErrorMarker,
        long StampUtcTicks
    )
    {
        internal string OutcomeLogValue => Outcome switch
        {
            ImageProbeOutcome.Found => "found",
            ImageProbeOutcome.Missing => "missing",
            ImageProbeOutcome.ErrorMarker => "error-marker",
            _ => "unknown",
        };
    }

    internal static class ImageProbeLogFields
    {
        internal static string Build(ImageProbeRequest request, ImageProbeResult result)
        {
            return
                $"image_log_reason={request.LogReason ?? ""} image_role={request.ImageRequest.ThumbnailRole} probe_flags={request.ProbeFlags} probe_outcome={result.OutcomeLogValue} missing={FormatLogBool(result.IsMissing)} error_marker={FormatLogBool(result.HasErrorMarker)} stamp_utc_ticks={result.StampUtcTicks}";
        }

        private static string FormatLogBool(bool value)
        {
            return value ? "true" : "false";
        }
    }

    internal readonly record struct ImageDecodeRequest(
        ImageRequest ImageRequest,
        int DecodePixelHeight,
        string LogReason,
        int RequestRevision
    )
    {
        internal static ImageDecodeRequest ForSynchronousDecode(
            ImageRequest imageRequest,
            int decodePixelHeight,
            string logReason
        )
        {
            return new ImageDecodeRequest(
                imageRequest,
                decodePixelHeight > 0 ? decodePixelHeight : 0,
                logReason ?? "",
                imageRequest.RequestRevision
            );
        }
    }

    internal readonly record struct ImageLoadResult(
        ImageRequest ImageRequest,
        ImageLoadOutcome Outcome,
        bool HasResolvedImage,
        bool UsesPlaceholder,
        bool IsStale,
        string FailureReason,
        int ResultRevision
    )
    {
        internal string OutcomeLogValue => Outcome switch
        {
            ImageLoadOutcome.Ready => "ready",
            ImageLoadOutcome.Missing => "missing",
            ImageLoadOutcome.Canceled => "canceled",
            ImageLoadOutcome.Failed => "failed",
            _ => "unknown",
        };

        internal static ImageLoadResult Ready(
            ImageRequest request,
            bool usesPlaceholder,
            int resultRevision
        )
        {
            return new ImageLoadResult(
                request,
                ImageLoadOutcome.Ready,
                HasResolvedImage: true,
                UsesPlaceholder: usesPlaceholder,
                IsStale: false,
                FailureReason: "",
                ResultRevision: resultRevision
            );
        }

        internal static ImageLoadResult Missing(ImageRequest request, int resultRevision)
        {
            return new ImageLoadResult(
                request,
                ImageLoadOutcome.Missing,
                HasResolvedImage: false,
                UsesPlaceholder: false,
                IsStale: false,
                FailureReason: "",
                ResultRevision: resultRevision
            );
        }

        internal static ImageLoadResult Canceled(
            ImageRequest request,
            int resultRevision,
            string failureReason,
            bool isStale
        )
        {
            return new ImageLoadResult(
                request,
                ImageLoadOutcome.Canceled,
                HasResolvedImage: false,
                UsesPlaceholder: false,
                IsStale: isStale,
                FailureReason: failureReason ?? "",
                ResultRevision: resultRevision
            );
        }

        internal static ImageLoadResult Failed(
            ImageRequest request,
            int resultRevision,
            string failureReason,
            bool usesPlaceholder
        )
        {
            return Failed(
                request,
                resultRevision,
                failureReason,
                usesPlaceholder,
                hasResolvedImage: usesPlaceholder
            );
        }

        internal static ImageLoadResult Failed(
            ImageRequest request,
            int resultRevision,
            string failureReason,
            bool usesPlaceholder,
            bool hasResolvedImage
        )
        {
            return new ImageLoadResult(
                request,
                ImageLoadOutcome.Failed,
                HasResolvedImage: hasResolvedImage,
                UsesPlaceholder: usesPlaceholder,
                IsStale: false,
                FailureReason: failureReason ?? "",
                ResultRevision: resultRevision
            );
        }
    }

    internal readonly record struct ImageDecodeResult(
        ImageLoadResult ImageLoadResult,
        long DecodeElapsedMilliseconds,
        bool CacheHit
    )
    {
        internal ImageRequest ImageRequest => ImageLoadResult.ImageRequest;
        internal ImageLoadOutcome Outcome => ImageLoadResult.Outcome;
    }

    internal readonly record struct ImageDecodePlanResult(
        ImageDecodeRequest DecodeRequest,
        ImageDecodeResult DecodeResult,
        bool DecodeAttempted
    )
    {
        internal ImageLoadResult ImageLoadResult => DecodeResult.ImageLoadResult;

        internal static ImageDecodePlanResult FromBackgroundProbe(
            ImageDecodeRequest decodeRequest,
            ImageLoadResult loadResult
        )
        {
            // 背景 probe では表示互換を変えず、decode 予定と判定結果だけを同じ語彙へ畳む。
            return new ImageDecodePlanResult(
                decodeRequest,
                new ImageDecodeResult(
                    loadResult,
                    DecodeElapsedMilliseconds: 0,
                    CacheHit: false
                ),
                DecodeAttempted: false
            );
        }
    }

    internal static class ImageDecodeLogFields
    {
        internal static string Build(ImageDecodeRequest request, ImageDecodeResult result)
        {
            return
                $"image_log_reason={request.LogReason ?? ""} image_role={request.ImageRequest.ThumbnailRole} image_request_revision={request.RequestRevision} visible_priority={FormatLogBool(request.ImageRequest.IsVisiblePriority)} image_cache_policy={request.ImageRequest.CachePolicy} should_decode={FormatLogBool(request.ImageRequest.ShouldDecode)} decode_pixel_height={request.DecodePixelHeight} decode_elapsed_ms={result.DecodeElapsedMilliseconds} cache_hit={FormatLogBool(result.CacheHit)} image_outcome={result.ImageLoadResult.OutcomeLogValue}";
        }

        private static string FormatLogBool(bool value)
        {
            return value ? "true" : "false";
        }
    }

    internal static class ImageDecodePlanLogFields
    {
        internal static string Build(ImageDecodePlanResult result)
        {
            return
                $"{ImageDecodeLogFields.Build(result.DecodeRequest, result.DecodeResult)} decode_attempted={FormatLogBool(result.DecodeAttempted)} {ImageLoadLogFields.BuildStateSuffix(result.ImageLoadResult)}";
        }

        private static string FormatLogBool(bool value)
        {
            return value ? "true" : "false";
        }
    }

    internal static class ImageLoadLogFields
    {
        internal static string Build(ImageLoadResult result)
        {
            return
                $"image_role={result.ImageRequest.ThumbnailRole} image_request_revision={result.ImageRequest.RequestRevision} visible_priority={FormatLogBool(result.ImageRequest.IsVisiblePriority)} image_cache_policy={result.ImageRequest.CachePolicy} should_decode={FormatLogBool(result.ImageRequest.ShouldDecode)} {BuildStateSuffixCore(result, includeOutcome: true)}";
        }

        internal static string BuildStateSuffix(ImageLoadResult result)
        {
            return BuildStateSuffixCore(result, includeOutcome: false);
        }

        private static string BuildStateSuffixCore(ImageLoadResult result, bool includeOutcome)
        {
            string outcomeField = includeOutcome ? $" image_outcome={result.OutcomeLogValue}" : "";
            return
                $"image_result_revision={result.ResultRevision}{outcomeField} resolved={FormatLogBool(result.HasResolvedImage)} placeholder={FormatLogBool(result.UsesPlaceholder)} stale={FormatLogBool(result.IsStale)} failure_reason={result.FailureReason ?? ""}";
        }

        private static string FormatLogBool(bool value)
        {
            return value ? "true" : "false";
        }
    }
}
