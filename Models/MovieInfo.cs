using System.IO;
using System.Runtime.InteropServices;
using FFMediaToolkit;
using FFMediaToolkit.Decoding;
using FFmpeg.AutoGen;
using IndigoMovieManager.Thumbnail;
using OpenCvSharp;

namespace IndigoMovieManager
{
    /// <summary>
    /// 動画ファイルに直接突撃してメタ情報（FPS・尺・サイズ・ハッシュ等）をもぎ取ってくる「生のデータ」の入れ物だ！📦
    /// （※DBから読むんじゃなく、ローカルファイルから這いずり回って集めた新鮮な情報の器だぜ！）
    /// </summary>
    public class MovieInfo : MovieCore
    {
        private const double DefaultFps = 30;
        private const double DurationMismatchRatioThreshold = 2.0;
        private const double DurationMismatchAbsoluteThresholdSec = 5.0;
        private static readonly object FfmpegLoadSync = new();
        private static readonly object AutoGenLoadSync = new();
        private static bool ffmpegLoadAttempted;
        private static bool ffmpegLoadSucceeded;
        private static bool autoGenLoadAttempted;
        private static bool autoGenLoadSucceeded;
        private static string autoGenLoadFailureReason = "";

        /// <summary>
        /// 旧実装リスペクト！既存コードが「Tag」名で呼んでるところへの救済エイリアス（別名）だ！🤝
        /// </summary>
        public string Tag => Tags;

        /// <summary>
        /// 誕生の瞬間！指定された動画ファイルを徹底解剖し、基本情報を自ら（MovieCore）の血肉に変えるぜ！🧬
        /// </summary>
        /// <param name="fileFullPath">解析対象のファイルフルパス</param>
        /// <param name="noHash">ハッシュ計算を省略するか。重い処理を飛ばしたい場合（Bookmark登録等）は true にする</param>
        public MovieInfo(string fileFullPath, bool noHash = false)
        {
            // 1. パスの保持
            // 生パスと正規化パスを両方保持する。
            // 生パス: DB保存やUI表示、Queueへの引渡しなど、システム内で標準的に扱う元の値。
            // 正規化: OpenCV等の外部ライブラリへ処理を依頼する際に渡す、表記ゆれをなくした値。
            string rawPath = fileFullPath ?? "";
            string normalizedPath = NormalizeMoviePath(fileFullPath);

            // 2. 動画の長さを取得する（FFMediaToolkit優先、失敗時のみOpenCV）
            // ベンチで検証した取得式（AvgFrameRate / NumberOfFrames / Duration）をそのまま流用する。
            double fps = DefaultFps;
            double totalFrames = 0;
            double durationSec = 0;
            string videoCodec = "";
            bool readByFfMediaToolkit =
                EnsureFfMediaToolkitLoaded()
                && TryReadByFfMediaToolkit(
                    rawPath,
                    out fps,
                    out totalFrames,
                    out durationSec,
                    out videoCodec
                );
            bool readByOpenCv = false;
            if (!readByFfMediaToolkit)
            {
                readByOpenCv = TryReadByOpenCv(
                    normalizedPath,
                    out fps,
                    out totalFrames,
                    out durationSec
                );
            }

            if (
                ShouldTryReadByAutoGen(rawPath, readByFfMediaToolkit, readByOpenCv, durationSec)
                && EnsureAutoGenLoadedSafe(out _)
                && TryReadByAutoGen(
                    rawPath,
                    out double autoGenFps,
                    out double autoGenTotalFrames,
                    out double autoGenDurationSec,
                    out string autoGenVideoCodec,
                    out string autoGenDetail
                )
            )
            {
                ApplyAutoGenProbeResult(
                    rawPath,
                    ref fps,
                    ref totalFrames,
                    ref durationSec,
                    ref videoCodec,
                    autoGenFps,
                    autoGenTotalFrames,
                    autoGenDurationSec,
                    autoGenVideoCodec,
                    autoGenDetail
                );
            }

            FPS = NormalizeFps(fps);
            TotalFrames = NormalizeTotalFrames(totalFrames);
            MovieLength = (long)
                NormalizeDurationSec(rawPath, durationSec, TotalFrames, FPS, "movieinfo-final");

            // 3. ファイルシステムの属性（ファイルサイズ、更新日時など）を取得
            FileInfo file = new(rawPath);

            var now = DateTime.Now;
            // 現在時刻から「秒以下の端数（ミリ秒など）」を切り捨ててDB格納用に調整
            var result = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
            LastDate = result;
            RegistDate = result;

            VideoCodec = videoCodec;

            // 4. ベースクラス(MovieCore)の各プロパティへ抽出・計算したメタ情報を流し込む
            MovieName = Path.GetFileNameWithoutExtension(rawPath);
            MoviePath = rawPath;
            MovieSize = file.Length;

            // 5. ハッシュ値の計算
            // ハッシュ計算は巨大ファイル相手だと時間がかかるため、noHashオプションでスキップできる設計。
            if (!noHash)
            {
                Hash = Tools.GetHashCRC32(rawPath);
            }

            // 万一のためにファイルの更新日時も、秒以下の端数を切り捨てて格納しておく
            var lastWrite = file.LastWriteTime;
            result = lastWrite.AddTicks(-(lastWrite.Ticks % TimeSpan.TicksPerSecond));
            FileDate = result;
        }

        /// <summary>
        /// 指定動画を FFMediaToolkit / OpenCV / autogen の3経路で読み比べる。
        /// 調査時はこの結果を見れば、どの経路が壊れた尺を返しているかを切り分けられる。
        /// </summary>
        public static MovieInfoMetadataProbeSet ProbeMetadataSources(
            string fileFullPath,
            bool writeDebugLog = true
        )
        {
            string rawPath = fileFullPath ?? "";
            string normalizedPath = NormalizeMoviePath(fileFullPath);

            MovieInfoMetadataProbeResult ffMediaToolkitProbe = BuildProbeResult(
                "ffmediatoolkit",
                () =>
                {
                    double fps = 0;
                    double totalFrames = 0;
                    double durationSec = 0;
                    string videoCodec = "";
                    bool success =
                        EnsureFfMediaToolkitLoaded()
                        && TryReadByFfMediaToolkit(
                            rawPath,
                            out fps,
                            out totalFrames,
                            out durationSec,
                            out videoCodec
                        );
                    return new MovieInfoMetadataProbeResult(
                        "ffmediatoolkit",
                        success,
                        fps,
                        totalFrames,
                        durationSec,
                        videoCodec,
                        success ? "ok" : "read_failed"
                    );
                }
            );
            MovieInfoMetadataProbeResult openCvProbe = BuildProbeResult(
                "opencv",
                () =>
                {
                    double fps = 0;
                    double totalFrames = 0;
                    double durationSec = 0;
                    bool success = TryReadByOpenCv(
                        normalizedPath,
                        out fps,
                        out totalFrames,
                        out durationSec
                    );
                    return new MovieInfoMetadataProbeResult(
                        "opencv",
                        success,
                        fps,
                        totalFrames,
                        durationSec,
                        "",
                        success ? "ok" : "read_failed"
                    );
                }
            );
            MovieInfoMetadataProbeResult autoGenProbe = BuildProbeResult(
                "autogen",
                () =>
                {
                    double fps = 0;
                    double totalFrames = 0;
                    double durationSec = 0;
                    string videoCodec = "";
                    if (!EnsureAutoGenLoadedSafe(out string loadError))
                    {
                        return MovieInfoMetadataProbeResult.Failed(
                            "autogen",
                            loadError
                        );
                    }

                    bool success = TryReadByAutoGen(
                        rawPath,
                        out fps,
                        out totalFrames,
                        out durationSec,
                        out videoCodec,
                        out string detail
                    );
                    return new MovieInfoMetadataProbeResult(
                        "autogen",
                        success,
                        fps,
                        totalFrames,
                        durationSec,
                        videoCodec,
                        success ? detail : $"read_failed:{detail}"
                    );
                }
            );

            MovieInfoMetadataProbeSet result = new(
                rawPath,
                ffMediaToolkitProbe,
                openCvProbe,
                autoGenProbe
            );
            if (writeDebugLog)
            {
                foreach (string line in result.ToDebugLines())
                {
                    DebugRuntimeLog.Write("movieinfo-probe", line);
                }
            }

            return result;
        }

        /// <summary>
        /// FFMediaToolkitのロード一発勝負！プロセス中で1回だけ試し、ダメなら潔く諦める武士の鑑だ！🏯
        /// </summary>
        private static bool EnsureFfMediaToolkitLoaded()
        {
            lock (FfmpegLoadSync)
            {
                if (ffmpegLoadAttempted)
                {
                    return ffmpegLoadSucceeded;
                }

                ffmpegLoadAttempted = true;
                string ffmpegSharedDir = Path.Combine(
                    AppContext.BaseDirectory,
                    "tools",
                    "ffmpeg-shared"
                );
                string lastError = "";
                try
                {
                    if (!Directory.Exists(ffmpegSharedDir))
                    {
                        lastError = "tools/ffmpeg-shared folder not found";
                    }
                    else if (!HasRequiredSharedDllSet(ffmpegSharedDir))
                    {
                        lastError = "required shared dll set is incomplete";
                    }
                    else
                    {
                        FFmpegLoader.FFmpegPath = ffmpegSharedDir;
                        try
                        {
                            FFmpegLoader.LoadFFmpeg();
                        }
                        catch (InvalidOperationException)
                        {
                            // 他のスレッド等で既にロードされている場合は無視する
                        }
                        ffmpegLoadSucceeded = true;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }

                if (ffmpegLoadSucceeded)
                {
                    DebugRuntimeLog.Write(
                        "movieinfo",
                        $"ffmediatoolkit init ok: dir='{ffmpegSharedDir}'"
                    );
                }
                else
                {
                    string detail = string.IsNullOrWhiteSpace(lastError)
                        ? "shared dll not found"
                        : lastError;
                    DebugRuntimeLog.Write(
                        "movieinfo",
                        $"ffmediatoolkit init fallback to opencv: {detail}"
                    );
                }

                return ffmpegLoadSucceeded;
            }
        }

        private static bool EnsureAutoGenLoadedSafe(out string errorMessage)
        {
            lock (AutoGenLoadSync)
            {
                if (autoGenLoadSucceeded)
                {
                    errorMessage = "";
                    return true;
                }

                if (autoGenLoadAttempted)
                {
                    errorMessage = string.IsNullOrWhiteSpace(autoGenLoadFailureReason)
                        ? "autogen init failed"
                        : autoGenLoadFailureReason;
                    return false;
                }

                autoGenLoadAttempted = true;
                try
                {
                    string ffmpegSharedDir = ResolveFfmpegSharedDirectory();
                    ffmpeg.RootPath = ffmpegSharedDir;
                    DynamicallyLoadedBindings.Initialize();
                    autoGenLoadSucceeded = true;
                    autoGenLoadFailureReason = "";
                    errorMessage = "";
                    return true;
                }
                catch (Exception ex)
                {
                    autoGenLoadSucceeded = false;
                    autoGenLoadFailureReason = ex.Message;
                    errorMessage = ex.Message;
                    DebugRuntimeLog.Write(
                        "movieinfo",
                        $"autogen init failed: reason='{ex.Message}'"
                    );
                    return false;
                }
            }
        }

        private static string ResolveFfmpegSharedDirectory()
        {
            string ffmpegSharedDir = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg-shared");
            if (!Directory.Exists(ffmpegSharedDir))
            {
                throw new DirectoryNotFoundException(
                    "tools/ffmpeg-shared folder not found"
                );
            }

            if (!HasRequiredSharedDllSet(ffmpegSharedDir))
            {
                throw new DirectoryNotFoundException(
                    "required shared dll set is incomplete"
                );
            }

            return ffmpegSharedDir;
        }

        /// <summary>
        /// ベンチマークで鍛え上げられた最強ロジック！「AvgFrameRate / NumberOfFrames / Duration」の黄金のトライアングルでメタ値をひねり出すぜ！📐
        /// </summary>
        private static bool TryReadByFfMediaToolkit(
            string inputPath,
            out double fps,
            out double totalFrames,
            out double durationSec,
            out string videoCodec
        )
        {
            fps = DefaultFps;
            totalFrames = 0;
            durationSec = 0;
            videoCodec = "";

            try
            {
                // 音声のみファイルでも Duration と stream有無を判定できるように AudioVideo で開く。
                MediaOptions options = new() { StreamsToLoad = MediaMode.AudioVideo };
                using var mediaFile = MediaFile.Open(inputPath, options);
                if (mediaFile == null)
                {
                    return false;
                }

                bool hasVideo = mediaFile.HasVideo && mediaFile.VideoStreams.Any();
                bool hasAudio = mediaFile.HasAudio && mediaFile.AudioStreams.Any();

                // Duration はコンテナ情報から取得できるので、映像ストリームの有無に関係なく先に確定する。
                durationSec = mediaFile.Info.Duration.TotalSeconds;

                // MovieInfo の FPS / TotalFrames は映像前提の値なので、映像がある時だけ埋める。
                if (hasVideo)
                {
                    var videoInfo = mediaFile.VideoStreams.First().Info;
                    fps = NormalizeFps(videoInfo.AvgFrameRate);
                    totalFrames =
                        videoInfo.NumberOfFrames
                        ?? Math.Truncate(
                            NormalizeDurationSec(durationSec, totalFrames, fps) * fps
                        );

                    videoCodec = videoInfo.CodecName ?? "";
                }
                else
                {
                    // 音声のみ等は映像メタを持たないため、FPS/Frames は既定値のまま保持する。
                    fps = DefaultFps;
                    totalFrames = 0;
                }

                durationSec = NormalizeDurationSec(durationSec, totalFrames, fps);
                if (!hasVideo && !hasAudio && !IsFinitePositive(durationSec))
                {
                    // stream情報もDurationも得られないケースだけ失敗扱いにして後段へフォールバックする。
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "movieinfo",
                    $"ffmediatoolkit read failed: path='{inputPath}', reason={ex.GetType().Name}"
                );
                return false;
            }
        }

        /// <summary>
        /// FFMediaToolkitが倒れた時の頼れる切り札、OpenCVによる後方互換特攻ルート！🛡️
        /// </summary>
        private static bool TryReadByOpenCv(
            string inputPath,
            out double fps,
            out double totalFrames,
            out double durationSec
        )
        {
            fps = DefaultFps;
            totalFrames = 0;
            durationSec = 0;

            try
            {
                using var capture = new VideoCapture(inputPath);
                if (!capture.IsOpened())
                {
                    return false;
                }

                capture.Grab();
                totalFrames = capture.Get(VideoCaptureProperties.FrameCount);
                fps = NormalizeFps(capture.Get(VideoCaptureProperties.Fps));
                durationSec = NormalizeDurationSec(totalFrames / fps, totalFrames, fps);
                return true;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "movieinfo",
                    $"opencv read failed: path='{inputPath}', reason={ex.GetType().Name}"
                );
                return false;
            }
        }

        private static unsafe bool TryReadByAutoGen(
            string inputPath,
            out double fps,
            out double totalFrames,
            out double durationSec,
            out string videoCodec,
            out string detail
        )
        {
            fps = DefaultFps;
            totalFrames = 0;
            durationSec = 0;
            videoCodec = "";
            detail = "";

            AVFormatContext* pFormatContext = null;
            try
            {
                pFormatContext = ffmpeg.avformat_alloc_context();
                int openRet = ffmpeg.avformat_open_input(&pFormatContext, inputPath, null, null);
                if (openRet < 0)
                {
                    detail = $"open_input:{GetAutoGenErrorMessage(openRet)}";
                    return false;
                }

                int infoRet = ffmpeg.avformat_find_stream_info(pFormatContext, null);
                if (infoRet < 0)
                {
                    detail = $"stream_info:{GetAutoGenErrorMessage(infoRet)}";
                    return false;
                }

                AVStream* pVideoStream = null;
                for (int i = 0; i < pFormatContext->nb_streams; i++)
                {
                    if (
                        pFormatContext->streams[i]->codecpar->codec_type
                        == AVMediaType.AVMEDIA_TYPE_VIDEO
                    )
                    {
                        pVideoStream = pFormatContext->streams[i];
                        break;
                    }
                }

                if (pVideoStream == null)
                {
                    if (pFormatContext->duration > 0)
                    {
                        durationSec = pFormatContext->duration / (double)ffmpeg.AV_TIME_BASE;
                        detail = "format_duration_only";
                        return IsFinitePositive(durationSec);
                    }

                    detail = "video_stream_not_found";
                    return false;
                }

                AVRational avgFrameRate = pVideoStream->avg_frame_rate;
                if (avgFrameRate.den != 0 && avgFrameRate.num > 0)
                {
                    fps = NormalizeFps(avgFrameRate.num / (double)avgFrameRate.den);
                }

                if (pVideoStream->nb_frames > 0)
                {
                    totalFrames = NormalizeTotalFrames(pVideoStream->nb_frames);
                }

                if (pVideoStream->duration > 0)
                {
                    durationSec = pVideoStream->duration * ffmpeg.av_q2d(pVideoStream->time_base);
                    detail = "stream_duration";
                }
                else if (pFormatContext->duration > 0)
                {
                    durationSec = pFormatContext->duration / (double)ffmpeg.AV_TIME_BASE;
                    detail = "format_duration";
                }

                if (totalFrames <= 0 && IsFinitePositive(durationSec) && IsFinitePositive(fps))
                {
                    totalFrames = NormalizeTotalFrames(durationSec * fps);
                }

                durationSec = NormalizeDurationSec(durationSec, totalFrames, fps);
                videoCodec = ffmpeg.avcodec_get_name(pVideoStream->codecpar->codec_id) ?? "";
                return IsFinitePositive(durationSec)
                    || IsFinitePositive(totalFrames)
                    || !string.IsNullOrWhiteSpace(videoCodec);
            }
            catch (Exception ex)
            {
                detail = $"{ex.GetType().Name}:{ex.Message}";
                return false;
            }
            finally
            {
                if (pFormatContext != null)
                {
                    ffmpeg.avformat_close_input(&pFormatContext);
                }
            }
        }

        /// <summary>
        /// 伝説の5つの秘宝（FFmpegの必須DLL群）が全て揃っているかを見極める審美眼！💎✨
        /// 一つでも欠けていたら容赦なく突き返す厳しいチェックだ！
        /// </summary>
        private static bool HasRequiredSharedDllSet(string dir)
        {
            return HasDll(dir, "avcodec*.dll")
                && HasDll(dir, "avformat*.dll")
                && HasDll(dir, "avutil*.dll")
                && HasDll(dir, "swscale*.dll")
                && HasDll(dir, "swresample*.dll");
        }

        /// <summary>
        /// 指定されたパターンのファイルがその地に眠っているかを探り当てるダウジングマシン！🪙
        /// </summary>
        private static bool HasDll(string dir, string pattern)
        {
            try
            {
                return Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly).Any();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 狂ったFPS値を叩き直し、健全で真っ当な数値へと更生させる生活指導員！👊
        /// </summary>
        private static double NormalizeFps(double fps)
        {
            return IsFinitePositive(fps) ? fps : DefaultFps;
        }

        /// <summary>
        /// 浮ついた小数点以下のフレーム数を容赦なく切り捨て、地に足のついた総フレーム数へと鍛え直す！🪓
        /// </summary>
        private static double NormalizeTotalFrames(double totalFrames)
        {
            return IsFinitePositive(totalFrames) ? Math.Truncate(totalFrames) : 0;
        }

        private static bool ShouldTryReadByAutoGen(
            string moviePath,
            bool readByFfMediaToolkit,
            bool readByOpenCv,
            double durationSec
        )
        {
            if (!readByFfMediaToolkit && !readByOpenCv)
            {
                return true;
            }

            string extension = Path.GetExtension(moviePath ?? "");
            if (
                string.Equals(extension, ".avi", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".divx", StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }

            return !IsFinitePositive(durationSec);
        }

        private static void ApplyAutoGenProbeResult(
            string moviePath,
            ref double fps,
            ref double totalFrames,
            ref double durationSec,
            ref string videoCodec,
            double autoGenFps,
            double autoGenTotalFrames,
            double autoGenDurationSec,
            string autoGenVideoCodec,
            string autoGenDetail
        )
        {
            bool durationWasReplaced = false;
            if (!IsFinitePositive(durationSec) && IsFinitePositive(autoGenDurationSec))
            {
                durationSec = autoGenDurationSec;
                durationWasReplaced = true;
            }
            else if (
                IsAutoGenTargetMovie(moviePath)
                && IsFinitePositive(durationSec)
                && IsFinitePositive(autoGenDurationSec)
                && IsDurationMismatch(durationSec, autoGenDurationSec)
            )
            {
                durationSec = autoGenDurationSec;
                durationWasReplaced = true;
            }

            if (!IsFinitePositive(totalFrames) && IsFinitePositive(autoGenTotalFrames))
            {
                totalFrames = autoGenTotalFrames;
            }

            if ((!IsFinitePositive(fps) || fps == DefaultFps) && IsFinitePositive(autoGenFps))
            {
                fps = autoGenFps;
            }

            if (string.IsNullOrWhiteSpace(videoCodec) && !string.IsNullOrWhiteSpace(autoGenVideoCodec))
            {
                videoCodec = autoGenVideoCodec;
            }

            if (durationWasReplaced)
            {
                DebugRuntimeLog.Write(
                    "movieinfo",
                    $"autogen duration applied: movie='{moviePath}' duration_sec={durationSec:0.###} detail='{autoGenDetail}'"
                );
            }
        }

        private static bool IsAutoGenTargetMovie(string moviePath)
        {
            string extension = Path.GetExtension(moviePath ?? "");
            return string.Equals(extension, ".avi", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".divx", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDurationMismatch(double currentDurationSec, double actualDurationSec)
        {
            if (!IsFinitePositive(currentDurationSec) || !IsFinitePositive(actualDurationSec))
            {
                return false;
            }

            double diff = Math.Abs(currentDurationSec - actualDurationSec);
            double ratio =
                currentDurationSec > actualDurationSec
                    ? currentDurationSec / actualDurationSec
                    : actualDurationSec / currentDurationSec;
            return diff >= DurationMismatchAbsoluteThresholdSec
                && ratio >= DurationMismatchRatioThreshold;
        }

        /// <summary>
        /// 真の再生時間(Duration)を導き出す最終アンサー！⏳
        /// コンテナ由来の時間がアテにならなければ、総フレーム数とFPSから執念で計算し直すサバイバル特化のメソッドだ！🔥
        /// </summary>
        internal static double NormalizeDurationSec(
            string moviePath,
            double durationSec,
            double totalFrames,
            double fps,
            string sourceTag
        )
        {
            double normalized = NormalizeDurationSec(durationSec, totalFrames, fps);
            WriteAviDurationRecoveryDebugLog(
                moviePath,
                sourceTag,
                durationSec,
                totalFrames,
                fps,
                normalized
            );
            return normalized;
        }

        internal static double NormalizeDurationSec(
            double durationSec,
            double totalFrames,
            double fps
        )
        {
            // 映像フレーム数が取れている時は、コンテナ長が壊れていても映像実尺を復元できる。
            double frameDerivedDurationSec =
                IsFinitePositive(totalFrames) && IsFinitePositive(fps)
                    ? totalFrames / fps
                    : 0;

            if (IsFinitePositive(durationSec))
            {
                if (IsFinitePositive(frameDerivedDurationSec))
                {
                    double diff = Math.Abs(durationSec - frameDerivedDurationSec);
                    double ratio = durationSec > frameDerivedDurationSec
                        ? durationSec / frameDerivedDurationSec
                        : frameDerivedDurationSec / durationSec;

                    // AVIなどで音声側メタだけ壊れていると、コンテナ尺が数百倍に化けることがある。
                    // サムネ用途ではシーク可能な映像尺を優先した方が安全。
                    if (
                        diff >= DurationMismatchAbsoluteThresholdSec
                        && ratio >= DurationMismatchRatioThreshold
                    )
                    {
                        return frameDerivedDurationSec;
                    }
                }

                return durationSec;
            }

            if (IsFinitePositive(frameDerivedDurationSec))
            {
                return frameDerivedDurationSec;
            }

            return 0;
        }

        // AVI系だけ詳細を出し、尺補正に入ったかどうかを debug-runtime.log で追えるようにする。
        private static void WriteAviDurationRecoveryDebugLog(
            string moviePath,
            string sourceTag,
            double containerDurationSec,
            double totalFrames,
            double fps,
            double normalizedDurationSec
        )
        {
            string extension = Path.GetExtension(moviePath ?? "");
            if (
                !string.Equals(extension, ".avi", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".divx", StringComparison.OrdinalIgnoreCase)
            )
            {
                return;
            }

            double frameDerivedDurationSec =
                IsFinitePositive(totalFrames) && IsFinitePositive(fps) ? totalFrames / fps : 0;
            bool hasContainerDuration = IsFinitePositive(containerDurationSec);
            bool hasFrameDerivedDuration = IsFinitePositive(frameDerivedDurationSec);
            double diff =
                hasContainerDuration && hasFrameDerivedDuration
                    ? Math.Abs(containerDurationSec - frameDerivedDurationSec)
                    : 0;
            double ratio =
                hasContainerDuration && hasFrameDerivedDuration
                    ? (
                        containerDurationSec > frameDerivedDurationSec
                            ? containerDurationSec / frameDerivedDurationSec
                            : frameDerivedDurationSec / containerDurationSec
                    )
                    : 0;
            bool mismatchDetected =
                hasContainerDuration
                && hasFrameDerivedDuration
                && diff >= DurationMismatchAbsoluteThresholdSec
                && ratio >= DurationMismatchRatioThreshold;
            bool usedFrameDerived =
                hasFrameDerivedDuration
                && Math.Abs(normalizedDurationSec - frameDerivedDurationSec) < 0.001d;

            string decision = mismatchDetected
                ? (usedFrameDerived ? "use_frame_duration" : "keep_container_duration")
                : hasContainerDuration
                    ? "container_duration_ok"
                    : hasFrameDerivedDuration
                        ? "frame_duration_only"
                        : "duration_unresolved";

            DebugRuntimeLog.Write(
                "avi-duration-recovery",
                $"source={sourceTag} decision={decision} movie='{moviePath}' "
                    + $"container_sec={containerDurationSec:0.###} frame_sec={frameDerivedDurationSec:0.###} "
                    + $"normalized_sec={normalizedDurationSec:0.###} frames={totalFrames:0.###} fps={fps:0.###} "
                    + $"diff_sec={diff:0.###} ratio={ratio:0.###}"
            );
        }

        /// <summary>
        /// NaNやInfinityといった混沌(カオス)を退け、この世の理にかなった「正の有限値」だけを通す絶対の門番！🚪🛡️
        /// </summary>
        private static bool IsFinitePositive(double value)
        {
            return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static MovieInfoMetadataProbeResult BuildProbeResult(
            string source,
            Func<MovieInfoMetadataProbeResult> action
        )
        {
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                return MovieInfoMetadataProbeResult.Failed(
                    source,
                    $"{ex.GetType().Name}:{ex.Message}"
                );
            }
        }

        private static unsafe string GetAutoGenErrorMessage(int errorCode)
        {
            byte* buffer = stackalloc byte[1024];
            ffmpeg.av_strerror(errorCode, buffer, 1024);
            return Marshal.PtrToStringAnsi((IntPtr)buffer) ?? $"ffmpeg_error:{errorCode}";
        }
    }

    public sealed class MovieInfoMetadataProbeSet
    {
        public MovieInfoMetadataProbeSet(
            string moviePath,
            MovieInfoMetadataProbeResult ffMediaToolkit,
            MovieInfoMetadataProbeResult openCv,
            MovieInfoMetadataProbeResult autoGen
        )
        {
            MoviePath = moviePath ?? "";
            FfMediaToolkit = ffMediaToolkit;
            OpenCv = openCv;
            AutoGen = autoGen;
        }

        public string MoviePath { get; }

        public MovieInfoMetadataProbeResult FfMediaToolkit { get; }

        public MovieInfoMetadataProbeResult OpenCv { get; }

        public MovieInfoMetadataProbeResult AutoGen { get; }

        public IEnumerable<string> ToDebugLines()
        {
            yield return $"movie='{MoviePath}' source={FfMediaToolkit.ToDebugLine()}";
            yield return $"movie='{MoviePath}' source={OpenCv.ToDebugLine()}";
            yield return $"movie='{MoviePath}' source={AutoGen.ToDebugLine()}";
        }
    }

    public sealed class MovieInfoMetadataProbeResult
    {
        public MovieInfoMetadataProbeResult(
            string source,
            bool isSuccess,
            double fps,
            double totalFrames,
            double durationSec,
            string videoCodec,
            string detail
        )
        {
            Source = source ?? "";
            IsSuccess = isSuccess;
            Fps = fps;
            TotalFrames = totalFrames;
            DurationSec = durationSec;
            VideoCodec = videoCodec ?? "";
            Detail = detail ?? "";
        }

        public string Source { get; }

        public bool IsSuccess { get; }

        public double Fps { get; }

        public double TotalFrames { get; }

        public double DurationSec { get; }

        public string VideoCodec { get; }

        public string Detail { get; }

        public string ToDebugLine()
        {
            return $"name={Source} success={IsSuccess} duration_sec={DurationSec:0.###} fps={Fps:0.###} "
                + $"frames={TotalFrames:0.###} codec='{VideoCodec}' detail='{Detail}'";
        }

        public static MovieInfoMetadataProbeResult Failed(string source, string detail)
        {
            return new MovieInfoMetadataProbeResult(source, false, 0, 0, 0, "", detail);
        }
    }
}
