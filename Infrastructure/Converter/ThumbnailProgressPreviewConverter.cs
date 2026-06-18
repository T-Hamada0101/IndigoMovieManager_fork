using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.UpperTabs.Common;

namespace IndigoMovieManager.Converter
{
    /// <summary>
    /// ミニパネル画像は「メモリプレビュー優先、無ければファイル読み込み」に切り替える。
    /// </summary>
    internal sealed class ThumbnailProgressPreviewConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string previewCacheKey = values != null && values.Length > 0 ? values[0] as string : "";
            long previewRevision = ReadPreviewRevision(values);
            if (
                !string.IsNullOrWhiteSpace(previewCacheKey)
                && ThumbnailPreviewCache.Shared.TryGet(previewCacheKey, out ImageSource imageSource)
            )
            {
                ThumbnailPreviewLatencyTracker.RecordDisplayed(
                    previewCacheKey,
                    previewRevision,
                    "memory"
                );
                return imageSource;
            }

            object fallbackValue = values != null && values.Length > 2 ? values[2] : null;
            ImageRequest fallbackRequest = ImageRequest.ForThumbnailProgressPreview(
                fallbackValue as string,
                previewCacheKey,
                ResolveImageRequestRevision(previewRevision)
            );
            int decodePixelHeight = ResolveDecodePixelHeight(parameter);
            object fallback = NoLockImageConverter.ConvertImageRequest(
                fallbackRequest,
                isExists: true,
                decodePixelHeight: decodePixelHeight,
                logReason: "image.thumbnail-progress-preview.sync-decode"
            );
            if (!ReferenceEquals(fallback, Binding.DoNothing))
            {
                ThumbnailPreviewLatencyTracker.RecordDisplayed(
                    previewCacheKey,
                    previewRevision,
                    "file"
                );
            }

            return fallback;
        }

        public object[] ConvertBack(
            object value,
            Type[] targetTypes,
            object parameter,
            CultureInfo culture
        )
        {
            throw new NotSupportedException();
        }

        private static long ReadPreviewRevision(object[] values)
        {
            if (values == null || values.Length < 2 || values[1] == null)
            {
                return 0;
            }

            object value = values[1];
            if (value is long longValue)
            {
                return longValue;
            }
            if (value is int intValue)
            {
                return intValue;
            }
            if (value is double doubleValue)
            {
                return (long)doubleValue;
            }
            if (
                value is string text
                && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            )
            {
                return parsed;
            }

            return 0;
        }

        private static int ResolveImageRequestRevision(long previewRevision)
        {
            if (previewRevision < int.MinValue || previewRevision > int.MaxValue)
            {
                return 0;
            }

            return (int)previewRevision;
        }

        private static int ResolveDecodePixelHeight(object parameter)
        {
            return NoLockImageConverter.ResolveDecodePixelHeight(parameter);
        }
    }
}
