using System.Globalization;
using System.Windows;
using System.Windows.Data;
using IndigoMovieManager.Converter;
using IndigoMovieManager.Thumbnail.QueueDb;
using IndigoMovieManager.UpperTabs.Common;

namespace IndigoMovieManager.BottomTabs.ThumbnailError
{
    /// <summary>
    /// 下側 ERROR 一覧の画像要求を ImageRequest 語彙へ寄せる薄い入口。
    /// </summary>
    public sealed class ThumbnailErrorImageSourceConverter : IMultiValueConverter
    {
        public object Convert(
            object[] values,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            if (values == null || values.Length < 2)
            {
                return DependencyProperty.UnsetValue;
            }

            object revisionValue = values.Length > 2 ? values[2] : null;
            ImageRequest request = ImageRequest.ForThumbnailErrorList(
                values[0] as string,
                ResolveMoviePathKey(values[1]),
                ResolveImageRequestRevision(revisionValue)
            );

            if (!ShouldApplyThumbnailErrorListImageRequest(request))
            {
                return DependencyProperty.UnsetValue;
            }

            int decodePixelHeight = NoLockImageConverter.ResolveDecodePixelHeight(parameter);
            return NoLockImageConverter.ConvertFilePath(
                request.ThumbnailPath,
                isExists: true,
                decodePixelHeight
            );
        }

        public object[] ConvertBack(
            object value,
            Type[] targetTypes,
            object parameter,
            CultureInfo culture
        )
        {
            throw new NotImplementedException();
        }

        internal static bool ShouldApplyThumbnailErrorListImageRequest(ImageRequest request)
        {
            return request.ThumbnailRole == ImageRequestThumbnailRole.ThumbnailErrorList
                && request.ShouldDecode;
        }

        internal static int ResolveImageRequestRevision(object revisionValue)
        {
            return revisionValue switch
            {
                int intValue => intValue,
                long longValue when longValue >= int.MinValue && longValue <= int.MaxValue =>
                    (int)longValue,
                _ => 0,
            };
        }

        private static string ResolveMoviePathKey(object moviePathValue)
        {
            if (moviePathValue is not string moviePath || string.IsNullOrWhiteSpace(moviePath))
            {
                return "";
            }

            return QueueDbPathResolver.CreateMoviePathKey(moviePath) ?? "";
        }
    }
}
