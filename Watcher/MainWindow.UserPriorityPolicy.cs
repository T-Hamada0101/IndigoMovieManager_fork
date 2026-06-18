namespace IndigoMovieManager;

public partial class MainWindow
{
    internal const string UserPriorityReleaseReasonNormal = "normal";
    internal const string UserPriorityReleaseReasonTimeout = "timeout";
    internal const int UserPriorityTimeoutSeconds = 30;

    // 明示的なユーザー要求中は、手動要求以外の背後走査を後ろへ逃がす。
    internal static bool ShouldDeferBackgroundWorkForUserPriority(
        bool isUserPriorityActive,
        bool isManualMode
    )
    {
        return UiOperationPriorityPolicy.ShouldDeferBackgroundWork(
            new UiOperationPrioritySnapshot(
                isUserPriorityActive,
                isManualMode,
                IsWatchUiSuppressed: false,
                IsRecentViewportInteractionActive: false,
                IsPlayerPlaybackActive: false
            )
        );
    }

    // ユーザー要求が終わったら、保留していた背後走査を1回だけ catch-up させる。
    internal static bool ShouldQueueBackgroundCatchUpAfterUserPriority(
        bool isStillActive,
        bool hasDeferredWatchWork
    )
    {
        return UiOperationPriorityPolicy.ShouldQueueBackgroundCatchUp(
            isStillActive,
            hasDeferredWatchWork
        );
    }

    internal static bool IsUserPriorityWorkTimedOut(
        DateTime? startedUtc,
        DateTime nowUtc,
        TimeSpan timeout
    )
    {
        return startedUtc.HasValue
            && timeout > TimeSpan.Zero
            && nowUtc - startedUtc.Value >= timeout;
    }

    internal static TimeSpan ResolveUserPriorityTimeout()
    {
        return TimeSpan.FromSeconds(UserPriorityTimeoutSeconds);
    }

    internal static string ResolveUserPriorityReleaseReason(
        DateTime? startedUtc,
        DateTime endedUtc,
        TimeSpan timeout
    )
    {
        return IsUserPriorityWorkTimedOut(startedUtc, endedUtc, timeout)
            ? UserPriorityReleaseReasonTimeout
            : UserPriorityReleaseReasonNormal;
    }

    internal static long ResolveUserPriorityElapsedMilliseconds(
        DateTime? startedUtc,
        DateTime endedUtc
    )
    {
        if (!startedUtc.HasValue || endedUtc <= startedUtc.Value)
        {
            return 0;
        }

        return (long)(endedUtc - startedUtc.Value).TotalMilliseconds;
    }

    internal static string BuildUserPriorityReleaseLogMessage(
        string beginReason,
        string endReason,
        long elapsedMilliseconds,
        string releaseReason,
        bool hasDeferredWatchWork
    )
    {
        return
            $"user priority end: begin_reason={beginReason} end_reason={endReason} elapsed_ms={elapsedMilliseconds} release_reason={releaseReason} deferred_watch={FormatLogBool(hasDeferredWatchWork)}";
    }

    private static string FormatLogBool(bool value)
    {
        return value ? "true" : "false";
    }
}
