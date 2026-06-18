namespace IndigoMovieManager.UpperTabs.Common
{
    internal enum ImageRequestThumbnailRole
    {
        UpperTab,
        ExtensionDetail,
    }

    internal enum ImageRequestCachePolicy
    {
        UseConverterCache,
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
    }
}
