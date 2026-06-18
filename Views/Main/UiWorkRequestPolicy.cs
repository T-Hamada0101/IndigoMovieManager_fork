using System;

namespace IndigoMovieManager;

// UIへ戻す軽い作業要求の語彙だけを固定する。実行器は既存の各予約経路に残す。
internal readonly record struct UiWorkRequest(
    UiWorkPriority Priority,
    string CoalesceKey,
    string LatestOnlyKey,
    string LogReason
)
{
    internal bool HasCoalesceKey => !string.IsNullOrWhiteSpace(CoalesceKey);
    internal bool HasLatestOnlyKey => !string.IsNullOrWhiteSpace(LatestOnlyKey);
}

internal readonly record struct UiWorkRequestAcceptance(
    bool Accepted,
    string SkipReason,
    string LogReason
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

    internal static UiWorkRequest CreateThumbnailProgressSnapshotRefreshRequest()
    {
        return new UiWorkRequest(
            UiWorkPriority.ThumbnailRefresh,
            ThumbnailProgressSnapshotRefreshCoalesceKey,
            ThumbnailProgressSnapshotRefreshLatestOnlyKey,
            ThumbnailProgressSnapshotRefreshLogReason
        );
    }

    internal static UiWorkRequest CreateEverythingWatchPollRequest()
    {
        return new UiWorkRequest(
            UiWorkPriority.WatchSmallDiff,
            EverythingWatchPollCoalesceKey,
            EverythingWatchPollLatestOnlyKey,
            EverythingWatchPollLogReason
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
            LogReason: request.LogReason ?? ""
        );
    }

    private static UiWorkRequestAcceptance Reject(UiWorkRequest request, string skipReason)
    {
        return new UiWorkRequestAcceptance(
            Accepted: false,
            SkipReason: skipReason ?? "",
            LogReason: request.LogReason ?? ""
        );
    }
}
