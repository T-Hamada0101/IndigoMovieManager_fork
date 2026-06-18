using System;

namespace IndigoMovieManager;

// UIへ戻す軽い作業要求の語彙だけを固定する。実行器は既存の各予約経路に残す。
internal readonly record struct UiWorkRequest(
    UiWorkPriority Priority,
    string CoalesceKey,
    string LatestOnlyKey,
    string LogReason,
    string BoundedDrain,
    string TimeoutPolicy
)
{
    internal bool HasCoalesceKey => !string.IsNullOrWhiteSpace(CoalesceKey);
    internal bool HasLatestOnlyKey => !string.IsNullOrWhiteSpace(LatestOnlyKey);
}

internal readonly record struct UiWorkRequestAcceptance(
    bool Accepted,
    string SkipReason,
    string LogReason,
    string ReleaseReason,
    string BoundedDrain,
    string TimeoutPolicy
);

// 数値が小さいほど、ユーザー操作に近い作業として扱う。
internal enum UiWorkPriority
{
    Input = 0,
    Selection = 10,
    Scroll = 20,
    Player = 30,
    VisibleImage = 40,
    LatestSearchSort = 50,
    WatchSmallDiff = 60,
    WatchReload = 65,
    ThumbnailRefresh = 70,
    Rescue = 80,
    SkinCatalog = 90,
}

internal static class UiWorkRequestPolicy
{
    internal const string AcceptReasonNone = "none";
    internal const string RejectReasonDispatcherMissing = "dispatcher-missing";
    internal const string RejectReasonShutdownStarted = "dispatcher-shutdown-started";
    internal const string RejectReasonShutdownFinished = "dispatcher-shutdown-finished";
    internal const string ReleaseReasonAccepted = "accepted";
    internal const string ReleaseReasonRejected = "rejected";
    internal const string ReleaseReasonDeferred = "deferred";
    internal const string ReleaseReasonReleased = "released";
    internal const string ReleaseReasonCompleted = "completed";
    internal const string ReleaseReasonFailed = "failed";
    internal const string ReleaseReasonCanceled = "canceled";
    internal const string ReleaseReasonTimeout = "timeout";
    internal const string BoundedDrainDispatcherShutdownGuard = "dispatcher-shutdown-guard";
    internal const string BoundedDrainCancellationToken = "cancellation-token";
    internal const string BoundedDrainDeferredRequestCts = "deferred-request-cts";
    internal const string TimeoutPolicyNone = "none";

    internal const string ThumbnailProgressSnapshotRefreshCoalesceKey =
        "thumbnail-progress:snapshot-refresh:coalesce";
    internal const string ThumbnailProgressSnapshotRefreshLatestOnlyKey =
        "thumbnail-progress:snapshot-refresh:latest-only";
    internal const string ThumbnailProgressSnapshotRefreshLogReason =
        "thumbnail-progress.snapshot-refresh";
    internal const string EverythingWatchPollCoalesceKey =
        "watch:everything-poll:coalesce";
    internal const string EverythingWatchPollLatestOnlyKey =
        "watch:everything-poll:latest-only";
    internal const string EverythingWatchPollLogReason = "watch.everything-poll";
    internal const string WatchUiReloadCoalesceKey = "watch:ui-reload:coalesce";
    internal const string WatchUiReloadLatestOnlyKey = "watch:ui-reload:latest-only";
    internal const string WatchUiReloadQueryOnlyLogReason = "watch.ui-reload.query-only";
    internal const string WatchUiReloadFullFallbackLogReason =
        "watch.ui-reload.full-fallback";

    internal static UiWorkRequest CreateThumbnailProgressSnapshotRefreshRequest()
    {
        return new UiWorkRequest(
            UiWorkPriority.ThumbnailRefresh,
            ThumbnailProgressSnapshotRefreshCoalesceKey,
            ThumbnailProgressSnapshotRefreshLatestOnlyKey,
            ThumbnailProgressSnapshotRefreshLogReason,
            BoundedDrainDispatcherShutdownGuard,
            TimeoutPolicyNone
        );
    }

    internal static UiWorkRequest CreateEverythingWatchPollRequest()
    {
        return new UiWorkRequest(
            UiWorkPriority.WatchSmallDiff,
            EverythingWatchPollCoalesceKey,
            EverythingWatchPollLatestOnlyKey,
            EverythingWatchPollLogReason,
            BoundedDrainCancellationToken,
            TimeoutPolicyNone
        );
    }

    internal static UiWorkRequest CreateWatchUiReloadRequest(bool useQueryOnlyReload)
    {
        return new UiWorkRequest(
            useQueryOnlyReload ? UiWorkPriority.WatchSmallDiff : UiWorkPriority.WatchReload,
            WatchUiReloadCoalesceKey,
            WatchUiReloadLatestOnlyKey,
            useQueryOnlyReload ? WatchUiReloadQueryOnlyLogReason : WatchUiReloadFullFallbackLogReason,
            BoundedDrainDeferredRequestCts,
            TimeoutPolicyNone
        );
    }

    internal static UiWorkRequestAcceptance CanAcceptForDispatcher(
        UiWorkRequest request,
        bool hasDispatcher,
        bool hasShutdownStarted,
        bool hasShutdownFinished
    )
    {
        if (!hasDispatcher)
        {
            return Reject(request, RejectReasonDispatcherMissing);
        }

        if (hasShutdownStarted)
        {
            return Reject(request, RejectReasonShutdownStarted);
        }

        if (hasShutdownFinished)
        {
            return Reject(request, RejectReasonShutdownFinished);
        }

        return new UiWorkRequestAcceptance(
            Accepted: true,
            SkipReason: AcceptReasonNone,
            LogReason: request.LogReason ?? "",
            ReleaseReason: ReleaseReasonAccepted,
            BoundedDrain: request.BoundedDrain ?? "",
            TimeoutPolicy: NormalizeTimeoutPolicy(request.TimeoutPolicy)
        );
    }

    internal static string BuildRequestLifecycleLogFields(
        UiWorkRequest request,
        string releaseReason
    )
    {
        return $"log_reason={request.LogReason ?? ""} release_reason={NormalizeReleaseReason(releaseReason)} bounded_drain={request.BoundedDrain ?? ""}";
    }

    // scheduler本体を作る前に、各予約ログの作業語彙だけを同じ形で揃える。
    internal static string BuildRequestSchedulerLogFields(
        UiWorkRequest request,
        string releaseReason
    )
    {
        return $"{BuildRequestLifecycleLogFields(request, releaseReason)} work_priority={request.Priority} coalesce_key='{request.CoalesceKey ?? ""}' latest_only_key='{request.LatestOnlyKey ?? ""}' timeout_policy={NormalizeTimeoutPolicy(request.TimeoutPolicy)}";
    }

    private static UiWorkRequestAcceptance Reject(UiWorkRequest request, string skipReason)
    {
        return new UiWorkRequestAcceptance(
            Accepted: false,
            SkipReason: skipReason ?? "",
            LogReason: request.LogReason ?? "",
            ReleaseReason: ReleaseReasonRejected,
            BoundedDrain: request.BoundedDrain ?? "",
            TimeoutPolicy: NormalizeTimeoutPolicy(request.TimeoutPolicy)
        );
    }

    private static string NormalizeReleaseReason(string releaseReason)
    {
        return string.IsNullOrWhiteSpace(releaseReason) ? ReleaseReasonCompleted : releaseReason;
    }

    private static string NormalizeTimeoutPolicy(string timeoutPolicy)
    {
        return string.IsNullOrWhiteSpace(timeoutPolicy) ? TimeoutPolicyNone : timeoutPolicy;
    }
}
