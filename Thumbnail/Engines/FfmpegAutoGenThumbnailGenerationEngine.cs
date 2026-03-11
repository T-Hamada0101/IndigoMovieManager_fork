using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
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
        private const double ShortClipFirstFrameSeekFallbackMaxDurationSec = 1.0d;
        private const long RobustProbeSizeBytes = 50L * 1024L * 1024L;
        private const long RobustAnalyzeDurationUsec = 15L * 1000L * 1000L;
        internal const string DemuxImmediateEofErrorDetail = "autogen demux immediate eof";
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

            bool enableInvestigationLog = IsAutogenSeekInvestigationMovie(context.MovieFullPath);
            if (enableInvestigationLog)
            {
                ThumbnailRuntimeLog.Write(
                    "autogen-investigation",
                    $"enter create: movie='{context.MovieFullPath}' duration_sec={FormatDurationForLog(durationSec)} "
                        + $"thumb_sec=[{string.Join(",", context.ThumbInfo.ThumbSec.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture)))}]"
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
            AVDictionary* pFormatOptions = null;
            List<Bitmap> bitmaps = [];
            bool observedDemuxImmediateEof = false;

            try
            {
                pFormatContext = ffmpeg.avformat_alloc_context();
                ApplyRobustInputProbeOptions(&pFormatOptions);
                int ret = ffmpeg.avformat_open_input(
                    &pFormatContext,
                    context.MovieFullPath,
                    null,
                    &pFormatOptions
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
                List<double> actualCaptureSecs = [];
                WriteFinalThumbInfoDebugLog(context, durationSec, captureSecs);

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

                bool enableSeekDebugLog = ShouldWriteSeekDebugLog(
                    context.MovieFullPath,
                    durationSec
                );
                bool preferClosestNonBlackThenLatestBright =
                    ShouldPreferClosestNonBlackThenLatestBright(durationSec, cols * rows);
                if (enableInvestigationLog)
                {
                    ThumbnailRuntimeLog.Write(
                        "autogen-investigation",
                        $"seek log flag: movie='{context.MovieFullPath}' enable_seek_debug={enableSeekDebugLog} "
                            + $"duration_sec={FormatDurationForLog(durationSec)}"
                    );
                }
                if (enableSeekDebugLog)
                {
                    ThumbnailRuntimeLog.Write(
                        "autogen-init",
                        $"movie='{context.MovieFullPath}' codec='{ffmpeg.avcodec_get_name(codecId)}' "
                            + $"stream_pix_fmt={(int)pStream->codecpar->format} codec_pix_fmt={(int)pCodecContext->pix_fmt} "
                            + $"stream_size={pStream->codecpar->width}x{pStream->codecpar->height} "
                            + $"codec_size={pCodecContext->width}x{pCodecContext->height}"
                    );
                }

                pPacket = ffmpeg.av_packet_alloc();
                pFrame = ffmpeg.av_frame_alloc();

                for (int i = 0; i < captureSecs.Count; i++)
                {
                    cts.ThrowIfCancellationRequested();
                    double sec = captureSecs[i];
                    if (
                        TryCaptureFrameAtSecond(
                            context.MovieFullPath,
                            sec,
                            pFormatContext,
                            pStream,
                            pCodecContext,
                            pPacket,
                            pFrame,
                            ref pSwsContext,
                            targetWidth,
                            targetHeight,
                            enableSeekDebugLog,
                            preferClosestNonBlackThenLatestBright,
                            cts,
                            ref observedDemuxImmediateEof,
                            out Bitmap capturedBitmap
                        )
                    )
                    {
                        bitmaps.Add(capturedBitmap);
                        actualCaptureSecs.Add(sec);
                        continue;
                    }

                    // 1枚目が外れた短尺だけ、先頭極小 seek を浅く試して拾える1枚を探す。
                    if (
                        i == 0
                        && bitmaps.Count < 1
                        && ShouldUseShortClipFirstFrameSeekFallback(durationSec, cols * rows)
                    )
                    {
                        IReadOnlyList<double> shortClipCandidateSecs =
                            BuildShortClipFirstFrameSeekCandidates(durationSec, sec);
                        ThumbnailRuntimeLog.Write(
                            "autogen-shortclip-firstframe-fallback",
                            $"fallback requested: movie='{context.MovieFullPath}' duration_sec={durationSec:0.###} "
                                + $"original_sec={sec:0.###} panel_count={cols * rows} "
                                + $"candidates=[{FormatCandidateSeconds(shortClipCandidateSecs)}]"
                        );
                        foreach (double candidateSec in shortClipCandidateSecs)
                        {
                            cts.ThrowIfCancellationRequested();
                            ThumbnailRuntimeLog.Write(
                                "autogen-shortclip-firstframe-fallback",
                                $"fallback try: movie='{context.MovieFullPath}' sec={candidateSec:0.###}"
                            );
                            if (
                                TryCaptureFrameAtSecond(
                                    context.MovieFullPath,
                                    candidateSec,
                                    pFormatContext,
                                    pStream,
                                    pCodecContext,
                                    pPacket,
                                    pFrame,
                                    ref pSwsContext,
                                    targetWidth,
                                    targetHeight,
                                    enableSeekDebugLog,
                                    preferClosestNonBlackThenLatestBright,
                                    cts,
                                    ref observedDemuxImmediateEof,
                                    out Bitmap shortClipBitmap
                                )
                            )
                            {
                                bitmaps.Add(shortClipBitmap);
                                actualCaptureSecs.Add(candidateSec);
                                ThumbnailRuntimeLog.Write(
                                    "autogen-shortclip-firstframe-fallback",
                                    $"fallback hit: movie='{context.MovieFullPath}' sec={candidateSec:0.###}"
                                );
                                break;
                            }
                        }

                        if (bitmaps.Count < 1)
                        {
                            ThumbnailRuntimeLog.Write(
                                "autogen-shortclip-firstframe-fallback",
                                $"fallback exhausted: movie='{context.MovieFullPath}' candidates=[{FormatCandidateSeconds(shortClipCandidateSecs)}]"
                            );
                        }
                    }
                }

                if (bitmaps.Count < 1)
                {
                    ThumbnailRuntimeLog.Write(
                        "autogen-header-frame-fallback",
                        $"fallback requested: movie='{context.MovieFullPath}' duration_sec={durationSec:0.###} cols={cols} rows={rows} "
                            + $"thumb_sec=[{string.Join(",", captureSecs.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture)))}]"
                    );
                    Bitmap headerFallbackBitmap = TryCaptureNearHeaderFallbackFrame(
                        context.MovieFullPath,
                        durationSec,
                        pFormatContext,
                        pStream,
                        pCodecContext,
                        pPacket,
                        pFrame,
                        ref pSwsContext,
                        targetWidth,
                        targetHeight,
                        enableSeekDebugLog,
                        preferClosestNonBlackThenLatestBright,
                        cts,
                        ref observedDemuxImmediateEof,
                        out double headerFallbackSec
                    );
                    if (headerFallbackBitmap != null)
                    {
                        // 先頭付近で1枚だけ拾えた時も、既存タイル形式で表示できるよう複製する。
                        ThumbnailRuntimeLog.Write(
                            "autogen-header-frame-fallback",
                            $"fallback tile replicate: movie='{context.MovieFullPath}' tile_count={cols * rows}"
                        );
                        for (int i = 0; i < cols * rows; i++)
                        {
                            bitmaps.Add((Bitmap)headerFallbackBitmap.Clone());
                        }
                        actualCaptureSecs = Enumerable
                            .Repeat(headerFallbackSec, cols * rows)
                            .ToList();
                        headerFallbackBitmap.Dispose();
                    }
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
                        BuildNoFramesDecodedMessage(observedDemuxImmediateEof)
                    );
                }

                ThumbInfo saveThumbInfo = ThumbnailImageUtility.RebuildThumbInfoWithCaptureSeconds(
                    context.ThumbInfo,
                    actualCaptureSecs
                );
                using FileStream dest = new(
                    context.SaveThumbFileName,
                    FileMode.Append,
                    FileAccess.Write
                );
                dest.Write(saveThumbInfo.SecBuffer);
                dest.Write(saveThumbInfo.InfoBuffer);

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
                if (pFormatOptions != null)
                    ffmpeg.av_dict_free(&pFormatOptions);
                foreach (Bitmap bmp in bitmaps)
                {
                    bmp.Dispose();
                }
            }
        }

        // 調査対象になりやすいAVI系だけ、seek詳細ログを出してデコード不能かシーク外しを判別する。
        internal static bool ShouldWriteSeekDebugLog(string movieFullPath, double? durationSec)
        {
            string extension = Path.GetExtension(movieFullPath ?? "");
            return string.Equals(extension, ".avi", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".divx", StringComparison.OrdinalIgnoreCase)
                || IsAutogenSeekInvestigationMovie(movieFullPath)
                || (durationSec.HasValue
                    && durationSec.Value > 0
                    && durationSec.Value <= ShortClipFirstFrameSeekFallbackMaxDurationSec);
        }

        internal static bool IsAutogenSeekInvestigationMovie(string movieFullPath)
        {
            string fileName = Path.GetFileName(movieFullPath ?? "");
            return fileName.IndexOf(
                    "真空エラー2_ghq5_temp",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0
                || fileName.IndexOf(
                    "ライブ配信真空エラー2_ghq5_temp",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0;
        }

        private static string FormatDurationForLog(double? durationSec)
        {
            if (!durationSec.HasValue)
            {
                return "";
            }

            return durationSec.Value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        internal enum DecodedFrameSelectionDecision
        {
            Ignore,
            KeepAsLatestBrightFallback,
            AcceptImmediately,
        }

        // 短尺・少パネルだけは、近傍の見えるコマを優先しつつ最後の救済を許可する。
        internal static bool ShouldPreferClosestNonBlackThenLatestBright(
            double? durationSec,
            int panelCount
        )
        {
            return ShouldUseShortClipFirstFrameSeekFallback(durationSec, panelCount);
        }

        // backward seek 直後に古い keyframe を拾うことがあるため、要求秒より十分手前のコマは採用しない。
        internal static bool ShouldAcceptDecodedFrameAtRequestedSecond(
            double requestedSec,
            double decodedPtsSec
        )
        {
            if (double.IsNaN(decodedPtsSec) || double.IsInfinity(decodedPtsSec) || decodedPtsSec < 0)
            {
                return true;
            }

            if (requestedSec <= 0)
            {
                return true;
            }

            double leadToleranceSec = Math.Min(0.1d, Math.Max(0.02d, requestedSec * 0.1d));
            return decodedPtsSec >= requestedSec - leadToleranceSec;
        }

        // 通常は従来どおり即採用し、短尺救済だけ黒回避と latest bright fallback を許可する。
        internal static DecodedFrameSelectionDecision ResolveDecodedFrameSelectionDecision(
            double requestedSec,
            double decodedPtsSec,
            bool isMostlyBlack,
            bool preferClosestNonBlackThenLatestBright
        )
        {
            bool isRequestedCompatible = ShouldAcceptDecodedFrameAtRequestedSecond(
                requestedSec,
                decodedPtsSec
            );
            if (!preferClosestNonBlackThenLatestBright)
            {
                return isRequestedCompatible
                    ? DecodedFrameSelectionDecision.AcceptImmediately
                    : DecodedFrameSelectionDecision.Ignore;
            }

            if (isRequestedCompatible && !isMostlyBlack)
            {
                return DecodedFrameSelectionDecision.AcceptImmediately;
            }

            if (!isMostlyBlack)
            {
                return DecodedFrameSelectionDecision.KeepAsLatestBrightFallback;
            }

            return DecodedFrameSelectionDecision.Ignore;
        }

        // autogen入口で最終的に使うThumbSecとcapture秒を残し、混入地点を切り分ける。
        private static void WriteFinalThumbInfoDebugLog(
            ThumbnailJobContext context,
            double? durationSec,
            List<double> captureSecs
        )
        {
            if (
                context == null
                || !ShouldWriteSeekDebugLog(context.MovieFullPath, durationSec)
            )
            {
                return;
            }

            string thumbSecText =
                context.ThumbInfo?.ThumbSec == null ? "" : string.Join(",", context.ThumbInfo.ThumbSec);
            string captureSecText =
                captureSecs == null
                    ? ""
                    : string.Join(",", captureSecs.Select(sec => sec.ToString("0.###")));
            string queuePanelText = context.QueueObj?.ThumbPanelPos?.ToString() ?? "";
            string queueTimeText = context.QueueObj?.ThumbTimePos?.ToString() ?? "";
            ThumbnailRuntimeLog.Write(
                "thumbinfo-final",
                $"engine=autogen movie='{context.MovieFullPath}' duration_sec={durationSec:0.###} "
                    + $"manual={context.IsManual} queue_panel={queuePanelText} queue_time={queueTimeText} "
                    + $"thumb_sec=[{thumbSecText}] capture_sec=[{captureSecText}]"
            );
        }

        // 通常 seek と先頭 fallback の両方で使う、指定秒からの単発フレーム取得。
        private unsafe static bool TryCaptureFrameAtSecond(
            string movieFullPath,
            double sec,
            AVFormatContext* pFormatContext,
            AVStream* pStream,
            AVCodecContext* pCodecContext,
            AVPacket* pPacket,
            AVFrame* pFrame,
            ref SwsContext* pSwsContext,
            int targetWidth,
            int targetHeight,
            bool enableSeekDebugLog,
            bool preferClosestNonBlackThenLatestBright,
            CancellationToken cts,
            ref bool observedDemuxImmediateEof,
            out Bitmap capturedBitmap
        )
        {
            capturedBitmap = null;
            Bitmap latestBrightFallbackBitmap = null;
            double latestBrightFallbackPtsSec = -1d;

            double timeBaseSec = ffmpeg.av_q2d(pStream->time_base);
            long streamTs = (long)(sec / timeBaseSec);
            bool zeroPacketSeekMissObserved = false;
            for (int seekAttempt = 0; seekAttempt < 2; seekAttempt++)
            {
                bool useContainerSeek = seekAttempt == 1;
                bool frameCaptured = false;
                bool streamEnded = false;
                int readPacketCount = 0;
                int skippedPacketCount = 0;
                int sentPacketCount = 0;
                int receiveFrameCount = 0;

                if (pFormatContext != null)
                {
                    ffmpeg.avformat_flush(pFormatContext);
                }
                ffmpeg.avcodec_flush_buffers(pCodecContext);

                int seekRet;
                string seekMode;
                long logTs;
                if (useContainerSeek)
                {
                    long globalTs = (long)(sec * ffmpeg.AV_TIME_BASE);
                    seekRet = ffmpeg.avformat_seek_file(
                        pFormatContext,
                        -1,
                        long.MinValue,
                        globalTs,
                        long.MaxValue,
                        ffmpeg.AVSEEK_FLAG_BACKWARD
                    );
                    seekMode = "container";
                    logTs = globalTs;
                }
                else
                {
                    seekRet = ffmpeg.av_seek_frame(
                        pFormatContext,
                        pStream->index,
                        streamTs,
                        ffmpeg.AVSEEK_FLAG_BACKWARD
                    );
                    seekMode = "stream";
                    logTs = streamTs;
                }

                if (enableSeekDebugLog)
                {
                    ThumbnailRuntimeLog.Write(
                        "autogen-seek",
                        $"seek start: movie='{movieFullPath}' requested_sec={sec:0.###} mode={seekMode} ts={logTs} time_base={timeBaseSec:0.########} ret={seekRet}"
                    );
                }

                if (seekRet < 0)
                {
                    if (enableSeekDebugLog)
                    {
                        ThumbnailRuntimeLog.Write(
                            "autogen-seek",
                            $"seek failed: movie='{movieFullPath}' requested_sec={sec:0.###} mode={seekMode} ret={seekRet} err='{GetErrorMessage(seekRet)}'"
                        );
                    }
                    continue;
                }

                while (!frameCaptured && !streamEnded)
                {
                    int readRet = ffmpeg.av_read_frame(pFormatContext, pPacket);
                    if (readRet < 0)
                    {
                        if (enableSeekDebugLog)
                        {
                            ThumbnailRuntimeLog.Write(
                                "autogen-seek",
                                $"read frame end: movie='{movieFullPath}' requested_sec={sec:0.###} mode={seekMode} ret={readRet} err='{GetErrorMessage(readRet)}' read_packets={readPacketCount}"
                            );
                        }
                        break;
                    }

                    cts.ThrowIfCancellationRequested();
                    readPacketCount++;
                    try
                    {
                        if (pPacket->stream_index != pStream->index)
                        {
                            skippedPacketCount++;
                            continue;
                        }

                        if (enableSeekDebugLog && readPacketCount <= 6)
                        {
                            ThumbnailRuntimeLog.Write(
                                "autogen-seek",
                                $"packet read: movie='{movieFullPath}' requested_sec={sec:0.###} mode={seekMode} index={readPacketCount} pts={pPacket->pts} dts={pPacket->dts} duration={pPacket->duration} size={pPacket->size} flags={pPacket->flags}"
                            );
                        }

                        int ret;
                        bool packetQueued = false;
                        while (!packetQueued && !streamEnded)
                        {
                            ret = ffmpeg.avcodec_send_packet(pCodecContext, pPacket);
                            if (ret == 0)
                            {
                                sentPacketCount++;
                                if (enableSeekDebugLog && sentPacketCount <= 6)
                                {
                                    ThumbnailRuntimeLog.Write(
                                        "autogen-seek",
                                        $"send packet ok: movie='{movieFullPath}' requested_sec={sec:0.###} mode={seekMode} sent_packets={sentPacketCount}"
                                    );
                                }
                                packetQueued = true;
                                break;
                            }

                            if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                            {
                                bool drained =
                                    TryDrainDecoderFrames(
                                        movieFullPath,
                                        sec,
                                        timeBaseSec,
                                        pCodecContext,
                                        pFrame,
                                        ref pSwsContext,
                                        targetWidth,
                                        targetHeight,
                                        enableSeekDebugLog,
                                        preferClosestNonBlackThenLatestBright,
                                        ref latestBrightFallbackBitmap,
                                        ref latestBrightFallbackPtsSec,
                                        ref streamEnded,
                                        ref frameCaptured,
                                        ref receiveFrameCount,
                                        out capturedBitmap
                                    );
                                if (!drained)
                                {
                                    if (enableSeekDebugLog)
                                    {
                                        ThumbnailRuntimeLog.Write(
                                            "autogen-seek",
                                            $"send packet eagain-no-drain: movie='{movieFullPath}' requested_sec={sec:0.###} mode={seekMode} sent_packets={sentPacketCount} received_frames={receiveFrameCount}"
                                        );
                                    }
                                    break;
                                }

                                if (capturedBitmap != null)
                                {
                                    latestBrightFallbackBitmap?.Dispose();
                                    return true;
                                }

                                continue;
                            }

                            if (ret == ffmpeg.AVERROR_EOF)
                            {
                                streamEnded = true;
                                break;
                            }

                            if (enableSeekDebugLog)
                            {
                                ThumbnailRuntimeLog.Write(
                                    "autogen-seek",
                                    $"send packet skipped: movie='{movieFullPath}' requested_sec={sec:0.###} mode={seekMode} ret={ret} err='{GetErrorMessage(ret)}'"
                                );
                            }
                            break;
                        }

                        if (!packetQueued || frameCaptured || streamEnded)
                        {
                            continue;
                        }

                        while (true)
                        {
                            if (
                                !TryDrainDecoderFrames(
                                    movieFullPath,
                                    sec,
                                    timeBaseSec,
                                    pCodecContext,
                                    pFrame,
                                    ref pSwsContext,
                                    targetWidth,
                                    targetHeight,
                                    enableSeekDebugLog,
                                    preferClosestNonBlackThenLatestBright,
                                    ref latestBrightFallbackBitmap,
                                    ref latestBrightFallbackPtsSec,
                                    ref streamEnded,
                                    ref frameCaptured,
                                    ref receiveFrameCount,
                                    out capturedBitmap
                                )
                            )
                            {
                                break;
                            }

                            if (capturedBitmap != null || streamEnded)
                            {
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

                if (!frameCaptured && !streamEnded && sentPacketCount > 0)
                {
                    int flushRet = ffmpeg.avcodec_send_packet(pCodecContext, null);
                    if (enableSeekDebugLog)
                    {
                        ThumbnailRuntimeLog.Write(
                            "autogen-seek",
                            $"send flush packet: movie='{movieFullPath}' requested_sec={sec:0.###} mode={seekMode} ret={flushRet} err='{GetErrorMessage(flushRet)}' sent_packets={sentPacketCount}"
                        );
                    }

                    if (
                        flushRet == 0
                        || flushRet == ffmpeg.AVERROR(ffmpeg.EAGAIN)
                        || flushRet == ffmpeg.AVERROR_EOF
                    )
                    {
                        while (
                            TryDrainDecoderFrames(
                                movieFullPath,
                                sec,
                                timeBaseSec,
                                pCodecContext,
                                pFrame,
                                ref pSwsContext,
                                targetWidth,
                                targetHeight,
                                enableSeekDebugLog,
                                preferClosestNonBlackThenLatestBright,
                                ref latestBrightFallbackBitmap,
                                ref latestBrightFallbackPtsSec,
                                ref streamEnded,
                                ref frameCaptured,
                                ref receiveFrameCount,
                                out capturedBitmap
                            )
                        )
                        {
                            if (capturedBitmap != null || streamEnded)
                            {
                                break;
                            }
                        }

                        if (capturedBitmap != null)
                        {
                            latestBrightFallbackBitmap?.Dispose();
                            return true;
                        }
                    }
                }

                if (
                    TryPromoteLatestBrightFallback(
                        movieFullPath,
                        sec,
                        enableSeekDebugLog,
                        ref latestBrightFallbackBitmap,
                        ref latestBrightFallbackPtsSec,
                        ref frameCaptured,
                        out capturedBitmap
                    )
                )
                {
                    return true;
                }

                if (enableSeekDebugLog)
                {
                    ThumbnailRuntimeLog.Write(
                        "autogen-seek",
                        $"seek miss: movie='{movieFullPath}' requested_sec={sec:0.###} mode={seekMode} read_packets={readPacketCount} skipped_packets={skippedPacketCount} sent_packets={sentPacketCount} received_frames={receiveFrameCount} stream_ended={streamEnded}"
                    );
                }

                if (frameCaptured)
                {
                    latestBrightFallbackBitmap?.Dispose();
                    return true;
                }

                ffmpeg.av_frame_unref(pFrame);

                // stream seek が成功扱いでも packet を1枚も返さず EOF になる MP4 があるため、
                // その時だけコンテナ全体の seek へ切り替えて再試行する。
                if (!useContainerSeek && readPacketCount == 0)
                {
                    if (enableSeekDebugLog)
                    {
                        ThumbnailRuntimeLog.Write(
                            "autogen-seek",
                            $"seek retry switch: movie='{movieFullPath}' requested_sec={sec:0.###} from=stream to=container"
                        );
                    }
                    zeroPacketSeekMissObserved = true;
                    continue;
                }

                if (readPacketCount == 0)
                {
                    zeroPacketSeekMissObserved = true;
                }
                break;
            }

            if (zeroPacketSeekMissObserved)
            {
                if (enableSeekDebugLog)
                {
                    ThumbnailRuntimeLog.Write(
                        "autogen-seek",
                        $"sequential fallback requested: movie='{movieFullPath}' requested_sec={sec:0.###}"
                    );
                }

                latestBrightFallbackBitmap?.Dispose();
                return TryCaptureFrameBySequentialReadFromFreshContext(
                    movieFullPath,
                    sec,
                    targetWidth,
                    targetHeight,
                    enableSeekDebugLog,
                    preferClosestNonBlackThenLatestBright,
                    cts,
                    ref observedDemuxImmediateEof,
                    out capturedBitmap
                );
            }

            latestBrightFallbackBitmap?.Dispose();
            return false;
        }

        // seek 後に demuxer が packet を1件も返さない動画だけ、fresh context で先頭から順送り decode を試す。
        private unsafe static bool TryCaptureFrameBySequentialReadFromFreshContext(
            string movieFullPath,
            double requestedSec,
            int targetWidth,
            int targetHeight,
            bool enableSeekDebugLog,
            bool preferClosestNonBlackThenLatestBright,
            CancellationToken cts,
            ref bool observedDemuxImmediateEof,
            out Bitmap capturedBitmap
        )
        {
            capturedBitmap = null;
            Bitmap latestBrightFallbackBitmap = null;
            double latestBrightFallbackPtsSec = -1d;

            AVFormatContext* pFormatContext = null;
            AVCodecContext* pCodecContext = null;
            AVPacket* pPacket = null;
            AVFrame* pFrame = null;
            SwsContext* pSwsContext = null;
            AVDictionary* pFormatOptions = null;

            try
            {
                pFormatContext = ffmpeg.avformat_alloc_context();
                ApplyRobustInputProbeOptions(&pFormatOptions);
                int openRet = ffmpeg.avformat_open_input(
                    &pFormatContext,
                    movieFullPath,
                    null,
                    &pFormatOptions
                );
                if (openRet < 0)
                {
                    if (enableSeekDebugLog)
                    {
                        ThumbnailRuntimeLog.Write(
                            "autogen-seek",
                            $"sequential fallback open failed: movie='{movieFullPath}' ret={openRet} err='{GetErrorMessage(openRet)}'"
                        );
                    }
                    return false;
                }

                int streamInfoRet = ffmpeg.avformat_find_stream_info(pFormatContext, null);
                if (streamInfoRet < 0)
                {
                    if (enableSeekDebugLog)
                    {
                        ThumbnailRuntimeLog.Write(
                            "autogen-seek",
                            $"sequential fallback stream-info failed: movie='{movieFullPath}' ret={streamInfoRet} err='{GetErrorMessage(streamInfoRet)}'"
                        );
                    }
                    return false;
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
                {
                    return false;
                }

                var pCodec = ffmpeg.avcodec_find_decoder(pStream->codecpar->codec_id);
                if (pCodec == null)
                {
                    return false;
                }

                pCodecContext = ffmpeg.avcodec_alloc_context3(pCodec);
                ffmpeg.avcodec_parameters_to_context(pCodecContext, pStream->codecpar);
                int codecOpenRet = ffmpeg.avcodec_open2(pCodecContext, pCodec, null);
                if (codecOpenRet < 0)
                {
                    if (enableSeekDebugLog)
                    {
                        ThumbnailRuntimeLog.Write(
                            "autogen-seek",
                            $"sequential fallback codec-open failed: movie='{movieFullPath}' ret={codecOpenRet} err='{GetErrorMessage(codecOpenRet)}'"
                        );
                    }
                    return false;
                }

                // stream_info で末尾まで読まれた入力は、そのままだと順送り decode が即 EOF になるため先頭へ戻す。
                int rewindRet = ffmpeg.avformat_seek_file(
                    pFormatContext,
                    -1,
                    long.MinValue,
                    0,
                    long.MaxValue,
                    ffmpeg.AVSEEK_FLAG_BACKWARD
                );
                if (rewindRet < 0)
                {
                    rewindRet = ffmpeg.av_seek_frame(
                        pFormatContext,
                        pStream->index,
                        0,
                        ffmpeg.AVSEEK_FLAG_BACKWARD
                    );
                }
                if (enableSeekDebugLog)
                {
                    ThumbnailRuntimeLog.Write(
                        "autogen-seek",
                        $"sequential fallback rewind: movie='{movieFullPath}' ret={rewindRet} err='{GetErrorMessage(rewindRet)}'"
                    );
                }
                if (rewindRet >= 0)
                {
                    ffmpeg.avformat_flush(pFormatContext);
                    ffmpeg.avcodec_flush_buffers(pCodecContext);
                }

                pPacket = ffmpeg.av_packet_alloc();
                pFrame = ffmpeg.av_frame_alloc();

                double timeBaseSec = ffmpeg.av_q2d(pStream->time_base);
                bool streamEnded = false;
                bool frameCaptured = false;
                int readPacketCount = 0;
                int receiveFrameCount = 0;
                int sentPacketCount = 0;

                while (!frameCaptured && !streamEnded)
                {
                    int readRet = ffmpeg.av_read_frame(pFormatContext, pPacket);
                    if (readRet < 0)
                    {
                        if (enableSeekDebugLog)
                        {
                            ThumbnailRuntimeLog.Write(
                                "autogen-seek",
                                $"sequential fallback read end: movie='{movieFullPath}' requested_sec={requestedSec:0.###} ret={readRet} err='{GetErrorMessage(readRet)}' read_packets={readPacketCount}"
                            );
                        }
                        break;
                    }

                    cts.ThrowIfCancellationRequested();
                    readPacketCount++;
                    try
                    {
                        if (pPacket->stream_index != pStream->index)
                        {
                            continue;
                        }

                        int sendRet = ffmpeg.avcodec_send_packet(pCodecContext, pPacket);
                        if (sendRet == 0)
                        {
                            sentPacketCount++;
                        }
                        else if (sendRet != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                        {
                            if (enableSeekDebugLog)
                            {
                                ThumbnailRuntimeLog.Write(
                                    "autogen-seek",
                                    $"sequential fallback send failed: movie='{movieFullPath}' requested_sec={requestedSec:0.###} ret={sendRet} err='{GetErrorMessage(sendRet)}'"
                                );
                            }
                            continue;
                        }

                        while (
                            TryDrainDecoderFrames(
                                movieFullPath,
                                requestedSec,
                                timeBaseSec,
                                pCodecContext,
                                pFrame,
                                ref pSwsContext,
                                targetWidth,
                                targetHeight,
                                enableSeekDebugLog,
                                preferClosestNonBlackThenLatestBright,
                                ref latestBrightFallbackBitmap,
                                ref latestBrightFallbackPtsSec,
                                ref streamEnded,
                                ref frameCaptured,
                                ref receiveFrameCount,
                                out capturedBitmap
                            )
                        )
                        {
                            if (capturedBitmap != null || streamEnded)
                            {
                                break;
                            }
                        }

                        if (capturedBitmap != null)
                        {
                            if (enableSeekDebugLog)
                            {
                                ThumbnailRuntimeLog.Write(
                                    "autogen-seek",
                                    $"sequential fallback hit: movie='{movieFullPath}' requested_sec={requestedSec:0.###} read_packets={readPacketCount}"
                                );
                            }
                            return true;
                        }
                    }
                    finally
                    {
                        ffmpeg.av_packet_unref(pPacket);
                    }
                }

                if (!frameCaptured && sentPacketCount > 0)
                {
                    int flushRet = ffmpeg.avcodec_send_packet(pCodecContext, null);
                    if (
                        flushRet == 0
                        || flushRet == ffmpeg.AVERROR(ffmpeg.EAGAIN)
                        || flushRet == ffmpeg.AVERROR_EOF
                    )
                    {
                        while (
                            TryDrainDecoderFrames(
                                movieFullPath,
                                requestedSec,
                                timeBaseSec,
                                pCodecContext,
                                pFrame,
                                ref pSwsContext,
                                targetWidth,
                                targetHeight,
                                enableSeekDebugLog,
                                preferClosestNonBlackThenLatestBright,
                                ref latestBrightFallbackBitmap,
                                ref latestBrightFallbackPtsSec,
                                ref streamEnded,
                                ref frameCaptured,
                                ref receiveFrameCount,
                                out capturedBitmap
                            )
                        )
                        {
                            if (capturedBitmap != null || streamEnded)
                            {
                                break;
                            }
                        }
                    }
                }

                if (capturedBitmap != null)
                {
                    if (enableSeekDebugLog)
                    {
                        ThumbnailRuntimeLog.Write(
                            "autogen-seek",
                            $"sequential fallback hit-after-flush: movie='{movieFullPath}' requested_sec={requestedSec:0.###}"
                        );
                    }
                    return true;
                }

                if (
                    TryPromoteLatestBrightFallback(
                        movieFullPath,
                        requestedSec,
                        enableSeekDebugLog,
                        ref latestBrightFallbackBitmap,
                        ref latestBrightFallbackPtsSec,
                        ref frameCaptured,
                        out capturedBitmap
                    )
                )
                {
                    return true;
                }

                if (enableSeekDebugLog)
                {
                    ThumbnailRuntimeLog.Write(
                        "autogen-seek",
                        $"sequential fallback miss: movie='{movieFullPath}' requested_sec={requestedSec:0.###} read_packets={readPacketCount} sent_packets={sentPacketCount} received_frames={receiveFrameCount}"
                    );
                }
                if (readPacketCount == 0)
                {
                    // fresh open 後でも1packetも取れない時だけ、demux即EOFとして上位へ返す。
                    observedDemuxImmediateEof = true;
                }
                return false;
            }
            finally
            {
                latestBrightFallbackBitmap?.Dispose();
                if (pPacket != null)
                {
                    ffmpeg.av_packet_free(&pPacket);
                }
                if (pFrame != null)
                {
                    ffmpeg.av_frame_free(&pFrame);
                }
                if (pCodecContext != null)
                {
                    ffmpeg.avcodec_free_context(&pCodecContext);
                }
                if (pFormatContext != null)
                {
                    ffmpeg.avformat_close_input(&pFormatContext);
                }
                if (pSwsContext != null)
                {
                    ffmpeg.sws_freeContext(pSwsContext);
                }
                if (pFormatOptions != null)
                {
                    ffmpeg.av_dict_free(&pFormatOptions);
                }
            }
        }

        private unsafe static bool TryDrainDecoderFrames(
            string movieFullPath,
            double requestedSec,
            double timeBaseSec,
            AVCodecContext* pCodecContext,
            AVFrame* pFrame,
            ref SwsContext* pSwsContext,
            int targetWidth,
            int targetHeight,
            bool enableSeekDebugLog,
            bool preferClosestNonBlackThenLatestBright,
            ref Bitmap latestBrightFallbackBitmap,
            ref double latestBrightFallbackPtsSec,
            ref bool streamEnded,
            ref bool frameCaptured,
            ref int receiveFrameCount,
            out Bitmap capturedBitmap
        )
        {
            capturedBitmap = null;

            while (true)
            {
                int ret = ffmpeg.avcodec_receive_frame(pCodecContext, pFrame);
                if (ret == 0)
                {
                    receiveFrameCount++;
                    double decodedPtsSec =
                        pFrame->best_effort_timestamp != ffmpeg.AV_NOPTS_VALUE
                            ? pFrame->best_effort_timestamp * timeBaseSec
                            : -1;

                    if (!preferClosestNonBlackThenLatestBright)
                    {
                        if (
                            !ShouldAcceptDecodedFrameAtRequestedSecond(
                                requestedSec,
                                decodedPtsSec
                            )
                        )
                        {
                            if (enableSeekDebugLog)
                            {
                                ThumbnailRuntimeLog.Write(
                                    "autogen-seek",
                                    $"seek frame skipped: movie='{movieFullPath}' requested_sec={requestedSec:0.###} decoded_pts_sec={decodedPtsSec:0.###} received_frames={receiveFrameCount}"
                                );
                            }
                            ffmpeg.av_frame_unref(pFrame);
                            continue;
                        }

                        Bitmap bmp = ConvertFrameToBitmap(
                            pFrame,
                            ref pSwsContext,
                            targetWidth,
                            targetHeight
                        );
                        if (bmp != null)
                        {
                            capturedBitmap = bmp;
                            frameCaptured = true;
                            if (enableSeekDebugLog)
                            {
                                ThumbnailRuntimeLog.Write(
                                    "autogen-seek",
                                    $"seek hit: movie='{movieFullPath}' requested_sec={requestedSec:0.###} decoded_pts_sec={decodedPtsSec:0.###} received_frames={receiveFrameCount}"
                                );
                            }
                        }
                        else if (enableSeekDebugLog)
                        {
                            ThumbnailRuntimeLog.Write(
                                "autogen-seek",
                                $"seek frame converted null: movie='{movieFullPath}' requested_sec={requestedSec:0.###} received_frames={receiveFrameCount}"
                            );
                        }
                        ffmpeg.av_frame_unref(pFrame);
                        return true;
                    }

                    Bitmap candidateBitmap = ConvertFrameToBitmap(
                        pFrame,
                        ref pSwsContext,
                        targetWidth,
                        targetHeight
                    );
                    if (candidateBitmap == null)
                    {
                        if (enableSeekDebugLog)
                        {
                            ThumbnailRuntimeLog.Write(
                                "autogen-seek",
                                $"seek frame converted null: movie='{movieFullPath}' requested_sec={requestedSec:0.###} received_frames={receiveFrameCount}"
                            );
                        }
                        ffmpeg.av_frame_unref(pFrame);
                        continue;
                    }

                    bool isMostlyBlack =
                        FfmpegOnePassThumbnailGenerationEngine.IsMostlyBlackPanel(candidateBitmap);
                    DecodedFrameSelectionDecision selectionDecision =
                        ResolveDecodedFrameSelectionDecision(
                            requestedSec,
                            decodedPtsSec,
                            isMostlyBlack,
                            preferClosestNonBlackThenLatestBright
                        );
                    ffmpeg.av_frame_unref(pFrame);
                    if (selectionDecision == DecodedFrameSelectionDecision.AcceptImmediately)
                    {
                        capturedBitmap = candidateBitmap;
                        frameCaptured = true;
                        if (enableSeekDebugLog)
                        {
                            ThumbnailRuntimeLog.Write(
                                "autogen-seek",
                                $"seek hit nonblack: movie='{movieFullPath}' requested_sec={requestedSec:0.###} decoded_pts_sec={decodedPtsSec:0.###} received_frames={receiveFrameCount}"
                            );
                        }
                        return true;
                    }

                    if (
                        selectionDecision
                        == DecodedFrameSelectionDecision.KeepAsLatestBrightFallback
                    )
                    {
                        latestBrightFallbackBitmap?.Dispose();
                        latestBrightFallbackBitmap = candidateBitmap;
                        latestBrightFallbackPtsSec = decodedPtsSec;
                        if (enableSeekDebugLog)
                        {
                            ThumbnailRuntimeLog.Write(
                                "autogen-seek",
                                $"seek bright fallback updated: movie='{movieFullPath}' requested_sec={requestedSec:0.###} decoded_pts_sec={decodedPtsSec:0.###} received_frames={receiveFrameCount}"
                            );
                        }
                        continue;
                    }

                    candidateBitmap.Dispose();
                    if (enableSeekDebugLog)
                    {
                        ThumbnailRuntimeLog.Write(
                            "autogen-seek",
                            $"seek frame ignored: movie='{movieFullPath}' requested_sec={requestedSec:0.###} decoded_pts_sec={decodedPtsSec:0.###} black={isMostlyBlack} received_frames={receiveFrameCount}"
                        );
                    }
                    continue;
                }

                if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                {
                    if (enableSeekDebugLog)
                    {
                        ThumbnailRuntimeLog.Write(
                            "autogen-seek",
                            $"receive frame eagain: movie='{movieFullPath}' requested_sec={requestedSec:0.###} received_frames={receiveFrameCount}"
                        );
                    }
                    return false;
                }

                if (ret == ffmpeg.AVERROR_EOF)
                {
                    if (enableSeekDebugLog)
                    {
                        ThumbnailRuntimeLog.Write(
                            "autogen-seek",
                            $"receive frame eof: movie='{movieFullPath}' requested_sec={requestedSec:0.###} received_frames={receiveFrameCount}"
                        );
                    }
                    streamEnded = true;
                    return true;
                }

                if (enableSeekDebugLog)
                {
                    ThumbnailRuntimeLog.Write(
                        "autogen-seek",
                        $"receive frame error: movie='{movieFullPath}' requested_sec={requestedSec:0.###} ret={ret} err='{GetErrorMessage(ret)}' received_frames={receiveFrameCount}"
                    );
                }

                return true;
            }
        }

        // 近傍が全滅した時だけ、最後に取れた見えるコマを救済採用する。
        private static bool TryPromoteLatestBrightFallback(
            string movieFullPath,
            double requestedSec,
            bool enableSeekDebugLog,
            ref Bitmap latestBrightFallbackBitmap,
            ref double latestBrightFallbackPtsSec,
            ref bool frameCaptured,
            out Bitmap capturedBitmap
        )
        {
            capturedBitmap = null;
            if (latestBrightFallbackBitmap == null)
            {
                return false;
            }

            capturedBitmap = latestBrightFallbackBitmap;
            latestBrightFallbackBitmap = null;
            frameCaptured = true;
            if (enableSeekDebugLog)
            {
                ThumbnailRuntimeLog.Write(
                    "autogen-seek",
                    $"seek latest bright fallback hit: movie='{movieFullPath}' requested_sec={requestedSec:0.###} decoded_pts_sec={latestBrightFallbackPtsSec:0.###}"
                );
            }
            latestBrightFallbackPtsSec = -1d;
            return true;
        }

        // 通常の代表秒が全部外れた時だけ、ヘッダー直後相当の先頭付近を浅く探る。
        private unsafe static Bitmap TryCaptureNearHeaderFallbackFrame(
            string movieFullPath,
            double? durationSec,
            AVFormatContext* pFormatContext,
            AVStream* pStream,
            AVCodecContext* pCodecContext,
            AVPacket* pPacket,
            AVFrame* pFrame,
            ref SwsContext* pSwsContext,
            int targetWidth,
            int targetHeight,
            bool enableSeekDebugLog,
            bool preferClosestNonBlackThenLatestBright,
            CancellationToken cts,
            ref bool observedDemuxImmediateEof,
            out double capturedSec
        )
        {
            capturedSec = 0;
            List<double> candidateSecs = BuildHeaderFallbackCandidateSeconds(durationSec);
            ThumbnailRuntimeLog.Write(
                "autogen-header-frame-fallback",
                $"fallback candidates: movie='{movieFullPath}' duration_sec={durationSec:0.###} candidates=[{FormatCandidateSeconds(candidateSecs)}]"
            );
            foreach (double sec in candidateSecs)
            {
                ThumbnailRuntimeLog.Write(
                    "autogen-header-frame-fallback",
                    $"fallback try: movie='{movieFullPath}' sec={sec:0.###}"
                );
                if (
                    TryCaptureFrameAtSecond(
                        movieFullPath,
                        sec,
                        pFormatContext,
                        pStream,
                        pCodecContext,
                        pPacket,
                        pFrame,
                        ref pSwsContext,
                        targetWidth,
                        targetHeight,
                        enableSeekDebugLog,
                        preferClosestNonBlackThenLatestBright,
                        cts,
                        ref observedDemuxImmediateEof,
                        out Bitmap capturedBitmap
                    )
                )
                {
                    ThumbnailRuntimeLog.Write(
                        "autogen-header-frame-fallback",
                        $"fallback hit: movie='{movieFullPath}' sec={sec:0.###}"
                    );
                    capturedSec = sec;
                    return capturedBitmap;
                }
            }

            ThumbnailRuntimeLog.Write(
                "autogen-header-frame-fallback",
                $"fallback exhausted: movie='{movieFullPath}' candidates=[{FormatCandidateSeconds(candidateSecs)}]"
            );
            return null;
        }

        private static string BuildNoFramesDecodedMessage(bool observedDemuxImmediateEof)
        {
            return observedDemuxImmediateEof
                ? $"No frames decoded ({DemuxImmediateEofErrorDetail})"
                : "No frames decoded";
        }

        // 先頭の黒味やロード直後も考慮して、少しずつ後ろへずらした候補を作る。
        // テストからも同じ候補列を検証できるよう internal にしている。
        internal static List<double> BuildHeaderFallbackCandidateSeconds(double? durationSec)
        {
            double[] baseCandidates = [0d, 0.1d, 0.25d, 0.5d, 1d, 2d];
            double maxSec = durationSec.HasValue && durationSec.Value > 0
                ? Math.Max(0d, durationSec.Value - 0.001d)
                : 2d;
            HashSet<long> seen = [];
            List<double> result = [];
            foreach (double candidate in baseCandidates)
            {
                double normalized = Math.Max(0d, Math.Min(candidate, maxSec));
                long key = (long)Math.Round(normalized * 1000d);
                if (seen.Add(key))
                {
                    result.Add(normalized);
                }
            }

            return result;
        }

        internal static bool ShouldUseShortClipFirstFrameSeekFallback(
            double? durationSec,
            int panelCount
        )
        {
            return durationSec.HasValue
                && durationSec.Value > 0
                && durationSec.Value <= ShortClipFirstFrameSeekFallbackMaxDurationSec
                && panelCount > 0
                && panelCount <= 5;
        }

        internal static IReadOnlyList<double> BuildShortClipFirstFrameSeekCandidates(
            double? durationSec,
            double originalStartSec
        )
        {
            if (!durationSec.HasValue || durationSec.Value <= 0)
            {
                return [];
            }

            List<double> rawCandidates = [0.001d, 0.01d, 0.016d, 0.033d];
            SortedDictionary<string, double> normalized = new(StringComparer.Ordinal);
            foreach (double rawCandidate in rawCandidates)
            {
                double? clamped = ClampShortClipFirstFrameSeekCandidate(
                    rawCandidate,
                    durationSec.Value
                );
                if (!clamped.HasValue)
                {
                    continue;
                }

                if (Math.Abs(clamped.Value - originalStartSec) < 0.0005d)
                {
                    continue;
                }

                string key = clamped.Value.ToString("0.###", CultureInfo.InvariantCulture);
                normalized[key] = clamped.Value;
            }

            return normalized.Values.ToList();
        }

        private static double? ClampShortClipFirstFrameSeekCandidate(double candidate, double durationSec)
        {
            if (candidate < 0)
            {
                return null;
            }

            double maxSeek = Math.Max(0d, durationSec - 0.001d);
            if (maxSeek <= 0)
            {
                return 0d;
            }

            if (candidate > maxSeek)
            {
                return null;
            }

            return candidate;
        }

        private static string FormatCandidateSeconds(IReadOnlyList<double> candidateSecs)
        {
            if (candidateSecs == null || candidateSecs.Count < 1)
            {
                return "";
            }

            List<string> parts = [];
            foreach (double sec in candidateSecs)
            {
                parts.Add(sec.ToString("0.###"));
            }
            return string.Join(",", parts);
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
            AVDictionary* pFormatOptions = null;

            try
            {
                pFormatContext = ffmpeg.avformat_alloc_context();
                ApplyRobustInputProbeOptions(&pFormatOptions);
                if (
                    ffmpeg.avformat_open_input(
                        &pFormatContext,
                        movieFullPath,
                        null,
                        &pFormatOptions
                    ) < 0
                )
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

                        int sendRet;
                        bool packetQueued = false;
                        while (!packetQueued && !streamEnded)
                        {
                            sendRet = ffmpeg.avcodec_send_packet(pCodecContext, pPacket);
                            if (sendRet == 0)
                            {
                                packetQueued = true;
                                break;
                            }

                            if (sendRet == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                            {
                                int drainRet = ffmpeg.avcodec_receive_frame(pCodecContext, pFrame);
                                if (drainRet == 0)
                                {
                                    extracted = ConvertFrameToBitmap(
                                        pFrame,
                                        ref pSwsContext,
                                        targetWidth,
                                        targetHeight
                                    );
                                    ffmpeg.av_frame_unref(pFrame);
                                    break;
                                }

                                if (drainRet == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                                {
                                    break;
                                }

                                if (drainRet == ffmpeg.AVERROR_EOF)
                                {
                                    streamEnded = true;
                                    break;
                                }

                                break;
                            }

                            if (sendRet == ffmpeg.AVERROR_EOF)
                            {
                                streamEnded = true;
                            }
                            break;
                        }

                        if (!packetQueued || extracted != null || streamEnded)
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
                                    ref pSwsContext,
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
                if (pFormatOptions != null)
                    ffmpeg.av_dict_free(&pFormatOptions);
            }
        }

        private static unsafe void ApplyRobustInputProbeOptions(AVDictionary** options)
        {
            if (options == null)
            {
                return;
            }

            ffmpeg.av_dict_set(
                options,
                "probesize",
                RobustProbeSizeBytes.ToString(CultureInfo.InvariantCulture),
                0
            );
            ffmpeg.av_dict_set(
                options,
                "analyzeduration",
                RobustAnalyzeDurationUsec.ToString(CultureInfo.InvariantCulture),
                0
            );
        }

        internal static bool ShouldUseRobustProbeOptions(string movieFullPath, double? durationSec)
        {
            return true;
        }

        private static unsafe Bitmap ConvertFrameToBitmap(
            AVFrame* pFrame,
            ref SwsContext* pSwsContext,
            int width,
            int height
        )
        {
            if (
                pFrame == null
                || pFrame->width <= 0
                || pFrame->height <= 0
                || pFrame->format == (int)AVPixelFormat.AV_PIX_FMT_NONE
            )
            {
                return null;
            }

            if (pSwsContext != null)
            {
                ffmpeg.sws_freeContext(pSwsContext);
                pSwsContext = null;
            }

            pSwsContext = ffmpeg.sws_getContext(
                pFrame->width,
                pFrame->height,
                (AVPixelFormat)pFrame->format,
                width,
                height,
                AVPixelFormat.AV_PIX_FMT_BGR24,
                1,
                null,
                null,
                null
            );
            if (pSwsContext == null)
            {
                return null;
            }

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
