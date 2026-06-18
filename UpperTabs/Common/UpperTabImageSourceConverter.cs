using System.Globalization;
using System.Windows;
using System.Windows.Data;
using IndigoMovieManager.Converter;

namespace IndigoMovieManager.UpperTabs.Common
{
    /// <summary>
    /// 上側タブ専用の画像 converter。
    /// 非アクティブ中は再評価を止め、選択中だけ decode を走らせる。
    /// </summary>
    public sealed class UpperTabImageSourceConverter : IMultiValueConverter
    {
        public object Convert(
            object[] values,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            if (values == null || values.Length < 3)
            {
                return DependencyProperty.UnsetValue;
            }

            object moviePathValue = values.Length > 3 ? values[3] : null;
            ImageRequest request = UpperTabActivationGate.CreateUpperTabImageRequest(
                values[0],
                values[2],
                moviePathValue,
                requestRevision: 0
            );
            if (!UpperTabActivationGate.ShouldApplyImageRequest(request))
            {
                // Recycling されたコンテナへ前の画像が残らないよう、非対象時は明示的に空へ戻す。
                return DependencyProperty.UnsetValue;
            }

            bool isExists = values[1] is not bool exists || exists;
            int decodePixelHeight = NoLockImageConverter.ResolveDecodePixelHeight(parameter);
            ImageDecodeRequest decodeRequest = NoLockImageConverter.BuildImageDecodeRequest(
                request,
                decodePixelHeight,
                "image.upper-tab.sync-decode"
            );
            NoLockImageConverter.ImageDecodeExecutionResult executionResult =
                NoLockImageConverter.ConvertDecodeRequest(decodeRequest, isExists);
            ImageDecodeResult decodeResult = executionResult.DecodeResult;
            return ResolveUpperTabDecodeImage(executionResult.Image, decodeResult);
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

        private static object ResolveUpperTabDecodeImage(
            object image,
            ImageDecodeResult decodeResult
        )
        {
            // decode 済み結果も ImageRequest の役割で受け直し、別用途の画像結果を混ぜない。
            return decodeResult.ImageRequest.ThumbnailRole == ImageRequestThumbnailRole.UpperTab
                ? image
                : DependencyProperty.UnsetValue;
        }
    }
}
