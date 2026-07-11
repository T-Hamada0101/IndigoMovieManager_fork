#nullable enable

using System;
using System.Globalization;

namespace IndigoMovieManager.Infrastructure;

public readonly record struct DebugRuntimeLogPhase0SessionBoundaryResult(
    bool IsSatisfied,
    string Reason
);

public static class DebugRuntimeLogPhase0SessionBoundaryPolicy
{
    public const string SessionStartedLocalTimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";

    public static bool TryParseSessionStartedLocal(string? value, out DateTime sessionStartedLocal)
    {
        return DateTime.TryParseExact(
            value,
            SessionStartedLocalTimestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out sessionStartedLocal
        );
    }

    public static DebugRuntimeLogPhase0SessionBoundaryResult Evaluate(
        DebugRuntimeLogRunWindowSummary runWindow,
        DebugRuntimeLogRunSliceResult runSlice,
        DateTime sessionStartedLocal
    )
    {
        if (runWindow.FirstTimestamp is not DateTime firstTimestamp)
        {
            return new DebugRuntimeLogPhase0SessionBoundaryResult(
                IsSatisfied: false,
                Reason: "missing-run-timestamp"
            );
        }

        if (!runSlice.HasSequence)
        {
            return new DebugRuntimeLogPhase0SessionBoundaryResult(
                IsSatisfied: false,
                Reason: "missing-sequence"
            );
        }

        if (runSlice.StartSequence != 1)
        {
            return new DebugRuntimeLogPhase0SessionBoundaryResult(
                IsSatisfied: false,
                Reason: "start-sequence-not-one"
            );
        }

        // ローカル時刻同士をそのまま比較し、UTC変換による境界のずれを作らない。
        if (firstTimestamp < sessionStartedLocal)
        {
            return new DebugRuntimeLogPhase0SessionBoundaryResult(
                IsSatisfied: false,
                Reason: "run-before-session"
            );
        }

        return new DebugRuntimeLogPhase0SessionBoundaryResult(
            IsSatisfied: true,
            Reason: "satisfied"
        );
    }
}
