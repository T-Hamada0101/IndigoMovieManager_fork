using System.Drawing;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail.Swf
{
    /// <summary>
    /// SWF 専用の統合口。
    /// 代表フレーム取得から既存サムネ形式への整形、失敗時の縮退までをここで閉じる。
    /// </summary>
    internal sealed class SwfThumbnailRouteHandler
    {
        private readonly SwfThumbnailGenerationService swfThumbnailGenerationService;

        public SwfThumbnailRouteHandler(SwfThumbnailGenerationService swfThumbnailGenerationService)
        {
            this.swfThumbnailGenerationService =
                swfThumbnailGenerationService ?? new SwfThumbnailGenerationService();
        }

        public async Task<SwfThumbnailRouteResult> HandleAsync(
            SwfThumbnailRouteRequest request,
            CancellationToken cts = default
        )
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            string representativeTempPath = BuildRepresentativeTempPath(request.SaveThumbFileName);
            SwfThumbnailCaptureOptions captureOptions = CreateCaptureOptions(request.TabInfo);
            try
            {
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"swf delegated: movie='{request.MovieFullPath}', detail='{request.Detail}'"
                );

                SwfThumbnailCandidate candidate = await swfThumbnailGenerationService
                    .TryCaptureRepresentativeFrameAsync(
                        request.MovieFullPath,
                        representativeTempPath,
                        captureOptions,
                        cts
                    )
                    .ConfigureAwait(false);

                if (candidate?.IsFrameAccepted == true && Path.Exists(candidate.OutputPath))
                {
                    string processEngineId = ResolveProcessEngineId(candidate);
                    if (
                        TryCreateOutput(
                            candidate.OutputPath,
                            request.SaveThumbFileName,
                            request.TabInfo,
                            candidate.RequestedCaptureSec,
                            out ThumbnailPreviewFrame previewFrame,
                            out string outputError
                        )
                    )
                    {
                        ThumbnailRuntimeLog.Write(
                            "thumbnail",
                            $"swf thumbnail accepted: movie='{request.MovieFullPath}', kind='{candidate.CaptureKind}', sec={candidate.RequestedCaptureSec:0.###}, out='{request.SaveThumbFileName}'"
                        );
                        return SwfThumbnailRouteResult.Complete(
                            ThumbnailResultFactory.CreateSuccess(
                                request.SaveThumbFileName,
                                request.DurationSec,
                                previewFrame,
                                failureStage: "swf-route"
                            ),
                            processEngineId,
                            "swf",
                            request.FileSizeBytes
                        );
                    }

                    string outputFailure = $"swf thumbnail finalize failed: {outputError}";
                    ThumbnailRuntimeLog.Write(
                        "thumbnail",
                        $"swf finalize failed: movie='{request.MovieFullPath}', reason='{outputFailure}'"
                    );
                    return SwfThumbnailRouteResult.Complete(
                        ThumbnailResultFactory.CreateFailed(
                            request.SaveThumbFileName,
                            request.DurationSec,
                            outputFailure,
                            failureStage: "swf-finalize"
                        ),
                        "swf-ffmpeg-finalize",
                        "swf",
                        request.FileSizeBytes
                    );
                }

                string swfFailureReason = candidate?.FailureReason ?? "swf capture failed";
                string swfFfmpegError = candidate?.FfmpegError ?? "";
                string swfFailureText = string.IsNullOrWhiteSpace(swfFfmpegError)
                    ? swfFailureReason
                    : $"{swfFailureReason}, ffmpeg='{swfFfmpegError}'";

                ThumbnailJobContext context = new()
                {
                    QueueObj = request.QueueObj,
                    TabInfo = request.TabInfo,
                    ThumbInfo = ThumbnailImageUtility.BuildAutoThumbInfo(
                        request.TabInfo,
                        request.DurationSec
                    ),
                    MovieFullPath = request.MovieFullPath,
                    SaveThumbFileName = request.SaveThumbFileName,
                    IsResizeThumb = request.IsResizeThumb,
                    IsManual = request.IsManual,
                    DurationSec = request.DurationSec,
                    FileSizeBytes = request.FileSizeBytes,
                    AverageBitrateMbps = null,
                    HasEmojiPath = false,
                    VideoCodec = "",
                };

                if (
                    ThumbnailPlaceholderUtility.TryCreateFailurePlaceholderThumbnail(
                        context,
                        FailurePlaceholderKind.FlashVideo,
                        out string placeholderDetail
                    )
                )
                {
                    ThumbnailRuntimeLog.Write(
                        "thumbnail",
                        $"swf placeholder fallback: movie='{request.MovieFullPath}', reason='{swfFailureText}', placeholder='{placeholderDetail}'"
                    );
                    return SwfThumbnailRouteResult.Complete(
                        ThumbnailResultFactory.CreateSuccess(
                            request.SaveThumbFileName,
                            request.DurationSec,
                            failureStage: "swf-route",
                            policyDecision: "swf-placeholder",
                            placeholderAction: "created",
                            placeholderKind: FailurePlaceholderKind.FlashVideo.ToString()
                        ),
                        "swf-placeholder",
                        "swf",
                        request.FileSizeBytes
                    );
                }

                string swfError = $"swf capture failed and placeholder failed: {swfFailureText}";
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"swf fallback failed: movie='{request.MovieFullPath}', reason='{swfError}'"
                );
                return SwfThumbnailRouteResult.Complete(
                    ThumbnailResultFactory.CreateFailed(
                        request.SaveThumbFileName,
                        request.DurationSec,
                        swfError,
                        failureStage: "swf-route",
                        policyDecision: "swf-placeholder-failed",
                        placeholderAction: "failed",
                        placeholderKind: FailurePlaceholderKind.FlashVideo.ToString()
                    ),
                    "swf-failed",
                    "swf",
                    request.FileSizeBytes
                );
            }
            finally
            {
                TryDeleteFileQuietly(representativeTempPath);
            }
        }

        // SWF 代表フレーム用の一時保存先を、本番出力と同じディレクトリ配下へ切る。
        private static string BuildRepresentativeTempPath(string saveThumbFileName)
        {
            string directory = Path.GetDirectoryName(saveThumbFileName) ?? Path.GetTempPath();
            // 代表画像の一時保存先も本番出力と同じ配下へ置くので、先にフォルダを保証する。
            Directory.CreateDirectory(directory);
            string fileName = Path.GetFileNameWithoutExtension(saveThumbFileName);
            return Path.Combine(directory, $"{fileName}.swf-representative.jpg");
        }

        // まずは既定サイズだけ既存TabInfoへ合わせ、細かな調整はSWF側で閉じる。
        private static SwfThumbnailCaptureOptions CreateCaptureOptions(TabInfo tabInfo)
        {
            int width = Math.Max(1, tabInfo?.Width ?? 320);
            int height = Math.Max(1, tabInfo?.Height ?? 240);
            return SwfThumbnailCaptureOptions.CreateDefault(width, height);
        }

        private static string ResolveProcessEngineId(SwfThumbnailCandidate candidate)
        {
            if (string.Equals(candidate?.CaptureKind, "extract", StringComparison.OrdinalIgnoreCase))
            {
                return "swf-extract";
            }

            return "swf-ffmpeg";
        }

        // 代表1枚を既存タイル形式へ複製し、後段互換のサムネイルへ仕上げる。
        private static bool TryCreateOutput(
            string representativeImagePath,
            string saveThumbFileName,
            TabInfo tabInfo,
            double requestedCaptureSec,
            out ThumbnailPreviewFrame previewFrame,
            out string errorMessage
        )
        {
            previewFrame = null;
            errorMessage = "";

            if (string.IsNullOrWhiteSpace(representativeImagePath) || !Path.Exists(representativeImagePath))
            {
                errorMessage = "representative image is missing";
                return false;
            }

            if (tabInfo == null)
            {
                errorMessage = "tab info is missing";
                return false;
            }

            try
            {
                using Bitmap representativeBitmap = new(representativeImagePath);
                previewFrame = ThumbnailImageUtility.CreatePreviewFrameFromBitmap(
                    representativeBitmap,
                    120
                );

                int columns = Math.Max(1, tabInfo.Columns);
                int rows = Math.Max(1, tabInfo.Rows);
                int count = Math.Max(1, columns * rows);
                List<Bitmap> frames = [];
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        frames.Add(new Bitmap(representativeBitmap));
                    }

                    if (
                        !ThumbnailImageUtility.SaveCombinedThumbnail(
                            saveThumbFileName,
                            frames,
                            columns,
                            rows
                        )
                    )
                    {
                        errorMessage = "combined thumbnail save failed";
                        return false;
                    }
                }
                finally
                {
                    for (int i = 0; i < frames.Count; i++)
                    {
                        frames[i]?.Dispose();
                    }
                }

                ThumbInfo thumbInfo = ThumbnailImageUtility.BuildSwfThumbInfo(
                    tabInfo,
                    requestedCaptureSec
                );
                using FileStream dest = new(saveThumbFileName, FileMode.Append, FileAccess.Write);
                if (thumbInfo.SecBuffer != null && thumbInfo.InfoBuffer != null)
                {
                    dest.Write(thumbInfo.SecBuffer);
                    dest.Write(thumbInfo.InfoBuffer);
                }
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static void TryDeleteFileQuietly(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && Path.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // SWF 一時ファイル掃除失敗では処理を止めない。
            }
        }
    }

    internal sealed class SwfThumbnailRouteRequest
    {
        public QueueObj QueueObj { get; init; }

        public TabInfo TabInfo { get; init; }

        public string MovieFullPath { get; init; } = "";

        public string SaveThumbFileName { get; init; } = "";

        public string Detail { get; init; } = "";

        public bool IsResizeThumb { get; init; }

        public bool IsManual { get; init; }

        public double? DurationSec { get; init; }

        public long FileSizeBytes { get; init; }
    }

    internal sealed class SwfThumbnailRouteResult
    {
        private SwfThumbnailRouteResult(
            ThumbnailCreateResult result,
            string processEngineId,
            string videoCodec,
            long fileSizeBytes
        )
        {
            Result = result;
            ProcessEngineId = processEngineId ?? "";
            VideoCodec = videoCodec ?? "";
            FileSizeBytes = fileSizeBytes;
        }

        public ThumbnailCreateResult Result { get; }

        public string ProcessEngineId { get; }

        public string VideoCodec { get; }

        public long FileSizeBytes { get; }

        public static SwfThumbnailRouteResult Complete(
            ThumbnailCreateResult result,
            string processEngineId,
            string videoCodec,
            long fileSizeBytes
        )
        {
            return new SwfThumbnailRouteResult(result, processEngineId, videoCodec, fileSizeBytes);
        }
    }
}
