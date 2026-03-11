using System.Globalization;
using System.IO;
using System.Text;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// Worker と UI fallback で共有する、サムネイル実行ポリシー。
    /// エンジン順、再試行、救済条件の判断をここへ集約する。
    /// </summary>
    internal static class ThumbnailExecutionPolicy
    {
        private const string EngineEnvName = "IMM_THUMB_ENGINE";
        private const string AutogenRetryEnvName = "IMM_THUMB_AUTOGEN_RETRY";
        private const string AutogenRetryDelayMsEnvName = "IMM_THUMB_AUTOGEN_RETRY_DELAY_MS";
        private const int DefaultAutogenRetryCount = 4;
        private const int DefaultAutogenRetryDelayMs = 300;
        private const double InitialLongClipOnePassFallbackThresholdSec = 300d;
        private const string AutogenDemuxImmediateEofKeyword = "autogen demux immediate eof";
        private static readonly HashSet<string> IndexRepairTargetExtensions = new(
            [
                ".mp4",
                ".m4v",
                ".3gp",
                ".3g2",
                ".mov",
                ".avi",
                ".divx",
                ".mkv",
                ".flv",
                ".f4v",
                ".wmv",
                ".asf",
                ".mts",
                ".m2ts",
            ],
            StringComparer.OrdinalIgnoreCase
        );

        private static readonly string[] AutogenTransientRetryKeywords =
        [
            "a generic error occurred in gdi+",
            "no frames decoded",
            "resource temporarily unavailable",
            "cannot allocate memory",
            "timeout",
        ];

        private static readonly string[] FfmpegOnePassSkipKeywords =
        [
            "invalid data found when processing input",
            "moov atom not found",
            "video stream is missing",
        ];

        private static readonly string[] IndexRepairForcedFallbackKeywords =
        [
            "no frames decoded",
            "invalid data found when processing input",
            "video stream not found",
            "failed to open input",
        ];

        private static readonly string[] AutogenImmediateEofFallbackKeywords =
        [
            AutogenDemuxImmediateEofKeyword,
        ];

        public static List<string> BuildEngineOrderIds(
            string selectedEngineId,
            bool isManual,
            bool hasEmojiPath,
            int attemptCount
        )
        {
            List<string> order = [];
            AddEngineId(order, selectedEngineId);

            if (IsForcedEngineMode())
            {
                return order;
            }

            bool isRecoveryLane = attemptCount > 0;
            bool skipOpenCv = hasEmojiPath;

            if (isManual)
            {
                if (string.Equals(selectedEngineId, "ffmediatoolkit", StringComparison.OrdinalIgnoreCase))
                {
                    if (!skipOpenCv)
                    {
                        AddEngineId(order, "opencv");
                    }
                }
                else
                {
                    AddEngineId(order, "ffmediatoolkit");
                }
                return order;
            }

            if (string.Equals(selectedEngineId, "autogen", StringComparison.OrdinalIgnoreCase))
            {
                if (isRecoveryLane)
                {
                    AddEngineId(order, "ffmpeg1pass");
                }
                return order;
            }

            if (
                string.Equals(
                    selectedEngineId,
                    "ffmediatoolkit",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                AddEngineId(order, "autogen");
                AddEngineId(order, "ffmpeg1pass");
                if (!skipOpenCv)
                {
                    AddEngineId(order, "opencv");
                }
                return order;
            }

            if (string.Equals(selectedEngineId, "ffmpeg1pass", StringComparison.OrdinalIgnoreCase))
            {
                return order;
            }

            if (string.Equals(selectedEngineId, "opencv", StringComparison.OrdinalIgnoreCase))
            {
                AddEngineId(order, "autogen");
                AddEngineId(order, "ffmediatoolkit");
                AddEngineId(order, "ffmpeg1pass");
                return order;
            }

            AddEngineId(order, "autogen");
            AddEngineId(order, "ffmediatoolkit");
            AddEngineId(order, "ffmpeg1pass");
            if (!skipOpenCv)
            {
                AddEngineId(order, "opencv");
            }
            return order;
        }

        public static bool ShouldSkipFfmpegOnePassByKnownInvalidInput(
            IReadOnlyList<string> engineErrorMessages
        )
        {
            if (engineErrorMessages == null || engineErrorMessages.Count < 1)
            {
                return false;
            }

            for (int i = 0; i < engineErrorMessages.Count; i++)
            {
                string message = engineErrorMessages[i];
                if (string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                if (ContainsAnyKeyword(message, FfmpegOnePassSkipKeywords))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool ShouldForceIndexRepairAfterEngineFailure(
            IReadOnlyList<string> engineErrorMessages
        )
        {
            if (engineErrorMessages == null || engineErrorMessages.Count < 1)
            {
                return false;
            }

            StringBuilder merged = new();
            for (int i = 0; i < engineErrorMessages.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(engineErrorMessages[i]))
                {
                    continue;
                }

                merged.Append(engineErrorMessages[i]);
                merged.Append(' ');
            }

            string text = merged.ToString().ToLowerInvariant();
            return !string.IsNullOrWhiteSpace(text)
                && ContainsAnyKeyword(text, IndexRepairForcedFallbackKeywords);
        }

        public static bool ShouldTryRecoveryOnePassFallback(
            IReadOnlyList<string> engineErrorMessages
        )
        {
            if (engineErrorMessages == null || engineErrorMessages.Count < 1)
            {
                return false;
            }

            bool hasAutogenRecoverableFailure = false;
            for (int i = 0; i < engineErrorMessages.Count; i++)
            {
                string message = engineErrorMessages[i] ?? "";
                if (message.IndexOf("[autogen]", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (
                        message.IndexOf("no frames decoded", StringComparison.OrdinalIgnoreCase)
                            >= 0
                        || message.IndexOf(
                            "near-black thumbnail",
                            StringComparison.OrdinalIgnoreCase
                        ) >= 0
                    )
                    {
                        hasAutogenRecoverableFailure = true;
                    }
                }

                if (message.IndexOf("[ffmpeg1pass]", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
            }

            return hasAutogenRecoverableFailure;
        }

        public static bool IsIndexRepairTargetMovie(string movieFullPath)
        {
            if (string.IsNullOrWhiteSpace(movieFullPath))
            {
                return false;
            }

            string ext = Path.GetExtension(movieFullPath)?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(ext))
            {
                return false;
            }

            return IndexRepairTargetExtensions.Contains(ext);
        }

        public static bool ShouldTreatAutogenSuccessAsFailure(
            string engineId,
            ThumbnailPreviewFrame previewFrame
        )
        {
            return string.Equals(engineId, "autogen", StringComparison.OrdinalIgnoreCase)
                && IsMostlyBlackPreviewFrame(previewFrame);
        }

        public static bool ShouldForceRepairAfterFailure(
            bool isManual,
            bool isSuccess,
            bool isRecoveryLane,
            bool isIndexRepairTargetMovie,
            bool repairedByProbe,
            IReadOnlyList<string> engineErrorMessages
        )
        {
            return !isManual
                && !isSuccess
                && isRecoveryLane
                && isIndexRepairTargetMovie
                && !repairedByProbe
                && ShouldForceIndexRepairAfterEngineFailure(engineErrorMessages);
        }

        public static bool ShouldTryRecoveryOnePassFallback(
            bool isManual,
            bool isRecoveryLane,
            bool isSuccess,
            string processEngineId,
            double? durationSec,
            IReadOnlyList<string> engineErrorMessages
        )
        {
            bool shouldTryInitialLongClipFallback =
                !isRecoveryLane
                && ShouldTryInitialLongClipOnePassFallback(durationSec, engineErrorMessages);
            bool shouldTryInitialDemuxImmediateEofFallback =
                !isRecoveryLane
                && ShouldTryInitialDemuxImmediateEofOnePassFallback(engineErrorMessages);

            return !isManual
                && !isSuccess
                && string.Equals(processEngineId, "autogen", StringComparison.OrdinalIgnoreCase)
                && (
                    isRecoveryLane
                    || shouldTryInitialLongClipFallback
                    || shouldTryInitialDemuxImmediateEofFallback
                )
                && !ShouldSkipFfmpegOnePassByKnownInvalidInput(engineErrorMessages)
                && ShouldTryRecoveryOnePassFallback(engineErrorMessages);
        }

        public static string ResolveRecoveryOnePassFallbackReason(
            bool isRecoveryLane,
            double? durationSec,
            IReadOnlyList<string> engineErrorMessages
        )
        {
            if (ShouldTryInitialDemuxImmediateEofOnePassFallback(engineErrorMessages))
            {
                return "initial-demux-immediate-eof";
            }

            if (
                !isRecoveryLane
                && ShouldTryInitialLongClipOnePassFallback(durationSec, engineErrorMessages)
            )
            {
                return "initial-longclip-no-frames-decoded";
            }

            return "recovery-no-frames-decoded";
        }

        private static bool ShouldTryInitialLongClipOnePassFallback(
            double? durationSec,
            IReadOnlyList<string> engineErrorMessages
        )
        {
            if (!durationSec.HasValue || durationSec.Value < InitialLongClipOnePassFallbackThresholdSec)
            {
                return false;
            }

            if (engineErrorMessages == null || engineErrorMessages.Count < 1)
            {
                return false;
            }

            for (int i = 0; i < engineErrorMessages.Count; i++)
            {
                string message = engineErrorMessages[i] ?? "";
                if (
                    message.IndexOf("[autogen]", StringComparison.OrdinalIgnoreCase) >= 0
                    && message.IndexOf("no frames decoded", StringComparison.OrdinalIgnoreCase) >= 0
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldTryInitialDemuxImmediateEofOnePassFallback(
            IReadOnlyList<string> engineErrorMessages
        )
        {
            if (engineErrorMessages == null || engineErrorMessages.Count < 1)
            {
                return false;
            }

            for (int i = 0; i < engineErrorMessages.Count; i++)
            {
                string message = engineErrorMessages[i] ?? "";
                if (
                    message.IndexOf("[autogen]", StringComparison.OrdinalIgnoreCase) >= 0
                    && ContainsAnyKeyword(message, AutogenImmediateEofFallbackKeywords)
                )
                {
                    return true;
                }
            }

            return false;
        }

        public static bool ShouldTryOriginalOnePassAfterRepairFailure(
            bool isManual,
            bool repairedByProbe,
            bool isSuccess,
            string processEngineId,
            string ffmpegOnePassEngineId,
            IReadOnlyList<string> engineErrorMessages
        )
        {
            return !isManual
                && repairedByProbe
                && !isSuccess
                && !string.Equals(
                    processEngineId,
                    ffmpegOnePassEngineId,
                    StringComparison.OrdinalIgnoreCase
                )
                && !ShouldSkipFfmpegOnePassByKnownInvalidInput(engineErrorMessages);
        }

        public static bool ShouldSkipFailurePlaceholder(
            bool isManual,
            bool isSuccess,
            bool isRecoveryLane
        )
        {
            return !isManual && !isSuccess && !isRecoveryLane;
        }

        public static string ResolveFailurePlaceholderSkipReason(bool isIndexRepairTargetMovie)
        {
            return isIndexRepairTargetMovie
                ? "initial-index-repair-target"
                : "initial-autogen-failure-retry";
        }

        public static bool IsForcedEngineMode()
        {
            string mode = Environment.GetEnvironmentVariable(EngineEnvName)?.Trim() ?? "";
            return !string.IsNullOrWhiteSpace(mode)
                && !string.Equals(mode, "auto", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsAutogenRetryEnabled()
        {
            string mode = Environment.GetEnvironmentVariable(AutogenRetryEnvName)?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(mode))
            {
                return true;
            }

            string normalized = mode.ToLowerInvariant();
            return normalized is "1" or "true" or "on" or "yes" or "auto";
        }

        public static int ResolveAutogenRetryCount()
        {
            return DefaultAutogenRetryCount;
        }

        public static int ResolveAutogenRetryDelayMs()
        {
            string raw =
                Environment.GetEnvironmentVariable(AutogenRetryDelayMsEnvName)?.Trim() ?? "";
            if (
                !string.IsNullOrWhiteSpace(raw)
                && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            )
            {
                if (parsed < 0)
                {
                    return 0;
                }
                if (parsed > 5000)
                {
                    return 5000;
                }
                return parsed;
            }
            return DefaultAutogenRetryDelayMs;
        }

        public static bool IsAutogenTransientRetryError(string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                return false;
            }

            string normalized = errorMessage.ToLowerInvariant();
            for (int i = 0; i < AutogenTransientRetryKeywords.Length; i++)
            {
                if (normalized.Contains(AutogenTransientRetryKeywords[i]))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsRecoveryLaneAttempt(QueueObj queueObj)
        {
            return queueObj?.AttemptCount > 0;
        }

        // 真っ黒成功を拾ってしまうケースだけを落とすため、かなり保守的な閾値で判定する。
        private static bool IsMostlyBlackPreviewFrame(ThumbnailPreviewFrame previewFrame)
        {
            if (previewFrame == null || !previewFrame.IsValid())
            {
                return false;
            }

            int bytesPerPixel = previewFrame.PixelFormat switch
            {
                ThumbnailPreviewPixelFormat.Bgr24 => 3,
                ThumbnailPreviewPixelFormat.Bgra32 => 4,
                _ => 0,
            };
            if (bytesPerPixel < 3)
            {
                return false;
            }

            int brightPixelCount = 0;
            int totalPixelCount = 0;
            byte[] pixelBytes = previewFrame.PixelBytes;
            for (int y = 0; y < previewFrame.Height; y++)
            {
                int rowStart = y * previewFrame.Stride;
                for (int x = 0; x < previewFrame.Width; x++)
                {
                    int offset = rowStart + (x * bytesPerPixel);
                    if (offset + 2 >= pixelBytes.Length)
                    {
                        break;
                    }

                    totalPixelCount++;
                    int maxChannel = Math.Max(
                        pixelBytes[offset],
                        Math.Max(pixelBytes[offset + 1], pixelBytes[offset + 2])
                    );
                    if (maxChannel >= 18)
                    {
                        brightPixelCount++;
                    }
                }
            }

            if (totalPixelCount < 1)
            {
                return false;
            }

            return brightPixelCount <= Math.Max(1, totalPixelCount / 200);
        }

        private static bool ContainsAnyKeyword(string text, IReadOnlyList<string> keywords)
        {
            if (string.IsNullOrWhiteSpace(text) || keywords == null)
            {
                return false;
            }

            for (int i = 0; i < keywords.Count; i++)
            {
                string keyword = keywords[i];
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddEngineId(List<string> order, string engineId)
        {
            if (string.IsNullOrWhiteSpace(engineId))
            {
                return;
            }

            for (int i = 0; i < order.Count; i++)
            {
                if (string.Equals(order[i], engineId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            order.Add(engineId);
        }
    }
}
