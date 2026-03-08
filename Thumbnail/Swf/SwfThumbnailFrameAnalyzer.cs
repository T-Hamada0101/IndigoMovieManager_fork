using System;
using System.Drawing;
using System.IO;

namespace IndigoMovieManager.Thumbnail.Swf
{
    /// <summary>
    /// SWFサムネイルが白画面寄りかを軽量判定する。
    /// </summary>
    internal static class SwfThumbnailFrameAnalyzer
    {
        public static bool TryAnalyzeFrame(
            string imagePath,
            SwfThumbnailCaptureOptions options,
            out bool isMostlyFlatBrightFrame
        )
        {
            isMostlyFlatBrightFrame = false;
            if (string.IsNullOrWhiteSpace(imagePath) || !Path.Exists(imagePath))
            {
                return false;
            }

            try
            {
                using Bitmap bitmap = new(imagePath);
                isMostlyFlatBrightFrame = IsMostlyFlatBrightFrame(bitmap, options);
                return true;
            }
            catch
            {
                // 壊れた画像は解析失敗として上位へ返し、誤採用を防ぐ。
                isMostlyFlatBrightFrame = false;
                return false;
            }
        }

        public static bool IsMostlyFlatBrightFrame(
            Bitmap bitmap,
            SwfThumbnailCaptureOptions options
        )
        {
            if (bitmap == null || bitmap.Width < 1 || bitmap.Height < 1)
            {
                return false;
            }

            options ??= SwfThumbnailCaptureOptions.CreateDefault(bitmap.Width, bitmap.Height);

            int sampleDiv = Math.Max(2, options.MaxSampleGridSide);
            int stepX = Math.Max(1, bitmap.Width / sampleDiv);
            int stepY = Math.Max(1, bitmap.Height / sampleDiv);
            int sampleCount = 0;
            int brightPixelCount = 0;
            double lumaSum = 0d;
            double lumaSquaredSum = 0d;

            // 画像全体を粗くなめ、真っ白に近く変化が乏しいかを見る。
            for (int y = 0; y < bitmap.Height; y += stepY)
            {
                for (int x = 0; x < bitmap.Width; x += stepX)
                {
                    Color color = bitmap.GetPixel(x, y);
                    double luma = (0.299d * color.R) + (0.587d * color.G) + (0.114d * color.B);
                    lumaSum += luma;
                    lumaSquaredSum += luma * luma;
                    sampleCount++;
                    if (luma >= options.BrightLumaThreshold)
                    {
                        brightPixelCount++;
                    }
                }
            }

            if (sampleCount < 1)
            {
                return false;
            }

            double averageLuma = lumaSum / sampleCount;
            double variance = (lumaSquaredSum / sampleCount) - (averageLuma * averageLuma);
            double brightRatio = (double)brightPixelCount / sampleCount;

            return averageLuma >= options.BrightLumaThreshold
                && variance <= options.FlatVarianceThreshold
                && brightRatio >= options.BrightPixelRatioThreshold;
        }
    }
}
