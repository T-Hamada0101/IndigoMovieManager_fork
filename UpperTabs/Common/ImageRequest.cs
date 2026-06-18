using System;

namespace IndigoMovieManager.UpperTabs.Common
{
    internal enum ImageRequestThumbnailRole
    {
        UpperTab,
        ExtensionDetail,
        PlayerRightRail,
        ThumbnailProgressPreview,
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
}
