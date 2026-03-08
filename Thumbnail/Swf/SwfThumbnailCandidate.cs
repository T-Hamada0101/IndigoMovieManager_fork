using System;
using System.Globalization;

namespace IndigoMovieManager.Thumbnail.Swf
{
    /// <summary>
    /// SWF代表フレーム候補の試行結果をまとめる。
    /// </summary>
    internal sealed class SwfThumbnailCandidate
    {
        public double RequestedCaptureSec { get; init; }
        public bool IsProcessSucceeded { get; init; }
        public bool IsFrameAccepted { get; init; }
        public bool IsMostlyFlatBrightFrame { get; init; }
        public string CaptureKind { get; init; } = "";
        public string OutputPath { get; init; } = "";
        public string FailureReason { get; init; } = "";
        public string FfmpegError { get; init; } = "";

        // ログ出力時に候補の状態を短く読めるようにする。
        public string ToLogText()
        {
            string secText = RequestedCaptureSec.ToString("0.###", CultureInfo.InvariantCulture);
            return
                $"sec={secText}, kind={CaptureKind}, process_ok={IsProcessSucceeded}, accepted={IsFrameAccepted}, bright={IsMostlyFlatBrightFrame}, out='{OutputPath}', reason='{FailureReason}', err='{FfmpegError}'";
        }

        public static SwfThumbnailCandidate CreateAccepted(
            double requestedCaptureSec,
            string outputPath,
            string captureKind = "ffmpeg"
        )
        {
            return new SwfThumbnailCandidate
            {
                RequestedCaptureSec = requestedCaptureSec,
                IsProcessSucceeded = true,
                IsFrameAccepted = true,
                IsMostlyFlatBrightFrame = false,
                CaptureKind = captureKind ?? "",
                OutputPath = outputPath ?? "",
            };
        }

        public static SwfThumbnailCandidate CreateRejected(
            double requestedCaptureSec,
            string outputPath,
            string failureReason,
            string ffmpegError,
            bool isMostlyFlatBrightFrame,
            bool isProcessSucceeded,
            string captureKind = ""
        )
        {
            return new SwfThumbnailCandidate
            {
                RequestedCaptureSec = requestedCaptureSec,
                IsProcessSucceeded = isProcessSucceeded,
                IsFrameAccepted = false,
                IsMostlyFlatBrightFrame = isMostlyFlatBrightFrame,
                CaptureKind = captureKind ?? "",
                OutputPath = outputPath ?? "",
                FailureReason = failureReason ?? "",
                FfmpegError = ffmpegError ?? "",
            };
        }
    }
}
