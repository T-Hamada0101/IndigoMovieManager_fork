#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace IndigoMovieManager.Infrastructure;

public readonly record struct DebugRuntimeLogRunWindowSummary(
    int SourceLineCount,
    int TimestampLineCount,
    DateTime? FirstTimestamp,
    DateTime? LastTimestamp
)
{
    public bool HasTimestamp => FirstTimestamp.HasValue && LastTimestamp.HasValue;

    public long? ElapsedMilliseconds
    {
        get
        {
            if (FirstTimestamp is not DateTime first || LastTimestamp is not DateTime last)
            {
                return null;
            }

            return (long)(last - first).TotalMilliseconds;
        }
    }

    public string BuildSummaryText()
    {
        if (FirstTimestamp is not DateTime first || LastTimestamp is not DateTime last)
        {
            return string.Join(
                " ",
                "log_run_window=none",
                "elapsed_ms=none",
                $"timestamp_lines={TimestampLineCount}/{SourceLineCount}"
            );
        }

        return string.Join(
            " ",
            $"log_run_window={FormatTimestamp(first)}..{FormatTimestamp(last)}",
            $"elapsed_ms={((long)(last - first).TotalMilliseconds).ToString(CultureInfo.InvariantCulture)}",
            $"timestamp_lines={TimestampLineCount}/{SourceLineCount}"
        );
    }

    private static string FormatTimestamp(DateTime timestamp)
    {
        return timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture);
    }
}

public static class DebugRuntimeLogRunWindowPolicy
{
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";
    private const int TimestampTextLength = 23;

    public static DebugRuntimeLogRunWindowSummary Evaluate(IEnumerable<string>? lines)
    {
        if (lines is null)
        {
            return new DebugRuntimeLogRunWindowSummary(0, 0, null, null);
        }

        int sourceLineCount = 0;
        int timestampLineCount = 0;
        DateTime? firstTimestamp = null;
        DateTime? lastTimestamp = null;

        foreach (string? sourceLine in lines)
        {
            sourceLineCount++;

            if (!TryReadLineHeadTimestamp(sourceLine, out DateTime timestamp))
            {
                continue;
            }

            // 先頭と終端だけを保持し、ログrun全体の時間幅を軽く要約する。
            firstTimestamp ??= timestamp;
            lastTimestamp = timestamp;
            timestampLineCount++;
        }

        return new DebugRuntimeLogRunWindowSummary(
            sourceLineCount,
            timestampLineCount,
            firstTimestamp,
            lastTimestamp
        );
    }

    private static bool TryReadLineHeadTimestamp(string? line, out DateTime timestamp)
    {
        timestamp = default;
        if (string.IsNullOrEmpty(line) || line.Length < TimestampTextLength)
        {
            return false;
        }

        return DateTime.TryParseExact(
            line.AsSpan(0, TimestampTextLength),
            TimestampFormat.AsSpan(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out timestamp
        );
    }
}
