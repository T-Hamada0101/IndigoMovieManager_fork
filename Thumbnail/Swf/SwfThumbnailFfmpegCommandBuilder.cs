using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace IndigoMovieManager.Thumbnail.Swf
{
    /// <summary>
    /// SWF専用のffmpeg引数を組み立てる。
    /// </summary>
    internal static class SwfThumbnailFfmpegCommandBuilder
    {
        public static ProcessStartInfo BuildProcessStartInfo(
            string ffmpegExePath,
            string inputPath,
            string outputPath,
            double captureSec,
            SwfThumbnailCaptureOptions options
        )
        {
            options ??= SwfThumbnailCaptureOptions.CreateDefault(320, 240);

            ProcessStartInfo psi = new()
            {
                FileName = ffmpegExePath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };

            foreach (string arg in BuildArguments(inputPath, outputPath, captureSec, options))
            {
                psi.ArgumentList.Add(arg);
            }

            return psi;
        }

        public static IReadOnlyList<string> BuildArguments(
            string inputPath,
            string outputPath,
            double captureSec,
            SwfThumbnailCaptureOptions options
        )
        {
            options ??= SwfThumbnailCaptureOptions.CreateDefault(320, 240);
            string seekText = captureSec.ToString("0.###", CultureInfo.InvariantCulture);
            string vf =
                $"scale={options.Width}:{options.Height}:flags={options.ScaleFlags}";

            return
            [
                "-y",
                "-hide_banner",
                "-loglevel",
                "error",
                "-an",
                "-sn",
                "-dn",
                "-i",
                inputPath,
                "-ss",
                seekText,
                "-frames:v",
                "1",
                "-pix_fmt",
                "yuv420p",
                "-q:v",
                options.JpegQuality.ToString(CultureInfo.InvariantCulture),
                "-vf",
                vf,
                outputPath,
            ];
        }
    }
}
