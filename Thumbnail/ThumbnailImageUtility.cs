using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using IndigoMovieManager.Thumbnail.Decoders;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイルの画像処理と ThumbInfo 組み立てをまとめる。
    /// engine と service が共通で使う画像系 helper をここへ寄せる。
    /// </summary>
    internal static class ThumbnailImageUtility
    {
        private const string JpegSaveParallelEnvName = "IMM_THUMB_JPEG_SAVE_PARALLEL";
        private const int DefaultJpegSaveParallel = 4;
        private const int MaxJpegSaveRetryCount = 3;
        private const int BaseJpegSaveRetryDelayMs = 60;
        private static readonly SemaphoreSlim JpegSaveGate = CreateJpegSaveGate();

        // エンジン内部で得たBitmapを、UI非依存のプレビューDTOへ詰め替える。
        public static ThumbnailPreviewFrame CreatePreviewFrameFromBitmap(
            Bitmap source,
            int maxHeight = 120
        )
        {
            if (source == null || source.Width < 1 || source.Height < 1)
            {
                return null;
            }

            Size scaledSize = ResolvePreviewTargetSize(source.Size, maxHeight);
            using Bitmap normalized = new(
                scaledSize.Width,
                scaledSize.Height,
                PixelFormat.Format24bppRgb
            );
            using (Graphics g = Graphics.FromImage(normalized))
            {
                g.Clear(Color.Black);
                g.DrawImage(source, 0, 0, scaledSize.Width, scaledSize.Height);
            }

            BitmapData bitmapData = null;
            try
            {
                bitmapData = normalized.LockBits(
                    new Rectangle(0, 0, normalized.Width, normalized.Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format24bppRgb
                );
                int stride = bitmapData.Stride;
                if (stride < 1)
                {
                    return null;
                }

                int pixelByteLength = stride * normalized.Height;
                if (pixelByteLength < 1)
                {
                    return null;
                }

                byte[] pixelBytes = new byte[pixelByteLength];
                Marshal.Copy(bitmapData.Scan0, pixelBytes, 0, pixelByteLength);
                return new ThumbnailPreviewFrame
                {
                    PixelBytes = pixelBytes,
                    Width = normalized.Width,
                    Height = normalized.Height,
                    Stride = stride,
                    PixelFormat = ThumbnailPreviewPixelFormat.Bgr24,
                };
            }
            catch
            {
                return null;
            }
            finally
            {
                if (bitmapData != null)
                {
                    normalized.UnlockBits(bitmapData);
                }
            }
        }

        // 1秒未満～1秒付近の短尺でも拾えるよう、前方へしぶとく探索する。
        public static bool TryReadFrameWithRetry(
            IThumbnailFrameSource frameSource,
            TimeSpan baseTime,
            out Bitmap frameBitmap
        )
        {
            frameBitmap = null;
            if (frameSource == null)
            {
                return false;
            }

            for (int i = 0; i <= 100; i++)
            {
                TimeSpan tryTime = baseTime + TimeSpan.FromMilliseconds(i * 100);
                if (tryTime < TimeSpan.Zero)
                {
                    tryTime = TimeSpan.Zero;
                }

                if (frameSource.TryReadFrame(tryTime, out frameBitmap))
                {
                    return true;
                }
            }

            if (baseTime <= TimeSpan.FromSeconds(1))
            {
                for (int ms = 0; ms <= 1000; ms += 33)
                {
                    if (frameSource.TryReadFrame(TimeSpan.FromMilliseconds(ms), out frameBitmap))
                    {
                        return true;
                    }
                }
            }

            frameBitmap?.Dispose();
            frameBitmap = null;
            return false;
        }

        // 動画末尾超えを避けるための安全な最大秒を返す。
        public static int ResolveSafeMaxCaptureSec(double durationSec)
        {
            if (durationSec <= 0 || double.IsNaN(durationSec) || double.IsInfinity(durationSec))
            {
                return 0;
            }

            double safeEnd = Math.Max(0, durationSec - 0.001);
            return Math.Max(0, (int)Math.Floor(safeEnd));
        }

        // 動画の時間と分割数から自動サムネイル用の秒数配列を作る。
        public static ThumbInfo BuildAutoThumbInfo(TabInfo tabInfo, double? durationSec)
        {
            int thumbCount = tabInfo.Columns * tabInfo.Rows;
            int divideSec = 1;
            int maxCaptureSec = int.MaxValue;
            if (durationSec.HasValue && durationSec.Value > 0)
            {
                divideSec = (int)(durationSec.Value / (thumbCount + 1));
                if (divideSec < 1)
                {
                    divideSec = 1;
                }

                maxCaptureSec = ResolveSafeMaxCaptureSec(durationSec.Value);
            }

            ThumbInfo thumbInfo = new()
            {
                ThumbWidth = tabInfo.Width,
                ThumbHeight = tabInfo.Height,
                ThumbRows = tabInfo.Rows,
                ThumbColumns = tabInfo.Columns,
                ThumbCounts = thumbCount,
            };

            for (int i = 1; i < thumbInfo.ThumbCounts + 1; i++)
            {
                int sec = i * divideSec;
                if (sec > maxCaptureSec)
                {
                    sec = maxCaptureSec;
                }
                thumbInfo.Add(sec);
            }

            thumbInfo.NewThumbInfo();
            WriteAutoThumbInfoDebugLog(tabInfo, durationSec, divideSec, maxCaptureSec, thumbInfo);
            return thumbInfo;
        }

        // SWFは代表1枚運用のため、同じ秒数を全コマへ複製する。
        public static ThumbInfo BuildSwfThumbInfo(TabInfo tabInfo, double requestedCaptureSec)
        {
            int thumbCount = Math.Max(1, tabInfo.Columns * tabInfo.Rows);
            int captureSec = Math.Max(0, (int)Math.Floor(requestedCaptureSec));
            ThumbInfo thumbInfo = new()
            {
                ThumbWidth = tabInfo.Width,
                ThumbHeight = tabInfo.Height,
                ThumbRows = tabInfo.Rows,
                ThumbColumns = tabInfo.Columns,
                ThumbCounts = thumbCount,
            };

            for (int i = 0; i < thumbCount; i++)
            {
                thumbInfo.Add(captureSec);
            }

            thumbInfo.NewThumbInfo();
            return thumbInfo;
        }

        // 既存互換の4:3中央トリミング矩形を返す。
        public static Rectangle GetAspectRect(int imgWidth, int imgHeight)
        {
            int w = imgWidth;
            int h = imgHeight;
            int wdiff = 0;
            int hdiff = 0;

            float aspect = (float)imgWidth / imgHeight;
            if (aspect > 1.34f)
            {
                h = (int)Math.Floor((decimal)imgHeight / 3);
                w = (int)Math.Floor((decimal)h * 4);
                h = imgHeight;
                wdiff = (imgWidth - w) / 2;
                hdiff = 0;
            }

            if (aspect < 1.33f)
            {
                w = (int)Math.Floor((decimal)imgWidth / 4);
                h = (int)Math.Floor((decimal)w * 3);
                w = imgWidth;
                hdiff = (imgHeight - h) / 2;
                wdiff = 0;
            }
            return new Rectangle(wdiff, hdiff, w, h);
        }

        public static Size ResolveDefaultTargetSize(Bitmap source)
        {
            int width = source.Width < 320 ? source.Width : 320;
            int height = source.Height < 240 ? source.Height : 240;

            if (width <= 0)
            {
                width = 320;
            }
            if (height <= 0)
            {
                height = 240;
            }
            return new Size(width, height);
        }

        public static Bitmap CropBitmap(Bitmap source, Rectangle cropRect)
        {
            Rectangle bounded = Rectangle.Intersect(
                new Rectangle(0, 0, source.Width, source.Height),
                cropRect
            );
            if (bounded.Width <= 0 || bounded.Height <= 0)
            {
                bounded = new Rectangle(0, 0, source.Width, source.Height);
            }

            Bitmap cropped = new(bounded.Width, bounded.Height, PixelFormat.Format24bppRgb);
            using Graphics g = Graphics.FromImage(cropped);
            g.DrawImage(
                source,
                new Rectangle(0, 0, bounded.Width, bounded.Height),
                bounded,
                GraphicsUnit.Pixel
            );
            return cropped;
        }

        public static Bitmap ResizeBitmap(Bitmap source, Size targetSize)
        {
            Bitmap resized = new(targetSize.Width, targetSize.Height, PixelFormat.Format24bppRgb);
            using Graphics g = Graphics.FromImage(resized);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(source, new Rectangle(0, 0, targetSize.Width, targetSize.Height));
            return resized;
        }

        // 集めたフレームを既存形式のタイルJPEGへまとめる。
        public static bool SaveCombinedThumbnail(
            string saveThumbFileName,
            IReadOnlyList<Bitmap> frames,
            int columns,
            int rows
        )
        {
            if (frames.Count < 1)
            {
                return false;
            }

            int total = Math.Min(frames.Count, columns * rows);
            int frameWidth = frames[0].Width;
            int frameHeight = frames[0].Height;
            if (frameWidth <= 0 || frameHeight <= 0)
            {
                return false;
            }

            string saveDir = Path.GetDirectoryName(saveThumbFileName) ?? "";
            if (!string.IsNullOrWhiteSpace(saveDir))
            {
                Directory.CreateDirectory(saveDir);
            }

            using Bitmap canvas = new(
                frameWidth * columns,
                frameHeight * rows,
                PixelFormat.Format24bppRgb
            );
            using Graphics g = Graphics.FromImage(canvas);
            g.Clear(Color.Black);

            for (int i = 0; i < total; i++)
            {
                int r = i / columns;
                int c = i % columns;
                Rectangle destRect = new(c * frameWidth, r * frameHeight, frameWidth, frameHeight);
                g.DrawImage(frames[i], destRect);
            }

            try
            {
                return TrySaveJpegWithRetry(canvas, saveThumbFileName, out _);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"thumb save failed: path='{saveThumbFileName}', err={ex.Message}");
                return false;
            }
        }

        // 救済経路で実際に使った秒列へ合わせて、保存用の ThumbInfo を組み直す。
        public static ThumbInfo RebuildThumbInfoWithCaptureSeconds(
            ThumbInfo source,
            IReadOnlyList<double> captureSeconds
        )
        {
            if (source == null)
            {
                return null;
            }

            ThumbInfo rebuilt = new()
            {
                ThumbCounts = source.ThumbCounts,
                ThumbWidth = source.ThumbWidth,
                ThumbHeight = source.ThumbHeight,
                ThumbColumns = source.ThumbColumns,
                ThumbRows = source.ThumbRows,
            };

            int desiredCount = Math.Max(1, source.ThumbCounts);
            List<int> normalizedSeconds = [];
            if (captureSeconds != null)
            {
                for (int i = 0; i < captureSeconds.Count; i++)
                {
                    double sec = captureSeconds[i];
                    if (double.IsNaN(sec) || double.IsInfinity(sec))
                    {
                        continue;
                    }

                    normalizedSeconds.Add(Math.Max(0, (int)Math.Floor(sec)));
                }
            }

            if (normalizedSeconds.Count < 1 && source.ThumbSec != null)
            {
                normalizedSeconds.AddRange(source.ThumbSec);
            }

            if (normalizedSeconds.Count < 1)
            {
                normalizedSeconds.Add(0);
            }

            while (normalizedSeconds.Count < desiredCount)
            {
                normalizedSeconds.Add(normalizedSeconds[normalizedSeconds.Count - 1]);
            }

            if (normalizedSeconds.Count > desiredCount)
            {
                normalizedSeconds.RemoveRange(desiredCount, normalizedSeconds.Count - desiredCount);
            }

            foreach (int sec in normalizedSeconds)
            {
                rebuilt.Add(sec);
            }

            rebuilt.NewThumbInfo();
            return rebuilt;
        }

        // JPEG保存時の一時エラーを吸収しつつ、壊れた中間ファイルを残さない。
        public static bool TrySaveJpegWithRetry(
            Image image,
            string savePath,
            out string errorMessage
        )
        {
            errorMessage = "";
            if (image == null)
            {
                errorMessage = "image is null";
                return false;
            }

            if (string.IsNullOrWhiteSpace(savePath))
            {
                errorMessage = "save path is empty";
                return false;
            }

            string saveDir = Path.GetDirectoryName(savePath) ?? "";
            if (!string.IsNullOrWhiteSpace(saveDir))
            {
                Directory.CreateDirectory(saveDir);
            }

            Exception lastError = null;
            JpegSaveGate.Wait();
            try
            {
                for (int attempt = 1; attempt <= MaxJpegSaveRetryCount; attempt++)
                {
                    string tempPath = BuildTempJpegPath(savePath, attempt);
                    try
                    {
                        using (FileStream fs = new(
                            tempPath,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None
                        ))
                        {
                            image.Save(fs, ImageFormat.Jpeg);
                            fs.Flush(true);
                        }

                        ReplaceFileAtomically(tempPath, savePath);
                        if (attempt > 1)
                        {
                            ThumbnailRuntimeLog.Write(
                                "thumbnail",
                                $"jpeg save recovered after retry: attempt={attempt}, path='{savePath}'"
                            );
                        }
                        return true;
                    }
                    catch (Exception ex) when (IsTransientJpegSaveError(ex))
                    {
                        lastError = ex;
                        TryDeleteFileQuietly(tempPath);
                        if (attempt >= MaxJpegSaveRetryCount)
                        {
                            break;
                        }

                        Thread.Sleep(BaseJpegSaveRetryDelayMs * attempt);
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        TryDeleteFileQuietly(tempPath);
                        break;
                    }
                }
            }
            finally
            {
                JpegSaveGate.Release();
            }

            errorMessage = lastError?.Message ?? "jpeg save failed";
            ThumbnailRuntimeLog.Write(
                "thumbnail",
                $"jpeg save failed: path='{savePath}', reason='{errorMessage}'"
            );
            return false;
        }

        // ミニパネル用途で過剰メモリを避けるため、上限高さだけ抑えて等比縮小する。
        private static Size ResolvePreviewTargetSize(Size sourceSize, int maxHeight)
        {
            if (sourceSize.Width < 1 || sourceSize.Height < 1)
            {
                return new Size(1, 1);
            }

            int safeMaxHeight = maxHeight < 1 ? sourceSize.Height : maxHeight;
            if (sourceSize.Height <= safeMaxHeight)
            {
                return sourceSize;
            }

            double scale = (double)safeMaxHeight / sourceSize.Height;
            int width = Math.Max(1, (int)Math.Round(sourceSize.Width * scale));
            return new Size(width, safeMaxHeight);
        }

        private static SemaphoreSlim CreateJpegSaveGate()
        {
            int parallel = DefaultJpegSaveParallel;
            string raw = Environment.GetEnvironmentVariable(JpegSaveParallelEnvName);
            if (
                !string.IsNullOrWhiteSpace(raw)
                && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            )
            {
                parallel = Math.Clamp(parsed, 1, 32);
            }

            return new SemaphoreSlim(parallel, parallel);
        }

        private static string BuildTempJpegPath(string savePath, int attempt)
        {
            string fileName = Path.GetFileName(savePath);
            string tempFileName =
                $"{fileName}.tmp.{Environment.ProcessId}.{Thread.CurrentThread.ManagedThreadId}.{attempt}.{Guid.NewGuid():N}";
            string dir = Path.GetDirectoryName(savePath) ?? "";
            return Path.Combine(dir, tempFileName);
        }

        private static void ReplaceFileAtomically(string tempPath, string savePath)
        {
            if (Path.Exists(savePath))
            {
                File.Replace(tempPath, savePath, null, true);
                return;
            }

            File.Move(tempPath, savePath);
        }

        private static bool IsTransientJpegSaveError(Exception ex)
        {
            return ex is ExternalException || ex is IOException || ex is UnauthorizedAccessException;
        }

        private static void TryDeleteFileQuietly(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
                // 一時ファイル削除失敗は後続処理を優先する。
            }
        }

        // AVI系の自動秒割りがどう決まったかを残し、外部持ち込み値との切り分けに使う。
        private static void WriteAutoThumbInfoDebugLog(
            TabInfo tabInfo,
            double? durationSec,
            int divideSec,
            int maxCaptureSec,
            ThumbInfo thumbInfo
        )
        {
            if (tabInfo == null || !ShouldWriteAutoThumbInfoDebugLog())
            {
                return;
            }

            string thumbSecText =
                thumbInfo?.ThumbSec == null ? "" : string.Join(",", thumbInfo.ThumbSec);
            ThumbnailRuntimeLog.Write(
                "thumbinfo-auto",
                $"tab={tabInfo.Width}x{tabInfo.Height} rows={tabInfo.Rows} cols={tabInfo.Columns} duration_sec={durationSec:0.###} divide_sec={divideSec} max_capture_sec={maxCaptureSec} thumb_sec=[{thumbSecText}]"
            );
        }

        private static bool ShouldWriteAutoThumbInfoDebugLog()
        {
            return true;
        }
    }
}
