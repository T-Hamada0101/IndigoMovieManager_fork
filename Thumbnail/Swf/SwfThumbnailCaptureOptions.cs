using System;
using System.Collections.ObjectModel;
using System.Threading;

namespace IndigoMovieManager.Thumbnail.Swf
{
    /// <summary>
    /// SWF代表フレーム取得に必要な設定をひとまとめにする。
    /// </summary>
    internal sealed class SwfThumbnailCaptureOptions
    {
        private static readonly TimeSpan DefaultProcessTimeout = TimeSpan.FromSeconds(30);

        public int Width { get; init; } = 320;
        public int Height { get; init; } = 240;
        public ReadOnlyCollection<double> CandidateSeconds { get; init; } =
            Array.AsReadOnly([2d, 5d, 0d]);
        public TimeSpan ProcessTimeout { get; init; } = DefaultProcessTimeout;
        public int JpegQuality { get; init; } = 5;
        public string ScaleFlags { get; init; } = "bilinear";
        public double BrightLumaThreshold { get; init; } = 248d;
        public double FlatVarianceThreshold { get; init; } = 18d;
        public double BrightPixelRatioThreshold { get; init; } = 0.94d;
        public int MaxSampleGridSide { get; init; } = 24;

        public static SwfThumbnailCaptureOptions CreateDefault(int width, int height)
        {
            return new SwfThumbnailCaptureOptions
            {
                Width = width > 0 ? width : 320,
                Height = height > 0 ? height : 240,
            };
        }
    }
}
