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
                return DependencyProperty.UnsetValue;
            }

            bool isExists = values[1] is not bool exists || exists;
            int decodePixelHeight = NoLockImageConverter.ResolveDecodePixelHeight(parameter);
            return NoLockImageConverter.ConvertImageRequest(
                request,
                isExists: isExists,
                decodePixelHeight: decodePixelHeight,
                logReason: "image.player-right-rail.sync-decode"
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
    }
}
