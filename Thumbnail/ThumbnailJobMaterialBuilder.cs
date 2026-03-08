namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 実行前に必要な素材をまとめて解決する。
    /// duration / thumbInfo / bitrate / codec を service 本体から切り離す。
    /// </summary>
    internal sealed class ThumbnailJobMaterialBuilder
    {
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
                }
                else
                {
                    durationSec = ThumbnailShellMetadataUtility.TryGetDurationSecFromShell(
                        request.WorkingMovieFullPath
                    );
                }
            }

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
            }
            else
            {
                thumbInfo = ThumbnailImageUtility.BuildAutoThumbInfo(
                    request.TabInfo,
                    durationSec
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
