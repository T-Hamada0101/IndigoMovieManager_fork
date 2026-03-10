using FFMediaToolkit;
using FFMediaToolkit.Decoding;
using OpenCvSharp;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 実行前に必要な素材をまとめて解決する。
    /// duration / thumbInfo / bitrate / codec を service 本体から切り離す。
    /// </summary>
    internal sealed class ThumbnailJobMaterialBuilder
    {
        private const double DurationMismatchRatioThreshold = 2.0;
        private const double DurationMismatchAbsoluteThresholdSec = 5.0;
        private readonly IVideoMetadataProvider videoMetadataProvider;

        public ThumbnailJobMaterialBuilder(IVideoMetadataProvider videoMetadataProvider)
        {
            this.videoMetadataProvider =
                videoMetadataProvider
                ?? throw new ArgumentNullException(nameof(videoMetadataProvider));
        }

        public ThumbnailJobMaterialBuildResult Build(ThumbnailJobMaterialBuildRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            double? durationSec = request.DurationSec;
            if (!durationSec.HasValue || durationSec.Value <= 0)
            {
                if (
                    videoMetadataProvider.TryGetDurationSec(
                        request.WorkingMovieFullPath,
                        out double providedDurationSec
                    )
                    && providedDurationSec > 0
                )
                {
                    durationSec = providedDurationSec;
                    WriteDurationResolveDebugLog(
                        request.WorkingMovieFullPath,
                        "provider",
                        durationSec
                    );
                }
                else
                {
                    durationSec = ThumbnailShellMetadataUtility.TryGetDurationSecFromShell(
                        request.WorkingMovieFullPath
                    );
                    WriteDurationResolveDebugLog(
                        request.WorkingMovieFullPath,
                        "shell",
                        durationSec
                    );
                }
            }
            else
            {
                WriteDurationResolveDebugLog(request.WorkingMovieFullPath, "request", durationSec);
            }

            durationSec = NormalizeDurationSecForAviIfNeeded(
                request.WorkingMovieFullPath,
                durationSec,
                request.IsManual
            );

            ThumbInfo thumbInfo;
            if (request.IsManual)
            {
                thumbInfo = new ThumbInfo();
                thumbInfo.GetThumbInfo(request.SaveThumbFileName);
                if (!thumbInfo.IsThumbnail)
                {
                    return ThumbnailJobMaterialBuildResult.Failed(
                        durationSec,
                        "manual source thumbnail metadata is missing"
                    );
                }

                if (
                    request.QueueObj?.ThumbPanelPos != null
                    && request.QueueObj.ThumbTimePos != null
                )
                {
                    int panelPos = (int)request.QueueObj.ThumbPanelPos;
                    if (panelPos >= 0 && panelPos < thumbInfo.ThumbSec.Count)
                    {
                        thumbInfo.ThumbSec[panelPos] = (int)request.QueueObj.ThumbTimePos;
                    }
                }
                thumbInfo.NewThumbInfo();
                WriteThumbInfoDebugLog(
                    request.WorkingMovieFullPath,
                    request.IsManual,
                    "manual",
                    durationSec,
                    request.QueueObj,
                    thumbInfo
                );
            }
            else
            {
                thumbInfo = ThumbnailImageUtility.BuildAutoThumbInfo(
                    request.TabInfo,
                    durationSec
                );
                WriteThumbInfoDebugLog(
                    request.WorkingMovieFullPath,
                    request.IsManual,
                    "auto",
                    durationSec,
                    request.QueueObj,
                    thumbInfo
                );
            }

            double? averageBitrateMbps = null;
            if (request.FileSizeBytes > 0 && durationSec.HasValue && durationSec.Value > 0)
            {
                averageBitrateMbps = (request.FileSizeBytes * 8d) / (durationSec.Value * 1_000_000d);
            }

            string videoCodec = "";
            if (
                videoMetadataProvider.TryGetVideoCodec(
                    request.WorkingMovieFullPath,
                    out string providedVideoCodec
                )
                && !string.IsNullOrWhiteSpace(providedVideoCodec)
            )
            {
                videoCodec = providedVideoCodec;
            }

            return ThumbnailJobMaterialBuildResult.Succeeded(
                durationSec,
                thumbInfo,
                averageBitrateMbps,
                videoCodec
            );
        }

        // AVI系はキャッシュ尺が壊れて残ることがあるため、サムネ生成直前で再検証して補正する。
        private double? NormalizeDurationSecForAviIfNeeded(
            string moviePath,
            double? currentDurationSec,
            bool isManual
        )
        {
            if (
                isManual
                || !currentDurationSec.HasValue
                || currentDurationSec.Value <= 0
                || !ShouldWriteThumbInfoDebugLog(moviePath)
            )
            {
                return currentDurationSec;
            }

            if (!TryResolveActualDurationSecForAvi(moviePath, out double actualDurationSec))
            {
                return currentDurationSec;
            }

            double diff = Math.Abs(currentDurationSec.Value - actualDurationSec);
            double ratio =
                currentDurationSec.Value > actualDurationSec
                    ? currentDurationSec.Value / actualDurationSec
                    : actualDurationSec / currentDurationSec.Value;
            if (
                diff < DurationMismatchAbsoluteThresholdSec
                || ratio < DurationMismatchRatioThreshold
            )
            {
                return currentDurationSec;
            }

            ThumbnailRuntimeLog.Write(
                "thumbinfo-build",
                $"duration corrected before auto thumb build: movie='{moviePath}' "
                    + $"cached_sec={currentDurationSec.Value:0.###} actual_sec={actualDurationSec:0.###} "
                    + $"diff_sec={diff:0.###} ratio={ratio:0.###}"
            );
            return actualDurationSec;
        }

        // AVI系は MovieInfo の3系統比較から実尺候補を拾う。
        // provider 自体が壊れた尺へ落ちるケースがあるため、ここだけは直接 probe する。
        private static bool TryResolveActualDurationSecForAvi(
            string moviePath,
            out double actualDurationSec
        )
        {
            actualDurationSec = 0;
            if (TryReadDurationByFfMediaToolkit(moviePath, out actualDurationSec))
            {
                ThumbnailRuntimeLog.Write(
                    "thumbinfo-build",
                    $"duration actual probe selected: movie='{moviePath}' source=ffmediatoolkit duration_sec={actualDurationSec:0.###}"
                );
                return true;
            }

            if (TryReadDurationByOpenCv(moviePath, out actualDurationSec))
            {
                ThumbnailRuntimeLog.Write(
                    "thumbinfo-build",
                    $"duration actual probe selected: movie='{moviePath}' source=opencv duration_sec={actualDurationSec:0.###}"
                );
                return true;
            }

            return false;
        }

        // まずは FFMediaToolkit の stream duration を優先する。
        private static bool TryReadDurationByFfMediaToolkit(
            string moviePath,
            out double durationSec
        )
        {
            durationSec = 0;
            try
            {
                string directory = Path.GetDirectoryName(moviePath) ?? "";
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    FFmpegLoader.FFmpegPath = directory;
                }

                using MediaFile file = MediaFile.Open(moviePath);
                TimeSpan duration = file.Info.Duration;
                if (duration.TotalSeconds <= 0)
                {
                    return false;
                }

                durationSec = duration.TotalSeconds;
                return true;
            }
            catch (Exception ex)
            {
                ThumbnailRuntimeLog.Write(
                    "thumbinfo-build",
                    $"duration actual ffmediatoolkit failed: movie='{moviePath}' err='{ex.Message}'"
                );
                return false;
            }
        }

        // FFMediaToolkit が倒れた環境でも OpenCV で実尺を拾えるようにしておく。
        private static bool TryReadDurationByOpenCv(string moviePath, out double durationSec)
        {
            durationSec = 0;
            try
            {
                using VideoCapture capture = new(moviePath);
                if (!capture.IsOpened())
                {
                    return false;
                }

                double frameCount = capture.Get(VideoCaptureProperties.FrameCount);
                double fps = capture.Get(VideoCaptureProperties.Fps);
                if (frameCount <= 0 || fps <= 0)
                {
                    return false;
                }

                durationSec = frameCount / fps;
                return durationSec > 0;
            }
            catch (Exception ex)
            {
                ThumbnailRuntimeLog.Write(
                    "thumbinfo-build",
                    $"duration actual opencv failed: movie='{moviePath}' err='{ex.Message}'"
                );
                return false;
            }
        }

        // 尺がどこから入ったかを残して、壊れた値の混入元を追えるようにする。
        private static void WriteDurationResolveDebugLog(
            string moviePath,
            string source,
            double? durationSec
        )
        {
            if (!ShouldWriteThumbInfoDebugLog(moviePath))
            {
                return;
            }

            ThumbnailRuntimeLog.Write(
                "thumbinfo-build",
                $"duration source resolved: movie='{moviePath}' source={source} duration_sec={durationSec:0.###}"
            );
        }

        // seek異常の切り分け用に、ThumbSec確定時点の入力値を残す。
        private static void WriteThumbInfoDebugLog(
            string moviePath,
            bool isManual,
            string source,
            double? durationSec,
            QueueObj queueObj,
            ThumbInfo thumbInfo
        )
        {
            if (!ShouldWriteThumbInfoDebugLog(moviePath))
            {
                return;
            }

            string thumbSecText =
                thumbInfo?.ThumbSec == null ? "" : string.Join(",", thumbInfo.ThumbSec);
            string queuePanelText = queueObj?.ThumbPanelPos?.ToString() ?? "";
            string queueTimeText = queueObj?.ThumbTimePos?.ToString() ?? "";
            ThumbnailRuntimeLog.Write(
                "thumbinfo-build",
                $"source={source} manual={isManual} movie='{moviePath}' duration_sec={durationSec:0.###} "
                    + $"queue_panel={queuePanelText} queue_time={queueTimeText} thumb_sec=[{thumbSecText}]"
            );
        }

        private static bool ShouldWriteThumbInfoDebugLog(string moviePath)
        {
            string extension = Path.GetExtension(moviePath ?? "");
            return string.Equals(extension, ".avi", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".divx", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class ThumbnailJobMaterialBuildRequest
    {
        public QueueObj QueueObj { get; init; }

        public TabInfo TabInfo { get; init; }

        public string WorkingMovieFullPath { get; init; } = "";

        public string SaveThumbFileName { get; init; } = "";

        public bool IsManual { get; init; }

        public double? DurationSec { get; init; }

        public long FileSizeBytes { get; init; }
    }

    internal sealed class ThumbnailJobMaterialBuildResult
    {
        private ThumbnailJobMaterialBuildResult(
            bool isSuccess,
            double? durationSec,
            ThumbInfo thumbInfo,
            double? averageBitrateMbps,
            string videoCodec,
            string errorMessage
        )
        {
            IsSuccess = isSuccess;
            DurationSec = durationSec;
            ThumbInfo = thumbInfo;
            AverageBitrateMbps = averageBitrateMbps;
            VideoCodec = videoCodec ?? "";
            ErrorMessage = errorMessage ?? "";
        }

        public bool IsSuccess { get; }

        public double? DurationSec { get; }

        public ThumbInfo ThumbInfo { get; }

        public double? AverageBitrateMbps { get; }

        public string VideoCodec { get; }

        public string ErrorMessage { get; }

        public static ThumbnailJobMaterialBuildResult Succeeded(
            double? durationSec,
            ThumbInfo thumbInfo,
            double? averageBitrateMbps,
            string videoCodec
        )
        {
            return new ThumbnailJobMaterialBuildResult(
                true,
                durationSec,
                thumbInfo,
                averageBitrateMbps,
                videoCodec,
                ""
            );
        }

        public static ThumbnailJobMaterialBuildResult Failed(
            double? durationSec,
            string errorMessage
        )
        {
            return new ThumbnailJobMaterialBuildResult(
                false,
                durationSec,
                null,
                null,
                "",
                errorMessage
            );
        }
    }
}
