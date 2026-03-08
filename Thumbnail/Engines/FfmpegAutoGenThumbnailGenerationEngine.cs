using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;

namespace IndigoMovieManager.Thumbnail.Engines
{
    /// <summary>
    /// FFmpeg.AutoGen を用いてアンマネージド領域で高速にサムネイルを生成するエンジン。
    /// </summary>
    internal sealed class FfmpegAutoGenThumbnailGenerationEngine : IThumbnailGenerationEngine
    {
        private static bool _isInitialized;
        private static bool _initAttempted;
        private static string _initFailureReason = "";
        private static readonly object _initLock = new();

        public string EngineId => "autogen";
        public string EngineName => "autogen";

        public FfmpegAutoGenThumbnailGenerationEngine()
        {
            // コンストラクタでは重い初期化を行わず、実行時に遅延初期化する。
        }

        private static bool EnsureFfmpegInitializedSafe(out string errorMessage)
        {
            if (_isInitialized)
            {
                errorMessage = "";
                return true;
            }

            if (_initAttempted)
            {
                errorMessage = string.IsNullOrWhiteSpace(_initFailureReason)
                    ? "autogen initialization failed"
                    : _initFailureReason;
                return false;
            }

            lock (_initLock)
            {
                if (_isInitialized)
                {
                    errorMessage = "";
                    return true;
                }

                if (_initAttempted)
                {
                    errorMessage = string.IsNullOrWhiteSpace(_initFailureReason)
                        ? "autogen initialization failed"
                        : _initFailureReason;
                    return false;
                }

                _initAttempted = true;
                try
                {
                    // IMM_FFMPEG_EXE_PATH がファイルでもディレクトリでも扱えるように正規化する。
                    string ffmpegSharedDir = ResolveFfmpegSharedDirectory();
                    ffmpeg.RootPath = ffmpegSharedDir;
                    DynamicallyLoadedBindings.Initialize();
                    _isInitialized = true;
                    _initFailureReason = "";
                    errorMessage = "";
                    return true;
                }
                catch (Exception ex)
                {
                    _isInitialized = false;
                    _initFailureReason =
                        $"autogen init failed: {ex.GetType().Name}: {ex.Message}";
                    errorMessage = _initFailureReason;
                    ThumbnailRuntimeLog.Write("thumbnail", _initFailureReason);
                    return false;
                }
            }
        }

        private static string ResolveFfmpegSharedDirectory()
        {
            string configuredPath = ThumbnailEnvConfig.GetFfmpegExePath()?.Trim().Trim('"') ?? "";
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                if (Directory.Exists(configuredPath))
                {
                    return configuredPath;
                }

                if (File.Exists(configuredPath))
                {
                    string fromFile = Path.GetDirectoryName(configuredPath) ?? "";
                    if (!string.IsNullOrWhiteSpace(fromFile) && Directory.Exists(fromFile))
                    {
                        return fromFile;
                    }
                }
            }

            string bundled = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg-shared");
            if (Directory.Exists(bundled))
            {
                return bundled;
            }

            throw new DirectoryNotFoundException(
                "ffmpeg shared directory not found. expected tools/ffmpeg-shared or IMM_FFMPEG_EXE_PATH"
            );
        }

        private static string BuildInitFailedMessage(string initError, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(initError))
            {
                return initError;
            }

            return string.IsNullOrWhiteSpace(fallback) ? "autogen initialization failed" : fallback;
        }

        public bool CanHandle(ThumbnailJobContext context)
        {
            return EnsureFfmpegInitializedSafe(out _);
        }

        public Task<ThumbnailCreateResult> CreateAsync(
            ThumbnailJobContext context,
            CancellationToken cts = default
        )
        {
            return Task.Run(
                () =>
                {
                    if (!EnsureFfmpegInitializedSafe(out string initError))
                    {
                        return ThumbnailResultFactory.CreateFailed(
                            context?.SaveThumbFileName ?? "",
                            context?.DurationSec,
                            BuildInitFailedMessage(initError, _initFailureReason)
                        );
                    }

                    return CreateInternal(context, cts);
                },
                cts
            );
        }

        public Task<bool> CreateBookmarkAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePos,
            CancellationToken cts = default
        )
        {
            return Task.Run(
                () =>
                {
                    if (!EnsureFfmpegInitializedSafe(out _))
                    {
                        return false;
                    }

                    return CreateBookmarkInternal(movieFullPath, saveThumbPath, capturePos, cts);
                },
                cts
            );
        }

        private unsafe ThumbnailCreateResult CreateInternal(
            ThumbnailJobContext context,
            CancellationToken cts
        )
        {
            if (context == null)
                return ThumbnailResultFactory.CreateFailed("", null, "context is null");
            if (
                context.ThumbInfo == null
                || context.ThumbInfo.ThumbSec == null
                || context.ThumbInfo.ThumbSec.Count < 1
            )
                return ThumbnailResultFactory.CreateFailed(
                    context.SaveThumbFileName,
                    context.DurationSec,
                    "thumb info is empty"
                );

            double? durationSec = context.DurationSec;
            if (!durationSec.HasValue || durationSec.Value <= 0)
            {
                durationSec = ThumbnailShellMetadataUtility.TryGetDurationSecFromShell(
                    context.MovieFullPath
                );
            }

            int cols = context.TabInfo.Columns;
            int rows = context.TabInfo.Rows;
            if (cols < 1 || rows < 1)
                return ThumbnailResultFactory.CreateFailed(
                    context.SaveThumbFileName,
                    durationSec,
                    "invalid panel configuration"
                );

            string saveDir = Path.GetDirectoryName(context.SaveThumbFileName) ?? "";
            if (!string.IsNullOrWhiteSpace(saveDir))
            {
                Directory.CreateDirectory(saveDir);
            }

            int targetWidth = context.TabInfo.Width > 0 ? context.TabInfo.Width : 320;
            int targetHeight = context.TabInfo.Height > 0 ? context.TabInfo.Height : 240;

            AVFormatContext* pFormatContext = null;
            AVCodecContext* pCodecContext = null;
            AVFrame* pFrame = null;
            AVPacket* pPacket = null;
            SwsContext* pSwsContext = null;
            List<Bitmap> bitmaps = [];

            try
            {
                pFormatContext = ffmpeg.avformat_alloc_context();
                int ret = ffmpeg.avformat_open_input(
                    &pFormatContext,
                    context.MovieFullPath,
                    null,
                    null
                );
                if (ret < 0)
                {
                    return ThumbnailResultFactory.CreateFailed(
                        context.SaveThumbFileName,
                        durationSec,
                        "Failed to open input: " + GetErrorMessage(ret)
                    );
                }

                ret = ffmpeg.avformat_find_stream_info(pFormatContext, null);
                if (ret < 0)
                {
                    return ThumbnailResultFactory.CreateFailed(
                        context.SaveThumbFileName,
                        durationSec,
                        "Failed to find stream info: " + GetErrorMessage(ret)
                    );
                }

                AVStream* pStream = null;
                for (int i = 0; i < pFormatContext->nb_streams; i++)
                {
                    if (
                        pFormatContext->streams[i]->codecpar->codec_type
                        == AVMediaType.AVMEDIA_TYPE_VIDEO
                    )
                    {
                        pStream = pFormatContext->streams[i];
                        break;
                    }
                }

                if (pStream == null)
                    return ThumbnailResultFactory.CreateFailed(
                        context.SaveThumbFileName,
                        durationSec,
                        "Video stream not found"
                    );

                var codecId = pStream->codecpar->codec_id;
                var pCodec = ffmpeg.avcodec_find_decoder(codecId);
                if (pCodec == null)
                    return ThumbnailResultFactory.CreateFailed(
                        context.SaveThumbFileName,
                        durationSec,
                        "Decoder not found"
                    );

                // Get true duration from stream if possible
                if (pStream->duration > 0)
                {
                    double streamDur = pStream->duration * ffmpeg.av_q2d(pStream->time_base);
                    if (streamDur > 0)
                        durationSec = streamDur;
                }

                List<double> captureSecs = ResolveCaptureSeconds(context.ThumbInfo, durationSec);

                pCodecContext = ffmpeg.avcodec_alloc_context3(pCodec);
                ffmpeg.avcodec_parameters_to_context(pCodecContext, pStream->codecpar);

                // Hardware decoding could be injected here if needed

                ret = ffmpeg.avcodec_open2(pCodecContext, pCodec, null);
                if (ret < 0)
                    return ThumbnailResultFactory.CreateFailed(
                        context.SaveThumbFileName,
                        durationSec,
                        "Failed to open codec: " + GetErrorMessage(ret)
                    );

                AVPixelFormat sourcePixelFormat =
                    pCodecContext->pix_fmt == AVPixelFormat.AV_PIX_FMT_NONE
                        ? AVPixelFormat.AV_PIX_FMT_YUV420P
                        : pCodecContext->pix_fmt;

                pSwsContext = ffmpeg.sws_getContext(
                    pCodecContext->width,
                    pCodecContext->height,
                    sourcePixelFormat,
                    targetWidth,
                    targetHeight,
                    AVPixelFormat.AV_PIX_FMT_BGR24,
                    1, // SWS_FAST_BILINEAR
                    null,
                    null,
                    null
                );
                if (pSwsContext == null)
                {
                    return ThumbnailResultFactory.CreateFailed(
                        context.SaveThumbFileName,
                        durationSec,
                        "Failed to create sws context"
                    );
                }

                pPacket = ffmpeg.av_packet_alloc();
                pFrame = ffmpeg.av_frame_alloc();

                foreach (double sec in captureSecs)
                {
                    cts.ThrowIfCancellationRequested();

                    long ts = (long)(sec / ffmpeg.av_q2d(pStream->time_base));
                    ffmpeg.av_seek_frame(
                        pFormatContext,
                        pStream->index,
                        ts,
                        ffmpeg.AVSEEK_FLAG_BACKWARD
                    );
                    ffmpeg.avcodec_flush_buffers(pCodecContext);

                    bool frameCaptured = false;
                    bool streamEnded = false;
                    while (
                        !frameCaptured
                        && !streamEnded
                        && ffmpeg.av_read_frame(pFormatContext, pPacket) >= 0
                    )
                    {
                        cts.ThrowIfCancellationRequested();
                        try
                        {
                            if (pPacket->stream_index != pStream->index)
                            {
                                continue;
                            }

                            ret = ffmpeg.avcodec_send_packet(pCodecContext, pPacket);
                            if (
                                ret < 0
                                && ret != ffmpeg.AVERROR(ffmpeg.EAGAIN)
                                && ret != ffmpeg.AVERROR_EOF
                            )
                            {
                                continue;
                            }

                            while (true)
                            {
                                ret = ffmpeg.avcodec_receive_frame(pCodecContext, pFrame);
                                if (ret == 0)
                                {
                                    Bitmap bmp = ConvertFrameToBitmap(
                                        pFrame,
                                        pSwsContext,
                                        targetWidth,
                                        targetHeight
                                    );
                                    if (bmp != null)
                                    {
                                        bitmaps.Add(bmp);
                                        frameCaptured = true;
                                    }
                                    ffmpeg.av_frame_unref(pFrame);
                                    break;
                                }

                                if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                                {
                                    break;
                                }

                                if (ret == ffmpeg.AVERROR_EOF)
                                {
                                    streamEnded = true;
                                    break;
                                }

                                break;
                            }
                        }
                        finally
                        {
                            ffmpeg.av_packet_unref(pPacket);
                        }
                    }

                    ffmpeg.av_frame_unref(pFrame);
                }

                ThumbnailPreviewFrame previewFrame = null;
                if (bitmaps.Count > 0)
                {
                    // 先頭フレームをミニパネル先行表示用に抜き出す。
                    previewFrame = ThumbnailImageUtility.CreatePreviewFrameFromBitmap(
                        bitmaps[0],
                        120
                    );
                    if (
                        !ThumbnailImageUtility.SaveCombinedThumbnail(
                            context.SaveThumbFileName,
                            bitmaps,
                            cols,
                            rows
                        )
                    )
                    {
                        return ThumbnailResultFactory.CreateFailed(
                            context.SaveThumbFileName,
                            durationSec,
                            "autogen combined save failed"
                        );
                    }
                }
                else
                {
                    return ThumbnailResultFactory.CreateFailed(
                        context.SaveThumbFileName,
                        durationSec,
                        "No frames decoded"
                    );
                }

                return ThumbnailResultFactory.CreateSuccess(
                    context.SaveThumbFileName,
                    durationSec,
                    previewFrame
                );
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ThumbnailResultFactory.CreateFailed(
                    context.SaveThumbFileName,
                    durationSec,
                    ex.Message
                );
            }
            finally
            {
                if (pFrame != null)
                    ffmpeg.av_frame_free(&pFrame);
                if (pPacket != null)
                    ffmpeg.av_packet_free(&pPacket);
                if (pCodecContext != null)
                    ffmpeg.avcodec_free_context(&pCodecContext);
                if (pFormatContext != null)
                    ffmpeg.avformat_close_input(&pFormatContext);
                if (pSwsContext != null)
                    ffmpeg.sws_freeContext(pSwsContext);
                foreach (Bitmap bmp in bitmaps)
                {
                    bmp.Dispose();
                }
            }
        }

        // 1秒未満の超短尺は整数秒だけだと全部0秒へ潰れるため、実デコード位置だけ実時間で均等化する。
        internal static List<double> ResolveCaptureSeconds(ThumbInfo thumbInfo, double? durationSec)
        {
            List<double> captureSecs = [];
            if (thumbInfo?.ThumbSec == null || thumbInfo.ThumbSec.Count < 1)
            {
                return captureSecs;
            }

            foreach (int sec in thumbInfo.ThumbSec)
            {
                captureSecs.Add(sec);
            }

            if (!durationSec.HasValue || durationSec.Value <= 0)
            {
                return captureSecs;
            }

            double safeEndSec = Math.Max(0, durationSec.Value - 0.001);
            if (safeEndSec <= 0 || safeEndSec >= 1.0d)
            {
                return captureSecs;
            }

            bool allZero = thumbInfo.ThumbSec.All(sec => sec <= 0);
            if (!allZero)
            {
                return captureSecs;
            }

            captureSecs.Clear();
            int count = thumbInfo.ThumbSec.Count;
            for (int i = 1; i <= count; i++)
            {
                double posSec = safeEndSec * i / (count + 1d);
                captureSecs.Add(posSec);
            }

            return captureSecs;
        }

        private unsafe bool CreateBookmarkInternal(
            string movieFullPath,
            string saveThumbPath,
            int capturePos,
            CancellationToken cts
        )
        {
            cts.ThrowIfCancellationRequested();

            string saveDir = Path.GetDirectoryName(saveThumbPath) ?? "";
            if (!string.IsNullOrWhiteSpace(saveDir))
            {
                Directory.CreateDirectory(saveDir);
            }

            int targetWidth = 640;
            int targetHeight = 480;

            AVFormatContext* pFormatContext = null;
            AVCodecContext* pCodecContext = null;
            AVFrame* pFrame = null;
            AVPacket* pPacket = null;
            SwsContext* pSwsContext = null;

            try
            {
                pFormatContext = ffmpeg.avformat_alloc_context();
                if (ffmpeg.avformat_open_input(&pFormatContext, movieFullPath, null, null) < 0)
                    return false;
                if (ffmpeg.avformat_find_stream_info(pFormatContext, null) < 0)
                    return false;

                AVStream* pStream = null;
                for (int i = 0; i < pFormatContext->nb_streams; i++)
                {
                    if (
                        pFormatContext->streams[i]->codecpar->codec_type
                        == AVMediaType.AVMEDIA_TYPE_VIDEO
                    )
                    {
                        pStream = pFormatContext->streams[i];
                        break;
                    }
                }
                if (pStream == null)
                    return false;

                var pCodec = ffmpeg.avcodec_find_decoder(pStream->codecpar->codec_id);
                if (pCodec == null)
                    return false;

                pCodecContext = ffmpeg.avcodec_alloc_context3(pCodec);
                ffmpeg.avcodec_parameters_to_context(pCodecContext, pStream->codecpar);
                if (ffmpeg.avcodec_open2(pCodecContext, pCodec, null) < 0)
                    return false;

                AVPixelFormat sourcePixelFormat =
                    pCodecContext->pix_fmt == AVPixelFormat.AV_PIX_FMT_NONE
                        ? AVPixelFormat.AV_PIX_FMT_YUV420P
                        : pCodecContext->pix_fmt;

                pSwsContext = ffmpeg.sws_getContext(
                    pCodecContext->width,
                    pCodecContext->height,
                    sourcePixelFormat,
                    targetWidth,
                    targetHeight,
                    AVPixelFormat.AV_PIX_FMT_BGR24,
                    1,
                    null,
                    null,
                    null
                );
                if (pSwsContext == null)
                    return false;

                pPacket = ffmpeg.av_packet_alloc();
                pFrame = ffmpeg.av_frame_alloc();

                long ts = (long)(capturePos / ffmpeg.av_q2d(pStream->time_base));
                ffmpeg.av_seek_frame(
                    pFormatContext,
                    pStream->index,
                    ts,
                    ffmpeg.AVSEEK_FLAG_BACKWARD
                );
                ffmpeg.avcodec_flush_buffers(pCodecContext);

                Bitmap extracted = null;
                bool streamEnded = false;
                while (!streamEnded && ffmpeg.av_read_frame(pFormatContext, pPacket) >= 0)
                {
                    cts.ThrowIfCancellationRequested();
                    try
                    {
                        if (pPacket->stream_index != pStream->index)
                        {
                            continue;
                        }

                        int sendRet = ffmpeg.avcodec_send_packet(pCodecContext, pPacket);
                        if (
                            sendRet < 0
                            && sendRet != ffmpeg.AVERROR(ffmpeg.EAGAIN)
                            && sendRet != ffmpeg.AVERROR_EOF
                        )
                        {
                            continue;
                        }

                        while (true)
                        {
                            int ret = ffmpeg.avcodec_receive_frame(pCodecContext, pFrame);
                            if (ret == 0)
                            {
                                extracted = ConvertFrameToBitmap(
                                    pFrame,
                                    pSwsContext,
                                    targetWidth,
                                    targetHeight
                                );
                                ffmpeg.av_frame_unref(pFrame);
                                break;
                            }

                            if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                            {
                                break;
                            }

                            if (ret == ffmpeg.AVERROR_EOF)
                            {
                                streamEnded = true;
                                break;
                            }

                            break;
                        }
                    }
                    finally
                    {
                        ffmpeg.av_packet_unref(pPacket);
                    }

                    if (extracted != null)
                    {
                        break;
                    }
                }

                ffmpeg.av_frame_unref(pFrame);

                if (extracted != null)
                {
                    using (extracted)
                    {
                        if (
                            !ThumbnailImageUtility.TrySaveJpegWithRetry(
                                extracted,
                                saveThumbPath,
                                out _
                            )
                        )
                        {
                            return false;
                        }
                    }
                    return true;
                }
                return false;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (pFrame != null)
                    ffmpeg.av_frame_free(&pFrame);
                if (pPacket != null)
                    ffmpeg.av_packet_free(&pPacket);
                if (pCodecContext != null)
                    ffmpeg.avcodec_free_context(&pCodecContext);
                if (pFormatContext != null)
                    ffmpeg.avformat_close_input(&pFormatContext);
                if (pSwsContext != null)
                    ffmpeg.sws_freeContext(pSwsContext);
            }
        }

        private unsafe Bitmap ConvertFrameToBitmap(
            AVFrame* pFrame,
            SwsContext* pSwsContext,
            int width,
            int height
        )
        {
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            BitmapData bitmapData = null;
            int outputHeight;
            try
            {
                bitmapData = bitmap.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format24bppRgb
                );
                byte*[] dstData = [(byte*)bitmapData.Scan0];
                int[] dstLinesize = [bitmapData.Stride];

                outputHeight = ffmpeg.sws_scale(
                    pSwsContext,
                    pFrame->data,
                    pFrame->linesize,
                    0,
                    pFrame->height,
                    dstData,
                    dstLinesize
                );
            }
            finally
            {
                if (bitmapData != null)
                {
                    bitmap.UnlockBits(bitmapData);
                }
            }

            if (outputHeight <= 0)
            {
                bitmap.Dispose();
                return null;
            }
            return bitmap;
        }

        private static unsafe string GetErrorMessage(int error)
        {
            var bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
            return Marshal.PtrToStringAnsi((IntPtr)buffer) ?? "Unknown error";
        }
    }
}
