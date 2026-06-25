namespace IndigoMovieManager;

// UI Shell が各イベントから拾った軽い状態だけを束ね、背後処理の優先判断へ渡す。
internal readonly record struct UiOperationSnapshot(
    bool IsUserPriorityActive,
    bool IsManualMode,
    bool IsWatchUiSuppressed,
    bool IsRecentViewportInteractionActive,
    bool IsPlayerPlaybackActive
);

// 旧名の呼び出し元を段階移行できるよう、新しい UI Shell 契約へそのまま流す。
internal readonly record struct UiOperationPrioritySnapshot(
    bool IsUserPriorityActive,
    bool IsManualMode,
    bool IsWatchUiSuppressed,
    bool IsRecentViewportInteractionActive,
    bool IsPlayerPlaybackActive
)
{
    internal UiOperationSnapshot ToUiOperationSnapshot()
    {
        return new UiOperationSnapshot(
            IsUserPriorityActive,
            IsManualMode,
            IsWatchUiSuppressed,
            IsRecentViewportInteractionActive,
            IsPlayerPlaybackActive
        );
    }

    public static implicit operator UiOperationSnapshot(UiOperationPrioritySnapshot snapshot)
    {
        return snapshot.ToUiOperationSnapshot();
    }
}

internal static class UiOperationPriorityPolicy
{
    internal const string DeferReasonNone = "none";
    internal const string DeferReasonUserPriority = "user-priority";
    internal const string DeferReasonUiSuppression = "ui-suppression";
    internal const string DeferReasonRecentViewport = "recent-viewport";
    internal const string OperationReasonNormal = "normal";
    internal const string OperationReasonPlayerPlayback = "player-playback";

    // 実機ログで UI Shell の入力状態を同じ語彙で追えるよう、snapshot fields をここに集約する。
    internal static string BuildSnapshotLogFields(UiOperationSnapshot snapshot)
    {
        return
            $"is_user_priority_active={FormatLogBool(snapshot.IsUserPriorityActive)} "
            + $"is_manual_mode={FormatLogBool(snapshot.IsManualMode)} "
            + $"is_watch_ui_suppressed={FormatLogBool(snapshot.IsWatchUiSuppressed)} "
            + $"is_recent_viewport_active={FormatLogBool(snapshot.IsRecentViewportInteractionActive)} "
            + $"is_player_playback_active={FormatLogBool(snapshot.IsPlayerPlaybackActive)}";
    }

    // 明示操作中は、手動要求以外の背後処理を後ろへ逃がす。
    internal static bool ShouldDeferBackgroundWork(UiOperationSnapshot snapshot)
    {
        return snapshot.IsUserPriorityActive && !snapshot.IsManualMode;
    }

    // catch-up は user-priority / UI suppression 側だけで扱い、軽い viewport 操作では積まない。
    internal static bool ShouldQueueBackgroundCatchUp(
        bool isStillActive,
        bool hasDeferredWatchWork
    )
    {
        return !isStillActive && hasDeferredWatchWork;
    }

    internal static string ResolveEverythingPollDeferReason(
        UiOperationSnapshot snapshot
    )
    {
        if (snapshot.IsWatchUiSuppressed && !snapshot.IsManualMode)
        {
            return DeferReasonUiSuppression;
        }

        if (ShouldDeferBackgroundWork(snapshot))
        {
            return DeferReasonUserPriority;
        }

        if (snapshot.IsRecentViewportInteractionActive && !snapshot.IsManualMode)
        {
            return DeferReasonRecentViewport;
        }

        return DeferReasonNone;
    }

    internal static bool ShouldQueueCatchUpForEverythingPollDefer(string deferReason)
    {
        return string.Equals(
                deferReason,
                DeferReasonUserPriority,
                StringComparison.Ordinal
            )
            || string.Equals(
                deferReason,
                DeferReasonUiSuppression,
                StringComparison.Ordinal
            );
    }

    internal static bool ShouldProbeEverythingPollQueueLoad(string deferReason)
    {
        return string.Equals(deferReason, DeferReasonNone, StringComparison.Ordinal);
    }

    internal static bool ShouldExtendEverythingPollDelay(
        UiOperationSnapshot snapshot,
        string deferReason
    )
    {
        return !string.Equals(deferReason, DeferReasonNone, StringComparison.Ordinal)
            || snapshot.IsPlayerPlaybackActive;
    }

    internal static string ResolveEverythingPollOperationReason(
        UiOperationSnapshot snapshot,
        string deferReason
    )
    {
        if (!string.Equals(deferReason, DeferReasonNone, StringComparison.Ordinal))
        {
            return deferReason;
        }

        return snapshot.IsPlayerPlaybackActive
            ? OperationReasonPlayerPlayback
            : OperationReasonNormal;
    }

    private static string FormatLogBool(bool value)
    {
        return value ? "true" : "false";
    }
}
