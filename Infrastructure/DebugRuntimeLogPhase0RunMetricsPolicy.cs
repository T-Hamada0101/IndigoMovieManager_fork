#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace IndigoMovieManager.Infrastructure;

public static class DebugRuntimeLogPhase0RunMetricsPolicy
{
    private const string UiHangUpdatedPrefix = "ui hang updated:";
    private const string DiffContractField = "diff_contract";
    private const string DiffContractValue = "readmodel-diff-v1";
    private const string DiffFullFallbackReasonField = "diff_full_fallback_reason";

    private static readonly string[] QueueDepthFieldNames =
    [
        "queue_depth_before",
        "queue_depth_after",
        "pending_count",
        "pending_count_after",
    ];

    public static DebugRuntimeLogPhase0RunMetricsSummary Evaluate(IEnumerable<string>? lines)
    {
        List<long> uiHangDelaySamples = new();
        long? maxQueueDepth = null;
        int staleDiscardLogCount = 0;
        int fullFallbackLogCount = 0;

        if (lines is not null)
        {
            foreach (string? sourceLine in lines)
            {
                string line = sourceLine ?? "";

                if (line.Contains(UiHangUpdatedPrefix, StringComparison.Ordinal)
                    && TryReadNonNegativeLongField(line, "delay_ms", out long delayMs))
                {
                    uiHangDelaySamples.Add(delayMs);
                }

                foreach (string fieldName in QueueDepthFieldNames)
                {
                    if (TryReadNonNegativeLongField(line, fieldName, out long queueDepth)
                        && (!maxQueueDepth.HasValue || queueDepth > maxQueueDepth.Value))
                    {
                        maxQueueDepth = queueDepth;
                    }
                }

                if (HasStaleDiscardReason(line))
                {
                    staleDiscardLogCount++;
                }

                if (IsFullFallback(line))
                {
                    fullFallbackLogCount++;
                }
            }
        }

        uiHangDelaySamples.Sort();
        return new DebugRuntimeLogPhase0RunMetricsSummary(
            uiHangDelaySamples.Count,
            GetNearestRank(uiHangDelaySamples, 50),
            GetNearestRank(uiHangDelaySamples, 95),
            uiHangDelaySamples.Count == 0 ? null : uiHangDelaySamples[^1],
            maxQueueDepth,
            staleDiscardLogCount,
            fullFallbackLogCount
        );
    }

    private static long? GetNearestRank(IReadOnlyList<long> sortedValues, int percentile)
    {
        if (sortedValues.Count == 0)
        {
            return null;
        }

        int rank = ((sortedValues.Count * percentile) + 99) / 100;
        return sortedValues[rank - 1];
    }

    private static bool HasStaleDiscardReason(string line)
    {
        // 実運用で出力している失効理由だけを固定し、曖昧なstale語彙を数えない。
        return HasFieldValue(line, "failure_reason", "stale-image-request")
            || HasFieldValue(line, "failure_reason", "stale-player-right-rail");
    }

    private static bool IsFullFallback(string line)
    {
        return HasFieldValue(line, DiffContractField, DiffContractValue)
            && TryReadFieldValue(line, DiffFullFallbackReasonField, out string reason)
            && !string.Equals(reason, "none", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadNonNegativeLongField(string line, string fieldName, out long value)
    {
        value = 0;
        return TryReadFieldValue(line, fieldName, out string text)
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)
            && value >= 0;
    }

    private static bool HasFieldValue(string line, string fieldName, string expectedValue)
    {
        string fieldPrefix = fieldName + "=";
        int searchStart = 0;
        while (searchStart < line.Length)
        {
            int fieldStart = line.IndexOf(fieldPrefix, searchStart, StringComparison.Ordinal);
            if (fieldStart < 0)
            {
                return false;
            }

            int valueStart = fieldStart + fieldPrefix.Length;
            bool isFieldStart = fieldStart == 0 || char.IsWhiteSpace(line[fieldStart - 1]);
            if (isFieldStart && valueStart < line.Length)
            {
                int valueEnd = valueStart;
                while (valueEnd < line.Length && !char.IsWhiteSpace(line[valueEnd]))
                {
                    valueEnd++;
                }

                if (
                    valueEnd - valueStart == expectedValue.Length
                    && string.CompareOrdinal(line, valueStart, expectedValue, 0, expectedValue.Length)
                        == 0
                )
                {
                    return true;
                }

                searchStart = valueEnd;
                continue;
            }

            searchStart = valueStart;
        }

        return false;
    }

    private static bool TryReadFieldValue(string line, string fieldName, out string value)
    {
        string fieldPrefix = fieldName + "=";
        int searchStart = 0;
        while (searchStart < line.Length)
        {
            int fieldStart = line.IndexOf(fieldPrefix, searchStart, StringComparison.Ordinal);
            if (fieldStart < 0)
            {
                break;
            }

            bool isFieldStart = fieldStart == 0 || char.IsWhiteSpace(line[fieldStart - 1]);
            int valueStart = fieldStart + fieldPrefix.Length;
            if (isFieldStart && valueStart < line.Length)
            {
                int valueEnd = valueStart;
                while (valueEnd < line.Length && !char.IsWhiteSpace(line[valueEnd]))
                {
                    valueEnd++;
                }

                value = line.Substring(valueStart, valueEnd - valueStart);
                return value.Length > 0;
            }

            searchStart = valueStart;
        }

        value = "";
        return false;
    }
}

public sealed class DebugRuntimeLogPhase0RunMetricsSummary
{
    internal DebugRuntimeLogPhase0RunMetricsSummary(
        int uiHangDelaySampleCount,
        long? uiHangDelayP50Ms,
        long? uiHangDelayP95Ms,
        long? uiHangDelayMaxMs,
        long? maxQueueDepth,
        int staleDiscardLogCount,
        int fullFallbackLogCount
    )
    {
        UiHangDelaySampleCount = uiHangDelaySampleCount;
        UiHangDelayP50Ms = uiHangDelayP50Ms;
        UiHangDelayP95Ms = uiHangDelayP95Ms;
        UiHangDelayMaxMs = uiHangDelayMaxMs;
        MaxQueueDepth = maxQueueDepth;
        StaleDiscardLogCount = staleDiscardLogCount;
        FullFallbackLogCount = fullFallbackLogCount;
    }

    public int UiHangDelaySampleCount { get; }

    public long? UiHangDelayP50Ms { get; }

    public long? UiHangDelayP95Ms { get; }

    public long? UiHangDelayMaxMs { get; }

    public long? MaxQueueDepth { get; }

    public int StaleDiscardLogCount { get; }

    public int FullFallbackLogCount { get; }

    public string BuildSummaryText()
    {
        return string.Join(
            " ",
            "phase0_run_metrics=available",
            $"ui_hang_delay_samples={UiHangDelaySampleCount}",
            $"ui_hang_delay_p50_ms={FormatOrNone(UiHangDelayP50Ms)}",
            $"ui_hang_delay_p95_ms={FormatOrNone(UiHangDelayP95Ms)}",
            $"ui_hang_delay_max_ms={FormatOrNone(UiHangDelayMaxMs)}",
            $"max_queue_depth={FormatOrNone(MaxQueueDepth)}",
            $"stale_discard_log_count={StaleDiscardLogCount}",
            $"full_fallback_log_count={FullFallbackLogCount}"
        );
    }

    private static string FormatOrNone(long? value)
    {
        return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "none";
    }
}
