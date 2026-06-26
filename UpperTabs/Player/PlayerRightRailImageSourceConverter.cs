using System.Globalization;
using System.Windows;
using System.Windows.Data;
using IndigoMovieManager.Converter;
using IndigoMovieManager.UpperTabs.Common;

namespace IndigoMovieManager.UpperTabs.Player
{
    /// <summary>
    /// Player右レールの画像要求を ImageRequest 語彙へ寄せる薄い入口。
    /// </summary>
    public sealed class PlayerRightRailImageSourceConverter : IMultiValueConverter
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
            object revisionValue = values.Length > 4 ? values[4] : null;
            int requestRevision = UpperTabActivationGate.ResolveImageRequestRevision(revisionValue);
            ImageRequest request = UpperTabActivationGate.CreatePlayerRightRailImageRequest(
                values[0],
                values[2],
                moviePathValue,
                requestRevision
            );

            if (
                !UpperTabActivationGate.ShouldApplyPlayerRightRailImageRequest(
                    request,
                    requestRevision
                )
            )
            {
                return ResolveStalePlayerRightRailImageResult(
                    request,
                    requestRevision,
                    out _
                );
            }

            bool isExists = values[1] is not bool exists || exists;
            int decodePixelHeight = NoLockImageConverter.ResolveDecodePixelHeight(parameter);
            ImageDecodeRequest decodeRequest = NoLockImageConverter.BuildImageDecodeRequest(
                request,
                decodePixelHeight,
                "image.player-right-rail.sync-decode"
            );
            NoLockImageConverter.ImageDecodeExecutionResult executionResult =
                NoLockImageConverter.ConvertDecodeRequest(decodeRequest, isExists);
            ImageDecodeResult decodeResult = executionResult.DecodeResult;
            return ResolvePlayerRightRailDecodeImage(executionResult.Image, decodeResult);
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

        internal static object ResolveStalePlayerRightRailImageResult(
            ImageRequest request,
            int currentRevision,
            out ImageDecodeResult decodeResult
        )
        {
            ImageLoadResult loadResult = ImageLoadResult.Canceled(
                request,
                currentRevision,
                "stale-player-right-rail",
                isStale: true
            );
            global::IndigoMovieManager.DebugRuntimeLog.Write(
                "ui-tempo",
                $"player {ImageLoadLogFields.Build(loadResult)} image_event=right-rail-request-discarded"
            );
            decodeResult = new ImageDecodeResult(
                loadResult,
                DecodeElapsedMilliseconds: 0,
                CacheHit: false
            );
            return DependencyProperty.UnsetValue;
        }

        private static object ResolvePlayerRightRailDecodeImage(
            object image,
            ImageDecodeResult decodeResult
        )
        {
            return decodeResult.ImageRequest.ThumbnailRole == ImageRequestThumbnailRole.PlayerRightRail
                ? image
                : DependencyProperty.UnsetValue;
        }
    }
}
