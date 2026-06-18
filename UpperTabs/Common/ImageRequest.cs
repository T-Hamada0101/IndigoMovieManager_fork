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
            return new ImageLoadResult(
                request,
                ImageLoadOutcome.Failed,
                HasResolvedImage: usesPlaceholder,
                UsesPlaceholder: usesPlaceholder,
                IsStale: false,
                FailureReason: failureReason ?? "",
                ResultRevision: resultRevision
            );
        }
    }

    internal static class ImageLoadLogFields
    {
        internal static string Build(ImageLoadResult result)
        {
            return
                $"image_role={result.ImageRequest.ThumbnailRole} image_request_revision={result.ImageRequest.RequestRevision} image_result_revision={result.ResultRevision} image_outcome={result.OutcomeLogValue} resolved={FormatLogBool(result.HasResolvedImage)} placeholder={FormatLogBool(result.UsesPlaceholder)} stale={FormatLogBool(result.IsStale)} failure_reason={result.FailureReason ?? ""}";
        }

        private static string FormatLogBool(bool value)
        {
            return value ? "true" : "false";
        }
    }
}
