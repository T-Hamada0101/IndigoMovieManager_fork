using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using IndigoMovieManager.Properties;

namespace IndigoMovieManager
{
    // Release実機でも体感テンポの支配要因を追えるよう、入口は常に残して設定と絞り込みで制御する。
    internal static class DebugRuntimeLog
    {
        private static readonly object LogLock = new();
        private static readonly object QuietLogLock = new();
        private const string LogPathOverrideEnvironmentVariable =
            "INDIGO_DEBUG_RUNTIME_LOG_PATH";
        private const long MaxLogFileBytes = 20 * 1024 * 1024;
        private const int ReleaseWatchLogThrottleMilliseconds = 1200;
        private const int NoisyWatchRepairLogThrottleMilliseconds = 1500;
        private static readonly HashSet<string> ReleaseMinimalCategories = new(
            new[] { "watch-check", "ui-tempo" },
            StringComparer.OrdinalIgnoreCase
        );
        private static readonly string[] AlwaysThrottledWatchMessagePrefixes =
        [
            "repair view by existing-db-movie:",
            "refresh filtered-view by existing-db-movie:",
        ];
        private static readonly HashSet<string> ReleaseMinimalWatchKeywords = new(
            new[] { "fail", "error", "exception", "shutdown", "critical", "recovery" },
            StringComparer.OrdinalIgnoreCase
        );
        private static readonly bool IsReleaseLikeLoggingMode =
            !Debugger.IsAttached
            || string.Equals(
                Environment.GetEnvironmentVariable("INDIGO_RELEASE_LOG_MODE"),
                "1",
                StringComparison.OrdinalIgnoreCase
            );
        private static readonly Dictionary<string, DateTime> ReleaseLastWriteUtcByEvent = new(
            StringComparer.OrdinalIgnoreCase
        );
        private static readonly Dictionary<string, DateTime> AlwaysThrottleLastWriteUtcByEvent =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly AsyncLocal<string> AmbientScopeText = new();
        private static readonly AsyncLocal<DebugRuntimeLogScopeMetrics> AmbientScopeMetrics = new();
        private static long _logSequence;

        internal static void Write(string category, string message)
        {
            if (!ShouldWrite(category, message, DateTime.UtcNow))
            {
                return;
            }

            string line = BuildLineForTesting(
                DateTime.Now,
                category,
                message,
                Interlocked.Increment(ref _logSequence),
                AmbientScopeText.Value
            );
            Debug.WriteLine(line);

            try
            {
                // VS出力だけで追いにくいケースに備え、同じ内容をファイルにも追記する。
                string defaultLogPath = Path.Combine(
                    AppLocalDataPaths.LogsPath,
                    "debug-runtime.log"
                );
                string requestedLogPath = Environment.GetEnvironmentVariable(
                    LogPathOverrideEnvironmentVariable
                );
                string resolvedLogPath = ResolveLogPath(requestedLogPath, defaultLogPath);
                string logDir =
                    Path.GetDirectoryName(resolvedLogPath) ?? AppLocalDataPaths.LogsPath;
                Directory.CreateDirectory(logDir);
                string logPath = IndigoMovieManager.Thumbnail.LogFileTimeWindowSeparator.PrepareForWrite(
                    resolvedLogPath,
                    MaxLogFileBytes
                );

                lock (LogLock)
                {
                    // 上限超過時は同日でも退避して、次の追記を継続できるようにする。
                    File.AppendAllText(logPath, line + Environment.NewLine);
                }
            }
            catch
            {
                // ログ失敗で本体処理を止めない。
            }
        }

        internal static bool ShouldWriteForCurrentProcess(
            string category,
            string message,
            DateTime utcNow
        )
        {
            return ShouldWrite(category, message, utcNow);
        }

        internal static string ResolveLogPathForTesting(
            string requestedLogPath,
            string defaultLogPath
        )
        {
            return ResolveLogPath(requestedLogPath, defaultLogPath);
        }

        private static string ResolveLogPath(string requestedLogPath, string defaultLogPath)
        {
            // 子プロセスだけ監査ログを分離し、通常起動時は従来の保存先を維持する。
            if (
                string.IsNullOrWhiteSpace(requestedLogPath)
                || !Path.IsPathFullyQualified(requestedLogPath)
            )
            {
                return defaultLogPath;
            }

            return requestedLogPath;
        }

        internal static void ResetThrottleStateForTests()
        {
            lock (QuietLogLock)
            {
                ReleaseLastWriteUtcByEvent.Clear();
                AlwaysThrottleLastWriteUtcByEvent.Clear();
            }

            Interlocked.Exchange(ref _logSequence, 0);
            AmbientScopeText.Value = "";
            AmbientScopeMetrics.Value = null;
        }

        internal static IDisposable BeginScopeForCurrentAsyncFlow(string scopeText)
        {
            string normalizedScopeText = NormalizeInlineText(scopeText);
            if (string.IsNullOrWhiteSpace(normalizedScopeText))
            {
                return DebugRuntimeLogScope.Empty;
            }

            string previousScopeText = AmbientScopeText.Value ?? "";
            DebugRuntimeLogScopeMetrics previousScopeMetrics = AmbientScopeMetrics.Value;
            AmbientScopeText.Value = string.IsNullOrWhiteSpace(previousScopeText)
                ? normalizedScopeText
                : $"{previousScopeText} {normalizedScopeText}";
            AmbientScopeMetrics.Value ??= new DebugRuntimeLogScopeMetrics();
            return new DebugRuntimeLogScope(previousScopeText, previousScopeMetrics);
        }

        internal static string GetAmbientScopeTextForTesting()
        {
            return AmbientScopeText.Value ?? "";
        }

        internal static string GetCurrentScopeText()
        {
            return AmbientScopeText.Value ?? "";
        }

        internal static void RecordCatalogCacheHit()
        {
            AmbientScopeMetrics.Value?.RecordCatalogCacheHit();
        }

        internal static void RecordCatalogCacheMiss()
        {
            AmbientScopeMetrics.Value?.RecordCatalogCacheMiss();
        }

        internal static void RecordCatalogLoadCore(int reusedCount, int skippedCount)
        {
            AmbientScopeMetrics.Value?.RecordCatalogLoadCore(reusedCount, skippedCount);
        }

        internal static void RecordCatalogSignatureElapsed(double elapsedMilliseconds)
        {
            AmbientScopeMetrics.Value?.RecordCatalogSignatureElapsed(elapsedMilliseconds);
        }

        internal static void RecordCatalogLoadElapsed(double elapsedMilliseconds)
        {
            AmbientScopeMetrics.Value?.RecordCatalogLoadElapsed(elapsedMilliseconds);
        }

        internal static void RecordSkinDbPersistQueued()
        {
            AmbientScopeMetrics.Value?.RecordSkinDbPersistQueued();
        }

        internal static void RecordSkinDbPersistFallbackApplied()
        {
            AmbientScopeMetrics.Value?.RecordSkinDbPersistFallbackApplied();
        }

        internal static void RecordSkinNavigateAttempted()
        {
            AmbientScopeMetrics.Value?.RecordSkinNavigateAttempted();
        }

        internal static void RecordSkinNavigateSucceeded()
        {
            AmbientScopeMetrics.Value?.RecordSkinNavigateSucceeded();
        }

        internal static void RecordSkinNavigateFailed()
        {
            AmbientScopeMetrics.Value?.RecordSkinNavigateFailed();
        }

        internal static void RecordSkinNavigateSkipped()
        {
            AmbientScopeMetrics.Value?.RecordSkinNavigateSkipped();
        }

        internal static void RecordSkinRefreshStaleSkipped()
        {
            AmbientScopeMetrics.Value?.RecordSkinRefreshStaleSkipped();
        }

        internal static void RecordSkinRefreshTeardownSkipped()
        {
            AmbientScopeMetrics.Value?.RecordSkinRefreshTeardownSkipped();
        }

        internal static string BuildCurrentScopeMetricSummary(bool includeZeroValues = false)
        {
            return AmbientScopeMetrics.Value?.BuildSummaryText(includeZeroValues) ?? "";
        }

        internal static string BuildLineForTesting(
            DateTime localNow,
            string category,
            string message,
            long sequence,
            string scopeText = ""
        )
        {
            // ログは1行で追える方が見返しやすいので、改行やタブはここで潰しておく。
            string normalizedCategory = NormalizeInlineText(category);
            string normalizedMessage = ComposeScopedMessage(message, scopeText);
            return $"{localNow:yyyy-MM-dd HH:mm:ss.fff} #{sequence:D6} [{normalizedCategory}] {normalizedMessage}";
        }

        private static bool ShouldWrite(string category, string message, DateTime utcNow)
        {
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            // まずカテゴリ単位のON/OFFを見て、不要なログは入口で落とす。
            if (!IsCategoryEnabled(category))
            {
                return false;
            }

            if (TryBuildAlwaysThrottledWatchBucket(category, message, out string alwaysBucket))
            {
                return !IsLogThrottled(
                    alwaysBucket,
                    utcNow,
                    NoisyWatchRepairLogThrottleMilliseconds,
                    AlwaysThrottleLastWriteUtcByEvent
                );
            }

            if (!IsReleaseLikeLoggingMode || !IsReleaseMinimalCategory(category))
            {
                return true;
            }

            if (ReleaseMinimalWatchKeywords.Any(keyword => message.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }

            string bucket = BuildLogBucket(category, message);
            return !IsLogThrottled(
                bucket,
                utcNow,
                ReleaseWatchLogThrottleMilliseconds,
                ReleaseLastWriteUtcByEvent
            );
        }

        private static bool IsReleaseMinimalCategory(string category)
        {
            return ReleaseMinimalCategories.Contains(category);
        }

        private static bool IsCategoryEnabled(string category)
        {
            return ResolveToggleGroup(category) switch
            {
                DebugRuntimeLogToggleGroup.Watch => Settings.Default.DebugLogWatchEnabled,
                DebugRuntimeLogToggleGroup.Queue => Settings.Default.DebugLogQueueEnabled,
                DebugRuntimeLogToggleGroup.Thumbnail => Settings.Default.DebugLogThumbnailEnabled,
                DebugRuntimeLogToggleGroup.Ui => Settings.Default.DebugLogUiEnabled,
                DebugRuntimeLogToggleGroup.Skin => Settings.Default.DebugLogSkinEnabled,
                DebugRuntimeLogToggleGroup.DebugTool => Settings.Default.DebugLogDebugToolEnabled,
                DebugRuntimeLogToggleGroup.Database => Settings.Default.DebugLogDatabaseEnabled,
                _ => Settings.Default.DebugLogOtherEnabled,
            };
        }

        private static DebugRuntimeLogToggleGroup ResolveToggleGroup(string category)
        {
            string normalized = (category ?? "").Trim().ToLowerInvariant();
            if (normalized.Length == 0)
            {
                return DebugRuntimeLogToggleGroup.Other;
            }

            if (normalized.StartsWith("watch", StringComparison.Ordinal))
            {
                return DebugRuntimeLogToggleGroup.Watch;
            }

            if (normalized.StartsWith("queue", StringComparison.Ordinal))
            {
                return DebugRuntimeLogToggleGroup.Queue;
            }

            if (normalized.StartsWith("thumbnail", StringComparison.Ordinal))
            {
                return DebugRuntimeLogToggleGroup.Thumbnail;
            }

            if (
                normalized.StartsWith("ui-", StringComparison.Ordinal)
                || normalized is "lifecycle"
                || normalized is "layout"
                || normalized is "player"
                || normalized is "kana"
                || normalized is "overlay"
                || normalized is "task"
                || normalized is "task-start"
                || normalized is "task-end"
            )
            {
                return DebugRuntimeLogToggleGroup.Ui;
            }

            if (normalized.StartsWith("skin", StringComparison.Ordinal))
            {
                return DebugRuntimeLogToggleGroup.Skin;
            }

            if (
                normalized.StartsWith("debug", StringComparison.Ordinal)
                || normalized is "log-tab"
            )
            {
                return DebugRuntimeLogToggleGroup.DebugTool;
            }

            if (
                normalized.StartsWith("db", StringComparison.Ordinal)
                || normalized is "sinku"
            )
            {
                return DebugRuntimeLogToggleGroup.Database;
            }

            return DebugRuntimeLogToggleGroup.Other;
        }

        private static bool TryBuildAlwaysThrottledWatchBucket(
            string category,
            string message,
            out string bucket
        )
        {
            bucket = "";
            if (!string.Equals(category, "watch-check", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string trimmed = message.Trim();
            foreach (string prefix in AlwaysThrottledWatchMessagePrefixes)
            {
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    bucket = BuildLogBucket(category, trimmed);
                    return true;
                }
            }

            return false;
        }

        // request 単位の trace を async フローへぶら下げ、別カテゴリでも同じ流れを追えるようにする。
        private static string ComposeScopedMessage(string message, string scopeText)
        {
            string normalizedMessage = NormalizeInlineText(message);
            string normalizedScopeText = NormalizeInlineText(scopeText);
            if (string.IsNullOrWhiteSpace(normalizedScopeText))
            {
                return normalizedMessage;
            }

            if (string.IsNullOrWhiteSpace(normalizedMessage))
            {
                return normalizedScopeText;
            }

            return $"{normalizedScopeText} {normalizedMessage}";
        }

        private sealed class DebugRuntimeLogScope : IDisposable
        {
            internal static readonly DebugRuntimeLogScope Empty = new("");

            private readonly string _previousScopeText;
            private readonly DebugRuntimeLogScopeMetrics _previousScopeMetrics;
            private int _disposed;

            internal DebugRuntimeLogScope(string previousScopeText, DebugRuntimeLogScopeMetrics previousScopeMetrics = null)
            {
                _previousScopeText = previousScopeText ?? "";
                _previousScopeMetrics = previousScopeMetrics;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                AmbientScopeText.Value = _previousScopeText;
                AmbientScopeMetrics.Value = _previousScopeMetrics;
            }
        }

        private sealed class DebugRuntimeLogScopeMetrics
        {
            private int _catalogCacheHitCount;
            private int _catalogCacheMissCount;
            private int _catalogReusedCount;
            private int _catalogSkippedCount;
            private double _catalogSignatureElapsedMilliseconds;
            private double _catalogLoadElapsedMilliseconds;
            private int _skinDbPersistQueuedCount;
            private int _skinDbPersistFallbackAppliedCount;
            private int _skinNavigateAttemptedCount;
            private int _skinNavigateSucceededCount;
            private int _skinNavigateFailedCount;
            private int _skinNavigateSkippedCount;
            private int _skinRefreshStaleSkippedCount;
            private int _skinRefreshTeardownSkippedCount;

            internal void RecordCatalogCacheHit()
            {
                _catalogCacheHitCount++;
            }

            internal void RecordCatalogCacheMiss()
            {
                _catalogCacheMissCount++;
            }

            internal void RecordCatalogLoadCore(int reusedCount, int skippedCount)
            {
                _catalogReusedCount += Math.Max(0, reusedCount);
                _catalogSkippedCount += Math.Max(0, skippedCount);
            }

            internal void RecordCatalogSignatureElapsed(double elapsedMilliseconds)
            {
                _catalogSignatureElapsedMilliseconds += Math.Max(0, elapsedMilliseconds);
            }

            internal void RecordCatalogLoadElapsed(double elapsedMilliseconds)
            {
                _catalogLoadElapsedMilliseconds += Math.Max(0, elapsedMilliseconds);
            }

            internal void RecordSkinDbPersistQueued()
            {
                _skinDbPersistQueuedCount++;
            }

            internal void RecordSkinDbPersistFallbackApplied()
            {
                _skinDbPersistFallbackAppliedCount++;
            }

            internal void RecordSkinNavigateAttempted()
            {
                _skinNavigateAttemptedCount++;
            }

            internal void RecordSkinNavigateSucceeded()
            {
                _skinNavigateSucceededCount++;
            }

            internal void RecordSkinNavigateFailed()
            {
                _skinNavigateFailedCount++;
            }

            internal void RecordSkinNavigateSkipped()
            {
                _skinNavigateSkippedCount++;
            }

            internal void RecordSkinRefreshStaleSkipped()
            {
                _skinRefreshStaleSkippedCount++;
            }

            internal void RecordSkinRefreshTeardownSkipped()
            {
                _skinRefreshTeardownSkippedCount++;
            }

            internal string BuildSummaryText(bool includeZeroValues)
            {
                List<string> parts = [];
                AddCount(parts, "catalog_hit", _catalogCacheHitCount, includeZeroValues);
                AddCount(parts, "catalog_miss", _catalogCacheMissCount, includeZeroValues);
                AddCount(parts, "persist_enqueued", _skinDbPersistQueuedCount, includeZeroValues);
                AddCount(
                    parts,
                    "persist_fallback_applied",
                    _skinDbPersistFallbackAppliedCount,
                    includeZeroValues
                );
                AddCount(parts, "catalog_reused", _catalogReusedCount, includeZeroValues);
                AddCount(parts, "catalog_skipped", _catalogSkippedCount, includeZeroValues);
                AddMilliseconds(
                    parts,
                    "catalog_signature_ms",
                    _catalogSignatureElapsedMilliseconds,
                    includeZeroValues
                );
                AddMilliseconds(
                    parts,
                    "catalog_load_ms",
                    _catalogLoadElapsedMilliseconds,
                    includeZeroValues
                );
                AddCount(parts, "navigate_attempted", _skinNavigateAttemptedCount, includeZeroValues);
                AddCount(parts, "navigate_succeeded", _skinNavigateSucceededCount, includeZeroValues);
                AddCount(parts, "navigate_failed", _skinNavigateFailedCount, includeZeroValues);
                AddCount(parts, "navigate_skipped", _skinNavigateSkippedCount, includeZeroValues);
                AddCount(
                    parts,
                    "refresh_stale_skipped",
                    _skinRefreshStaleSkippedCount,
                    includeZeroValues
                );
                AddCount(
                    parts,
                    "refresh_teardown_skipped",
                    _skinRefreshTeardownSkippedCount,
                    includeZeroValues
                );

                return string.Join(" ", parts);
            }

            private static void AddCount(
                List<string> parts,
                string name,
                int value,
                bool includeZeroValues
            )
            {
                if (includeZeroValues || value > 0)
                {
                    parts.Add($"{name}={value}");
                }
            }

            private static void AddMilliseconds(
                List<string> parts,
                string name,
                double value,
                bool includeZeroValues
            )
            {
                if (includeZeroValues || value > 0)
                {
                    parts.Add($"{name}={Math.Max(0, value):F1}");
                }
            }
        }

        private static bool IsLogThrottled(
            string bucket,
            DateTime now,
            int throttleMilliseconds,
            Dictionary<string, DateTime> lastWriteUtcByEvent
        )
        {
            lock (QuietLogLock)
            {
                if (
                    lastWriteUtcByEvent.TryGetValue(bucket, out DateTime lastWrite)
                    && (now - lastWrite).TotalMilliseconds < throttleMilliseconds
                )
                {
                    return true;
                }

                lastWriteUtcByEvent[bucket] = now;
                return false;
            }
        }

        private static string BuildLogBucket(string category, string message)
        {
            string trimmed = message.Trim();
            int colonIndex = trimmed.IndexOf(':');
            int cutIndex = colonIndex > 0 ? Math.Min(colonIndex, trimmed.Length) : trimmed.Length;

            if (trimmed.Length > 70)
            {
                cutIndex = Math.Min(70, trimmed.Length);
            }

            string eventKey = trimmed[..cutIndex].Trim();
            if (eventKey.Length == 0)
            {
                eventKey = "event";
            }

            return $"{category}|{eventKey}";
        }

        private static string NormalizeInlineText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            return value
                .Replace("\r\n", " ", StringComparison.Ordinal)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ')
                .Trim();
        }

        internal static void TaskStart(string taskName, string detail = "")
        {
            Write("task-start", $"{taskName} {detail}".Trim());
        }

        internal static void TaskEnd(string taskName, string detail = "")
        {
            Write("task-end", $"{taskName} {detail}".Trim());
        }

        private enum DebugRuntimeLogToggleGroup
        {
            Other,
            Watch,
            Queue,
            Thumbnail,
            Ui,
            Skin,
            DebugTool,
            Database,
        }
    }
}
