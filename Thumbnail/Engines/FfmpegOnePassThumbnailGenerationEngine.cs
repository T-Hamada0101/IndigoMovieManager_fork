using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace IndigoMovieManager.Thumbnail.Engines
{
    /// <summary>
    /// ffmpeg 1パス（抽出+tile）でサムネを作るエンジン。
    /// </summary>
    internal sealed class FfmpegOnePassThumbnailGenerationEngine : IThumbnailGenerationEngine
    {
        private const string FfmpegExePathEnvName = "IMM_FFMPEG_EXE_PATH";
        private const string GpuDecodeModeEnvName = "IMM_THUMB_GPU_DECODE";
        private const string FfmpegJpegQualityEnvName = "IMM_THUMB_JPEG_Q";
        private const string FfmpegScaleFlagsEnvName = "IMM_THUMB_SCALE_FLAGS";
        private const string FfmpegTimeoutSecEnvName = "IMM_THUMB_FFMPEG_TIMEOUT_SEC";
        private const string FfmpegPriorityEnvName = "IMM_THUMB_FFMPEG_PRIORITY";
        private const int DefaultJpegQuality = 5;
        private const double ShortClipSeekFallbackMaxDurationSec = 1.0d;
        private static readonly ProcessPriorityClass DefaultFfmpegPriorityClass =
            ProcessPriorityClass.Idle;

        public string EngineId => "ffmpeg1pass";
        public string EngineName => "ffmpeg1pass";

        public bool CanHandle(ThumbnailJobContext context)
        {
            return true;
        }

        public async Task<ThumbnailCreateResult> CreateAsync(
            ThumbnailJobContext context,
            CancellationToken cts = default
        )
        {
            if (context == null)
            {
                return ThumbnailResultFactory.CreateFailed("", null, "context is null");
            }

            if (context.IsManual)
            {
                return ThumbnailResultFactory.CreateFailed(
                    context.SaveThumbFileName,
                    context.DurationSec,
                    "ffmpeg1pass does not support manual mode"
                );
            }

            if (context.ThumbInfo == null || context.ThumbInfo.ThumbSec == null || context.ThumbInfo.ThumbSec.Count < 1)
            {
                return ThumbnailResultFactory.CreateFailed(
                    context.SaveThumbFileName,
                    context.DurationSec,
                    "thumb info is empty"
                );
            }

            double? durationSec = context.DurationSec;
            if (!durationSec.HasValue || durationSec.Value <= 0)
            {
                durationSec = ThumbnailShellMetadataUtility.TryGetDurationSecFromShell(
                    context.MovieFullPath
                );
            }

            int panelCount = context.ThumbInfo.ThumbSec.Count;
            int cols = context.TabInfo.Columns;
            int rows = context.TabInfo.Rows;
            if (panelCount < 1 || cols < 1 || rows < 1)
            {
                return ThumbnailResultFactory.CreateFailed(
                    context.SaveThumbFileName,
                    durationSec,
                    "invalid panel configuration"
                );
            }

            Size targetSize = ResolveTargetSize(context);
            string saveDir = Path.GetDirectoryName(context.SaveThumbFileName) ?? "";
            if (!string.IsNullOrWhiteSpace(saveDir))
            {
                Directory.CreateDirectory(saveDir);
            }

            string ffmpegExePath = ResolveFfmpegExecutablePath();
            double startSec = Math.Max(0, context.ThumbInfo.ThumbSec[0]);
            double actualStartSec = startSec;
            double intervalSec = ResolveFrameIntervalSec(context.ThumbInfo.ThumbSec, durationSec, panelCount);
            List<double> actualCaptureSeconds = BuildExpectedCaptureSeconds(
                actualStartSec,
                intervalSec,
                panelCount,
                durationSec
            );
            int jpegQuality = ResolveJpegQuality();
            string scaleFlags = ResolveScaleFlags();
            bool useTolerantInput = ShouldUseTolerantInput(context.MovieFullPath);
            bool useCandidateFiltering = ShouldUseCandidateFrameFiltering(context);
            int candidatePanelCount = ResolveCandidatePanelCount(panelCount, useCandidateFiltering);
            double sampleIntervalSec = ResolveSampleIntervalSec(
                intervalSec,
                candidatePanelCount,
                panelCount,
                useCandidateFiltering,
                durationSec
            );
            int sampleCols = useCandidateFiltering ? candidatePanelCount : cols;
            int sampleRows = useCandidateFiltering ? 1 : rows;
            string ffmpegOutputPath = useCandidateFiltering
                ? BuildCandidateTileTempPath(context.SaveThumbFileName)
                : context.SaveThumbFileName;
            TimeSpan ffmpegTimeout = ResolveProcessTimeout(
                panelCount,
                durationSec,
                useTolerantInput,
                useCandidateFiltering
            );

            string startText = startSec.ToString("0.###", CultureInfo.InvariantCulture);
            string vf = BuildTileFilter(
                sampleIntervalSec,
                targetSize.Width,
                targetSize.Height,
                sampleCols,
                sampleRows,
                durationSec,
                candidatePanelCount,
                scaleFlags
            );

            (bool ok, string err) = await TryCreateTileImageAsync(
                ffmpegExePath,
                context.MovieFullPath,
                startText,
                vf,
                ffmpegOutputPath,
                jpegQuality,
                useTolerantInput,
                ffmpegTimeout,
                cts
            );
            if (
                (!ok || !Path.Exists(ffmpegOutputPath))
                && ShouldUseShortClipSeekFallback(durationSec, panelCount)
            )
            {
                IReadOnlyList<double> shortClipSeekCandidates = BuildShortClipSeekCandidates(
                    durationSec,
                    startSec
                );
                ThumbnailRuntimeLog.Write(
                    "ffmpeg1pass-shortclip-seek",
                    $"fallback requested: movie='{context.MovieFullPath}' duration_sec={durationSec.Value.ToString("0.###", CultureInfo.InvariantCulture)} "
                        + $"panel_count={panelCount} original_start_sec={startSec.ToString("0.###", CultureInfo.InvariantCulture)} "
                        + $"candidates=[{string.Join(",", shortClipSeekCandidates.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture)))}] err='{err}'"
                );
                foreach (double fallbackStartSec in shortClipSeekCandidates)
                {
                    string fallbackStartText = fallbackStartSec.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture
                    );
                    ThumbnailRuntimeLog.Write(
                        "ffmpeg1pass-shortclip-seek",
                        $"fallback try: movie='{context.MovieFullPath}' start_sec={fallbackStartText}"
                    );
                    TryDeleteFile(ffmpegOutputPath);
                    (ok, err) = await TryCreateTileImageAsync(
                        ffmpegExePath,
                        context.MovieFullPath,
                        fallbackStartText,
                        vf,
                        ffmpegOutputPath,
                        jpegQuality,
                        useTolerantInput,
                        ffmpegTimeout,
                        cts
                    );
                    if (ok && Path.Exists(ffmpegOutputPath))
                    {
                        actualStartSec = fallbackStartSec;
                        actualCaptureSeconds = BuildExpectedCaptureSeconds(
                            actualStartSec,
                            intervalSec,
                            panelCount,
                            durationSec
                        );
                        ThumbnailRuntimeLog.Write(
                            "ffmpeg1pass-shortclip-seek",
                            $"fallback hit: movie='{context.MovieFullPath}' start_sec={fallbackStartText} output='{ffmpegOutputPath}'"
                        );
                        break;
                    }

                    ThumbnailRuntimeLog.Write(
                        "ffmpeg1pass-shortclip-seek",
                        $"fallback miss: movie='{context.MovieFullPath}' start_sec={fallbackStartText} err='{err}'"
                    );
                }

                if (!ok || !Path.Exists(ffmpegOutputPath))
                {
                    ThumbnailRuntimeLog.Write(
                        "ffmpeg1pass-shortclip-seek",
                        $"fallback exhausted: movie='{context.MovieFullPath}' err='{err}'"
                    );
                }
            }

            if (!ok || !Path.Exists(ffmpegOutputPath))
            {
                return ThumbnailResultFactory.CreateFailed(
                    context.SaveThumbFileName,
                    durationSec,
                    string.IsNullOrWhiteSpace(err) ? "ffmpeg one-pass failed" : err
                );
            }

            if (
                useCandidateFiltering
                && !TryComposeFilteredTile(
                    ffmpegOutputPath,
                    context.SaveThumbFileName,
                    cols,
                    rows,
                    candidatePanelCount,
                    sampleIntervalSec,
                    actualStartSec,
                    durationSec,
                    out actualCaptureSeconds,
                    out string composeError
                )
            )
            {
                TryDeleteFile(ffmpegOutputPath);
                return ThumbnailResultFactory.CreateFailed(
                    context.SaveThumbFileName,
                    durationSec,
                    composeError
                );
            }

            if (useCandidateFiltering)
            {
                TryDeleteFile(ffmpegOutputPath);
            }

            ThumbInfo saveThumbInfo = ThumbnailImageUtility.RebuildThumbInfoWithCaptureSeconds(
                context.ThumbInfo,
                actualCaptureSeconds
            );
            TryDeleteFile(context.SaveThumbFileName);
            using FileStream dest = new(context.SaveThumbFileName, FileMode.Append, FileAccess.Write);
            dest.Write(saveThumbInfo.SecBuffer);
            dest.Write(saveThumbInfo.InfoBuffer);
            return ThumbnailResultFactory.CreateSuccess(context.SaveThumbFileName, durationSec);
        }

        public async Task<bool> CreateBookmarkAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePos,
            CancellationToken cts = default
        )
        {
            if (!Path.Exists(movieFullPath))
            {
                return false;
            }

            string saveDir = Path.GetDirectoryName(saveThumbPath) ?? "";
            if (!string.IsNullOrWhiteSpace(saveDir))
            {
                Directory.CreateDirectory(saveDir);
            }

            string ffmpegExePath = ResolveFfmpegExecutablePath();
            string posSec = Math.Max(0, capturePos).ToString("0.###", CultureInfo.InvariantCulture);
            int jpegQuality = ResolveJpegQuality();
            string scaleFlags = ResolveScaleFlags();
            bool useTolerantInput = ShouldUseTolerantInput(movieFullPath);
            string vf =
                $"crop='if(gte(iw/ih,4/3),ih*4/3,iw)':'if(gte(iw/ih,4/3),ih,iw*3/4)',scale=640:480:flags={scaleFlags}";

            ProcessStartInfo psi = new()
            {
                FileName = ffmpegExePath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            if (!useTolerantInput)
            {
                AddHwAccelArguments(psi);
            }
            psi.ArgumentList.Add("-an");
            psi.ArgumentList.Add("-sn");
            psi.ArgumentList.Add("-dn");
            AddInputArguments(psi, movieFullPath, posSec, useTolerantInput);
            psi.ArgumentList.Add("-frames:v");
            psi.ArgumentList.Add("1");
            // 失敗率低減を優先し、厳格判定で弾かれやすい非標準YUV系も許容して処理継続しやすくする。
            psi.ArgumentList.Add("-strict");
            psi.ArgumentList.Add("unofficial");
            // 失敗率低減を優先し、出力ピクセル形式は互換性の高い yuv420p に固定する。
            psi.ArgumentList.Add("-pix_fmt");
            psi.ArgumentList.Add("yuv420p");
            psi.ArgumentList.Add("-q:v");
            psi.ArgumentList.Add(jpegQuality.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-vf");
            psi.ArgumentList.Add(vf);
            psi.ArgumentList.Add(saveThumbPath);

            TimeSpan ffmpegTimeout = ResolveProcessTimeout(
                panelCount: 1,
                durationSec: null,
                useTolerantInput,
                useCandidateFiltering: false
            );
            (bool ok, _) = await RunProcessAsync(psi, ffmpegTimeout, cts);
            return ok && Path.Exists(saveThumbPath);
        }

        private static Size ResolveTargetSize(ThumbnailJobContext context)
        {
            if (context.IsResizeThumb && context.TabInfo.Width > 0 && context.TabInfo.Height > 0)
            {
                return new Size(context.TabInfo.Width, context.TabInfo.Height);
            }

            // 非リサイズ時は既存既定値に近い固定値を使う。
            return new Size(320, 240);
        }

        private static double ResolveFrameIntervalSec(
            IReadOnlyList<int> secList,
            double? durationSec,
            int panelCount
        )
        {
            if (secList != null && secList.Count >= 2)
            {
                int interval = secList[1] - secList[0];
                if (interval > 0)
                {
                    return interval;
                }
            }

            if (durationSec.HasValue && durationSec.Value > 0 && panelCount > 0)
            {
                double divide = durationSec.Value / (panelCount + 1);
                if (divide > 0.1)
                {
                    return divide;
                }
            }

            return 1d;
        }

        private static string BuildTileFilter(
            double intervalSec,
            int width,
            int height,
            int cols,
            int rows,
            double? durationSec,
            int panelCount,
            string scaleFlags
        )
        {
            double safeInterval = intervalSec > 0 ? intervalSec : 1d;
            string intervalText = safeInterval.ToString("0.###", CultureInfo.InvariantCulture);
            StringBuilder vf = new();

            // 短尺で必要フレーム数が不足する場合は、末尾フレーム複製で tile 完成を保証する。
            if (
                durationSec.HasValue
                && durationSec.Value > 0
                && panelCount > 0
                && durationSec.Value < safeInterval * panelCount
            )
            {
                double padSec = (safeInterval * panelCount) - durationSec.Value + 0.05;
                string padText = padSec.ToString("0.###", CultureInfo.InvariantCulture);
                vf.Append($"tpad=stop_mode=clone:stop_duration={padText},");
            }

            vf.Append($"fps=1/{intervalText},");
            vf.Append("crop='if(gte(iw/ih,4/3),ih*4/3,iw)':'if(gte(iw/ih,4/3),ih,iw*3/4)',");
            vf.Append($"scale={width}:{height}:flags={scaleFlags},");
            vf.Append($"tile={cols}x{rows}");
            return vf.ToString();
        }

        // 壊れ気味のASF/WMVはインデックス無視と破損許容を優先し、入力後シークへ切り替える。
        internal static bool ShouldUseTolerantInput(string movieFullPath)
        {
            string extension = Path.GetExtension(movieFullPath ?? "");
            return extension.Equals(".wmv", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".asf", StringComparison.OrdinalIgnoreCase);
        }

        // Recovery時の壊れ気味WMV/ASFだけ、候補を倍取りして黒コマを避ける。
        internal static bool ShouldUseCandidateFrameFiltering(ThumbnailJobContext context)
        {
            if (context == null)
            {
                return false;
            }

            int attemptCount = context.QueueObj?.AttemptCount ?? 0;
            return attemptCount > 0
                && context.PanelCount > 0
                && context.PanelCount <= 10
                && ShouldUseTolerantInput(context.MovieFullPath);
        }

        internal static int ResolveCandidatePanelCount(int panelCount, bool useCandidateFiltering)
        {
            if (!useCandidateFiltering || panelCount < 1)
            {
                return panelCount;
            }

            return panelCount * 2;
        }

        internal static double ResolveSampleIntervalSec(
            double intervalSec,
            int candidatePanelCount,
            int panelCount,
            bool useCandidateFiltering,
            double? durationSec
        )
        {
            if (candidatePanelCount <= panelCount || intervalSec <= 0)
            {
                return intervalSec;
            }

            double oversampleFactor = (double)candidatePanelCount / panelCount;
            double oversampledIntervalSec = Math.Max(0.1d, intervalSec / oversampleFactor);
            if (!useCandidateFiltering)
            {
                return oversampledIntervalSec;
            }

            // 壊れ気味WMV/ASFは広く飛ぶより、前半区間を密に舐めた方が複数コマを拾いやすい。
            int sampleStepCount = Math.Max(1, candidatePanelCount - 1);
            double packedWindowSec = ResolvePackedRecoveryWindowSec(durationSec);
            double packedIntervalSec = Math.Max(0.25d, packedWindowSec / sampleStepCount);
            return Math.Max(0.25d, Math.Min(oversampledIntervalSec, packedIntervalSec));
        }

        internal static double ResolvePackedRecoveryWindowSec(double? durationSec)
        {
            if (!durationSec.HasValue || durationSec.Value <= 0)
            {
                return 12d;
            }

            return Math.Min(20d, Math.Max(4d, durationSec.Value * 0.25d));
        }

        // 短尺で開始秒の作り方だけが外れている時は、先頭付近の候補を浅く舐めて回復を狙う。
        internal static bool ShouldUseShortClipSeekFallback(double? durationSec, int panelCount)
        {
            return durationSec.HasValue
                && durationSec.Value > 0
                && durationSec.Value <= ShortClipSeekFallbackMaxDurationSec
                && panelCount > 0
                && panelCount <= 5;
        }

        internal static IReadOnlyList<double> BuildShortClipSeekCandidates(
            double? durationSec,
            double originalStartSec
        )
        {
            if (!durationSec.HasValue || durationSec.Value <= 0)
            {
                return [];
            }

            List<double> rawCandidates =
            [
                0d,
                0.001d,
                0.005d,
                0.01d,
                0.016d,
                0.033d,
                0.05d,
                0.069d,
                0.1d,
                0.25d,
                0.5d,
                durationSec.Value * 0.1d,
                durationSec.Value * 0.25d,
                durationSec.Value * 0.5d,
                Math.Max(0d, durationSec.Value - 0.01d),
            ];

            SortedDictionary<string, double> normalized = new(StringComparer.Ordinal);
            foreach (double rawCandidate in rawCandidates)
            {
                double? clamped = ClampShortClipSeekCandidate(rawCandidate, durationSec.Value);
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

        private static double? ClampShortClipSeekCandidate(double candidate, double durationSec)
        {
            if (candidate < 0)
            {
                return null;
            }

            if (candidate == 0)
            {
                return 0;
            }

            double maxSeek = Math.Max(0, durationSec - 0.001d);
            if (maxSeek <= 0)
            {
                return 0;
            }

            if (candidate > maxSeek)
            {
                return null;
            }

            return candidate;
        }

        // 救済優先モードでは入力側の寛容オプションを付け、-ss を入力後へ回して実フレーム取得を優先する。
        internal static void AddInputArguments(
            ProcessStartInfo psi,
            string movieFullPath,
            string startText,
            bool useTolerantInput
        )
        {
            if (useTolerantInput)
            {
                psi.ArgumentList.Add("-err_detect");
                psi.ArgumentList.Add("ignore_err");
                psi.ArgumentList.Add("-fflags");
                psi.ArgumentList.Add("+genpts+igndts+ignidx+discardcorrupt");
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add(movieFullPath);
                psi.ArgumentList.Add("-ss");
                psi.ArgumentList.Add(startText);
                return;
            }

            psi.ArgumentList.Add("-ss");
            psi.ArgumentList.Add(startText);
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(movieFullPath);
        }

        internal static List<int> SelectCandidateIndices(
            IReadOnlyList<bool> nonBlackCandidates,
            int desiredCount
        )
        {
            List<int> selected = [];
            if (desiredCount < 1 || nonBlackCandidates == null || nonBlackCandidates.Count < 1)
            {
                return selected;
            }

            List<int> validIndices = [];
            for (int i = 0; i < nonBlackCandidates.Count; i++)
            {
                if (nonBlackCandidates[i])
                {
                    validIndices.Add(i);
                }
            }

            if (validIndices.Count > 0)
            {
                AddDistributedIndices(selected, validIndices, desiredCount);
            }

            if (selected.Count < desiredCount)
            {
                List<int> allIndices = [];
                for (int i = 0; i < nonBlackCandidates.Count; i++)
                {
                    allIndices.Add(i);
                }

                AddDistributedIndices(selected, allIndices, desiredCount);
            }

            if (selected.Count > desiredCount)
            {
                selected.RemoveRange(desiredCount, selected.Count - desiredCount);
            }

            while (selected.Count < desiredCount)
            {
                selected.Add(selected.Count > 0 ? selected[selected.Count - 1] : 0);
            }

            return selected;
        }

        internal static bool IsMostlyBlackPanel(Bitmap bitmap)
        {
            if (bitmap == null || bitmap.Width < 1 || bitmap.Height < 1)
            {
                return true;
            }

            const int DarkLumaThreshold = 24;
            const double AvgLumaThreshold = 20d;
            const double DarkRatioThreshold = 0.92d;
            int stepX = Math.Max(1, bitmap.Width / 24);
            int stepY = Math.Max(1, bitmap.Height / 24);
            double lumaTotal = 0;
            int darkCount = 0;
            int sampleCount = 0;

            for (int y = 0; y < bitmap.Height; y += stepY)
            {
                for (int x = 0; x < bitmap.Width; x += stepX)
                {
                    Color color = bitmap.GetPixel(x, y);
                    double luma = (0.299d * color.R) + (0.587d * color.G) + (0.114d * color.B);
                    lumaTotal += luma;
                    sampleCount++;
                    if (luma <= DarkLumaThreshold)
                    {
                        darkCount++;
                    }
                }
            }

            if (sampleCount < 1)
            {
                return true;
            }

            double avgLuma = lumaTotal / sampleCount;
            double darkRatio = (double)darkCount / sampleCount;
            return avgLuma <= AvgLumaThreshold && darkRatio >= DarkRatioThreshold;
        }

        // 候補タイルを一度ばらし、黒っぽいコマを避けて本来のパネル数へ組み直す。
        internal static bool TryComposeFilteredTile(
            string candidateTilePath,
            string destinationPath,
            int cols,
            int rows,
            int candidatePanelCount,
            double intervalSec,
            double startSec,
            double? durationSec,
            out List<double> actualCaptureSeconds,
            out string error
        )
        {
            error = "";
            actualCaptureSeconds = [];
            if (!Path.Exists(candidateTilePath))
            {
                error = "candidate tile image is missing";
                return false;
            }

            if (cols < 1 || rows < 1 || candidatePanelCount < 1)
            {
                error = "invalid candidate tile configuration";
                return false;
            }

            int desiredPanelCount = cols * rows;
            using Bitmap candidateSheet = new(candidateTilePath);
            int panelWidth = candidateSheet.Width / candidatePanelCount;
            int panelHeight = candidateSheet.Height;
            if (
                panelWidth < 1
                || panelHeight < 1
                || panelWidth * candidatePanelCount != candidateSheet.Width
            )
            {
                error = "candidate tile image size is invalid";
                return false;
            }

            List<Bitmap> panels = [];
            List<bool> nonBlackPanels = [];
            try
            {
                for (int i = 0; i < candidatePanelCount; i++)
                {
                    Rectangle srcRect = new(i * panelWidth, 0, panelWidth, panelHeight);
                    Bitmap panel = new(panelWidth, panelHeight, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                    using (Graphics g = Graphics.FromImage(panel))
                    {
                        g.DrawImage(
                            candidateSheet,
                            new Rectangle(0, 0, panelWidth, panelHeight),
                            srcRect,
                            GraphicsUnit.Pixel
                        );
                    }

                    panels.Add(panel);
                    nonBlackPanels.Add(!IsMostlyBlackPanel(panel));
                }

                List<int> selectedIndices = SelectCandidateIndices(
                    nonBlackPanels,
                    desiredPanelCount
                );
                actualCaptureSeconds = BuildSelectedCaptureSeconds(
                    startSec,
                    intervalSec,
                    candidatePanelCount,
                    selectedIndices,
                    durationSec
                );

                using Bitmap canvas = new(
                    panelWidth * cols,
                    panelHeight * rows,
                    System.Drawing.Imaging.PixelFormat.Format24bppRgb
                );
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    g.Clear(Color.Black);
                    for (int i = 0; i < selectedIndices.Count; i++)
                    {
                        int selectedIndex = selectedIndices[i];
                        if (selectedIndex < 0 || selectedIndex >= panels.Count)
                        {
                            continue;
                        }

                        int drawX = (i % cols) * panelWidth;
                        int drawY = (i / cols) * panelHeight;
                        g.DrawImage(panels[selectedIndex], drawX, drawY, panelWidth, panelHeight);
                    }
                }

                if (Path.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }

                canvas.Save(destinationPath);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                foreach (Bitmap panel in panels)
                {
                    panel.Dispose();
                }
            }
        }

        // ffmpeg1pass が実際に拾った想定秒列を、保存メタにも反映できる形へ並べる。
        private static List<double> BuildExpectedCaptureSeconds(
            double startSec,
            double intervalSec,
            int panelCount,
            double? durationSec
        )
        {
            List<double> captureSeconds = [];
            if (panelCount < 1)
            {
                return captureSeconds;
            }

            double safeInterval = intervalSec > 0 ? intervalSec : 1d;
            double maxSec =
                durationSec.HasValue && durationSec.Value > 0
                    ? Math.Max(0d, durationSec.Value - 0.001d)
                    : double.MaxValue;
            for (int i = 0; i < panelCount; i++)
            {
                double sec = startSec + (safeInterval * i);
                captureSeconds.Add(Math.Max(0d, Math.Min(sec, maxSec)));
            }

            return captureSeconds;
        }

        // 候補タイルから選ばれた index を、実秒列へ引き戻して保存メタを合わせる。
        private static List<double> BuildSelectedCaptureSeconds(
            double startSec,
            double intervalSec,
            int candidatePanelCount,
            IReadOnlyList<int> selectedIndices,
            double? durationSec
        )
        {
            List<double> candidateSeconds = BuildExpectedCaptureSeconds(
                startSec,
                intervalSec,
                candidatePanelCount,
                durationSec
            );
            List<double> selectedSeconds = [];
            if (selectedIndices == null || selectedIndices.Count < 1)
            {
                return selectedSeconds;
            }

            for (int i = 0; i < selectedIndices.Count; i++)
            {
                int selectedIndex = selectedIndices[i];
                if (selectedIndex < 0 || selectedIndex >= candidateSeconds.Count)
                {
                    continue;
                }

                selectedSeconds.Add(candidateSeconds[selectedIndex]);
            }

            return selectedSeconds;
        }

        private static void AddDistributedIndices(
            List<int> selected,
            IReadOnlyList<int> sourceIndices,
            int desiredCount
        )
        {
            if (sourceIndices == null || sourceIndices.Count < 1 || selected.Count >= desiredCount)
            {
                return;
            }

            if (sourceIndices.Count == 1)
            {
                if (!selected.Contains(sourceIndices[0]))
                {
                    selected.Add(sourceIndices[0]);
                }
                return;
            }

            for (int i = 0; i < desiredCount; i++)
            {
                int sourcePos = (int)Math.Round(
                    ((double)(sourceIndices.Count - 1) * i) / Math.Max(1, desiredCount - 1)
                );
                sourcePos = Math.Max(0, Math.Min(sourceIndices.Count - 1, sourcePos));
                int candidateIndex = sourceIndices[sourcePos];
                if (!selected.Contains(candidateIndex))
                {
                    selected.Add(candidateIndex);
                }

                if (selected.Count >= desiredCount)
                {
                    return;
                }
            }
        }

        private static string BuildCandidateTileTempPath(string saveThumbFileName)
        {
            string directory = Path.GetDirectoryName(saveThumbFileName) ?? Path.GetTempPath();
            string fileName = Path.GetFileNameWithoutExtension(saveThumbFileName);
            string extension = Path.GetExtension(saveThumbFileName);
            return Path.Combine(directory, $"{fileName}.candidates.{Guid.NewGuid():N}{extension}");
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (Path.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 一時ファイル削除失敗は本処理を落とさない。
            }
        }

        // JPEG品質は 2〜31 の範囲のみ受け入れ、範囲外は既定値へフォールバックする。
        private static int ResolveJpegQuality()
        {
            string raw = Environment.GetEnvironmentVariable(FfmpegJpegQualityEnvName)?.Trim() ?? "";
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                if (parsed >= 2 && parsed <= 31)
                {
                    return parsed;
                }
            }
            return DefaultJpegQuality;
        }

        // スケーラは速度優先の bilinear を既定にし、必要時のみ環境変数で変更できるようにする。
        private static string ResolveScaleFlags()
        {
            string raw = Environment.GetEnvironmentVariable(FfmpegScaleFlagsEnvName)?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "bilinear";
            }

            return raw.ToLowerInvariant() switch
            {
                "nearest" => "nearest",
                "bilinear" => "bilinear",
                "bicubic" => "bicubic",
                "lanczos" => "lanczos",
                _ => "bilinear",
            };
        }

        internal static TimeSpan ResolveProcessTimeout(
            int panelCount,
            double? durationSec,
            bool useTolerantInput,
            bool useCandidateFiltering
        )
        {
            string raw = Environment.GetEnvironmentVariable(FfmpegTimeoutSecEnvName)?.Trim() ?? "";
            if (
                int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int envSec)
                && envSec >= 5
            )
            {
                return TimeSpan.FromSeconds(envSec);
            }

            return Timeout.InfiniteTimeSpan;
        }

        internal static ProcessPriorityClass? ResolveChildProcessPriorityClass()
        {
            string raw = Environment.GetEnvironmentVariable(FfmpegPriorityEnvName)?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(raw))
            {
                return DefaultFfmpegPriorityClass;
            }

            if (string.Equals(raw, "off", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (Enum.TryParse(raw, true, out ProcessPriorityClass parsed))
            {
                return parsed;
            }

            return DefaultFfmpegPriorityClass;
        }

        /// <summary>
        /// GPUモード設定に応じて ffmpeg の -hwaccel を付与する。
        /// </summary>
        private static void AddHwAccelArguments(ProcessStartInfo psi)
        {
            string mode = ThumbnailEnvConfig.NormalizeGpuDecodeMode(
                Environment.GetEnvironmentVariable(GpuDecodeModeEnvName)?.Trim()
            );
            string hwAccel = mode switch
            {
                "cuda" => "cuda",
                "qsv" => "qsv",
                // AMD系は d3d11va を優先。
                "amd" => "d3d11va",
                // 明示OFFはCPUデコード固定。
                "off" => "",
                // 未指定時は従来どおりautoで実行。
                _ => "auto",
            };

            if (string.IsNullOrWhiteSpace(hwAccel))
            {
                return;
            }

            psi.ArgumentList.Add("-hwaccel");
            psi.ArgumentList.Add(hwAccel);
        }

        private static async Task<(bool ok, string err)> RunProcessAsync(
            ProcessStartInfo psi,
            TimeSpan timeout,
            CancellationToken cts
        )
        {
            try
            {
                using Process process = new() { StartInfo = psi };
                if (!process.Start())
                {
                    return (false, "process start returned false");
                }

                TryApplyChildProcessPriority(process);

                // 出力読取りを先に並走させ、ReadToEnd待ちが終了待ちを塞がないようにする。
                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                Task waitForExitTask = process.WaitForExitAsync();
                Task cancelTask = Task.Delay(Timeout.InfiniteTimeSpan, cts);
                Task completedTask;
                bool hasTimeout = timeout != Timeout.InfiniteTimeSpan;
                Task timeoutTask = Task.CompletedTask;
                if (!hasTimeout)
                {
                    completedTask = await Task.WhenAny(waitForExitTask, cancelTask).ConfigureAwait(false);
                }
                else
                {
                    timeoutTask = Task.Delay(timeout);
                    completedTask = await Task
                        .WhenAny(waitForExitTask, timeoutTask, cancelTask)
                        .ConfigureAwait(false);
                }

                if (completedTask == cancelTask)
                {
                    TryKillProcess(process);
                    throw new OperationCanceledException(cts);
                }

                if (hasTimeout && completedTask == timeoutTask)
                {
                    TryKillProcess(process);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                    _ = await stdoutTask.ConfigureAwait(false);
                    string timeoutErr = await SafeReadProcessErrorAsync(stderrTask).ConfigureAwait(false);
                    return (
                        false,
                        string.IsNullOrWhiteSpace(timeoutErr)
                            ? $"ffmpeg timeout after {timeout.TotalSeconds:0}s"
                            : $"ffmpeg timeout after {timeout.TotalSeconds:0}s, err={timeoutErr}"
                    );
                }

                await waitForExitTask.ConfigureAwait(false);
                _ = await stdoutTask.ConfigureAwait(false);
                string stderr = await SafeReadProcessErrorAsync(stderrTask).ConfigureAwait(false);
                if (process.ExitCode != 0)
                {
                    return (false, $"exit={process.ExitCode}, err={stderr}");
                }

                return (true, "");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private static async Task<(bool ok, string err)> TryCreateTileImageAsync(
            string ffmpegExePath,
            string movieFullPath,
            string startText,
            string vf,
            string outputPath,
            int jpegQuality,
            bool useTolerantInput,
            TimeSpan ffmpegTimeout,
            CancellationToken cts
        )
        {
            ProcessStartInfo psi = new()
            {
                FileName = ffmpegExePath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            if (!useTolerantInput)
            {
                AddHwAccelArguments(psi);
            }
            psi.ArgumentList.Add("-an");
            psi.ArgumentList.Add("-sn");
            psi.ArgumentList.Add("-dn");
            AddInputArguments(psi, movieFullPath, startText, useTolerantInput);
            psi.ArgumentList.Add("-frames:v");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-strict");
            psi.ArgumentList.Add("unofficial");
            psi.ArgumentList.Add("-pix_fmt");
            psi.ArgumentList.Add("yuv420p");
            psi.ArgumentList.Add("-q:v");
            psi.ArgumentList.Add(jpegQuality.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-vf");
            psi.ArgumentList.Add(vf);
            psi.ArgumentList.Add(outputPath);
            return await RunProcessAsync(psi, ffmpegTimeout, cts).ConfigureAwait(false);
        }

        private static async Task<string> SafeReadProcessErrorAsync(Task<string> stderrTask)
        {
            try
            {
                return await stderrTask.ConfigureAwait(false);
            }
            catch
            {
                return "";
            }
        }

        private static void TryKillProcess(Process process)
        {
            try
            {
                if (process != null && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // プロセス競合で殺せなくても、ここで二次障害にしない。
            }
        }

        private static void TryApplyChildProcessPriority(Process process)
        {
            try
            {
                ProcessPriorityClass? priorityClass = ResolveChildProcessPriorityClass();
                if (!priorityClass.HasValue)
                {
                    return;
                }

                process.PriorityClass = priorityClass.Value;
            }
            catch
            {
                // 優先度変更失敗でサムネ生成自体は落とさない。
            }
        }

        private static string ResolveFfmpegExecutablePath()
        {
            string configuredPath = Environment.GetEnvironmentVariable(FfmpegExePathEnvName);
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                string normalizedConfiguredPath = configuredPath.Trim().Trim('"');
                if (File.Exists(normalizedConfiguredPath))
                {
                    return normalizedConfiguredPath;
                }
                if (Directory.Exists(normalizedConfiguredPath))
                {
                    string candidate = Path.Combine(normalizedConfiguredPath, "ffmpeg.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            string baseDir = AppContext.BaseDirectory;
            string[] bundledCandidates =
            [
                Path.Combine(baseDir, "ffmpeg.exe"),
                Path.Combine(baseDir, "ffmpeg", "ffmpeg.exe"),
                Path.Combine(baseDir, "tools", "ffmpeg", "ffmpeg.exe"),
                Path.Combine(baseDir, "runtimes", "win-x64", "native", "ffmpeg.exe"),
                Path.Combine(baseDir, "runtimes", "win-x86", "native", "ffmpeg.exe"),
            ];

            foreach (string candidate in bundledCandidates)
            {
                if (Path.Exists(candidate))
                {
                    return candidate;
                }
            }

            return "ffmpeg";
        }
    }
}
