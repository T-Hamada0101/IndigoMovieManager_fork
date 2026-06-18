using IndigoMovieManager;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UiWorkRequestPolicyTests
{
    [Test]
    public void ThumbnailProgressSnapshotRefreshRequest_契約語彙を固定する()
    {
        UiWorkRequest request = UiWorkRequestPolicy.CreateThumbnailProgressSnapshotRefreshRequest();

        Assert.Multiple(() =>
        {
            Assert.That(request.Priority, Is.EqualTo(UiWorkPriority.ThumbnailRefresh));
            Assert.That(
                request.CoalesceKey,
                Is.EqualTo(UiWorkRequestPolicy.ThumbnailProgressSnapshotRefreshCoalesceKey)
            );
            Assert.That(
                request.LatestOnlyKey,
                Is.EqualTo(UiWorkRequestPolicy.ThumbnailProgressSnapshotRefreshLatestOnlyKey)
            );
            Assert.That(
                request.LogReason,
                Is.EqualTo(UiWorkRequestPolicy.ThumbnailProgressSnapshotRefreshLogReason)
            );
            Assert.That(
                request.BoundedDrain,
                Is.EqualTo(UiWorkRequestPolicy.BoundedDrainDispatcherShutdownGuard)
            );
            Assert.That(request.TimeoutPolicy, Is.EqualTo(UiWorkRequestPolicy.TimeoutPolicyNone));
            Assert.That(request.HasCoalesceKey, Is.True);
            Assert.That(request.HasLatestOnlyKey, Is.True);
        });
    }

    [Test]
    public void EverythingWatchPollRequest_契約語彙を固定する()
    {
        UiWorkRequest request = UiWorkRequestPolicy.CreateEverythingWatchPollRequest();

        Assert.Multiple(() =>
        {
            Assert.That(request.Priority, Is.EqualTo(UiWorkPriority.WatchSmallDiff));
            Assert.That(
                request.CoalesceKey,
                Is.EqualTo(UiWorkRequestPolicy.EverythingWatchPollCoalesceKey)
            );
            Assert.That(
                request.LatestOnlyKey,
                Is.EqualTo(UiWorkRequestPolicy.EverythingWatchPollLatestOnlyKey)
            );
            Assert.That(request.LogReason, Is.EqualTo(UiWorkRequestPolicy.EverythingWatchPollLogReason));
            Assert.That(
                request.BoundedDrain,
                Is.EqualTo(UiWorkRequestPolicy.BoundedDrainCancellationToken)
            );
            Assert.That(request.TimeoutPolicy, Is.EqualTo(UiWorkRequestPolicy.TimeoutPolicyNone));
            Assert.That(request.HasCoalesceKey, Is.True);
            Assert.That(request.HasLatestOnlyKey, Is.True);
        });
    }

    [Test]
    public void WatchUiReloadRequest_queryOnlyはwatch小差分として契約語彙を固定する()
    {
        UiWorkRequest request = UiWorkRequestPolicy.CreateWatchUiReloadRequest(
            useQueryOnlyReload: true
        );

        Assert.Multiple(() =>
        {
            Assert.That(request.Priority, Is.EqualTo(UiWorkPriority.WatchSmallDiff));
            Assert.That(
                request.CoalesceKey,
                Is.EqualTo(UiWorkRequestPolicy.WatchUiReloadCoalesceKey)
            );
            Assert.That(
                request.LatestOnlyKey,
                Is.EqualTo(UiWorkRequestPolicy.WatchUiReloadLatestOnlyKey)
            );
            Assert.That(
                request.LogReason,
                Is.EqualTo(UiWorkRequestPolicy.WatchUiReloadQueryOnlyLogReason)
            );
            Assert.That(
                request.BoundedDrain,
                Is.EqualTo(UiWorkRequestPolicy.BoundedDrainDeferredRequestCts)
            );
            Assert.That(request.TimeoutPolicy, Is.EqualTo(UiWorkRequestPolicy.TimeoutPolicyNone));
            Assert.That(request.HasCoalesceKey, Is.True);
            Assert.That(request.HasLatestOnlyKey, Is.True);
        });
    }

    [Test]
    public void WatchUiReloadRequest_fullFallbackはreload作業として契約語彙を固定する()
    {
        UiWorkRequest request = UiWorkRequestPolicy.CreateWatchUiReloadRequest(
            useQueryOnlyReload: false
        );

        Assert.Multiple(() =>
        {
            Assert.That(request.Priority, Is.EqualTo(UiWorkPriority.WatchReload));
            Assert.That(
                request.CoalesceKey,
                Is.EqualTo(UiWorkRequestPolicy.WatchUiReloadCoalesceKey)
            );
            Assert.That(
                request.LatestOnlyKey,
                Is.EqualTo(UiWorkRequestPolicy.WatchUiReloadLatestOnlyKey)
            );
            Assert.That(
                request.LogReason,
                Is.EqualTo(UiWorkRequestPolicy.WatchUiReloadFullFallbackLogReason)
            );
            Assert.That(
                request.BoundedDrain,
                Is.EqualTo(UiWorkRequestPolicy.BoundedDrainDeferredRequestCts)
            );
            Assert.That(request.TimeoutPolicy, Is.EqualTo(UiWorkRequestPolicy.TimeoutPolicyNone));
            Assert.That(request.HasCoalesceKey, Is.True);
            Assert.That(request.HasLatestOnlyKey, Is.True);
        });
    }

    [Test]
    public void KanaBackfillMovieViewRefreshRequest_watch小差分として契約語彙を固定する()
    {
        UiWorkRequest request = UiWorkRequestPolicy.CreateKanaBackfillMovieViewRefreshRequest();

        Assert.Multiple(() =>
        {
            Assert.That(request.Priority, Is.EqualTo(UiWorkPriority.WatchSmallDiff));
            Assert.That(
                request.CoalesceKey,
                Is.EqualTo(UiWorkRequestPolicy.KanaBackfillMovieViewRefreshCoalesceKey)
            );
            Assert.That(
                request.LatestOnlyKey,
                Is.EqualTo(UiWorkRequestPolicy.KanaBackfillMovieViewRefreshLatestOnlyKey)
            );
            Assert.That(
                request.LogReason,
                Is.EqualTo(UiWorkRequestPolicy.KanaBackfillMovieViewRefreshLogReason)
            );
            Assert.That(
                request.BoundedDrain,
                Is.EqualTo(UiWorkRequestPolicy.BoundedDrainCancellationToken)
            );
            Assert.That(request.TimeoutPolicy, Is.EqualTo(UiWorkRequestPolicy.TimeoutPolicyNone));
            Assert.That(request.HasCoalesceKey, Is.True);
            Assert.That(request.HasLatestOnlyKey, Is.True);
        });
    }

    [Test]
    public void ExternalSkinHostRefreshRequest_skinCatalog作業として契約語彙を固定する()
    {
        UiWorkRequest request = UiWorkRequestPolicy.CreateExternalSkinHostRefreshRequest();

        Assert.Multiple(() =>
        {
            Assert.That(request.Priority, Is.EqualTo(UiWorkPriority.SkinCatalog));
            Assert.That(
                request.CoalesceKey,
                Is.EqualTo(UiWorkRequestPolicy.ExternalSkinHostRefreshCoalesceKey)
            );
            Assert.That(
                request.LatestOnlyKey,
                Is.EqualTo(UiWorkRequestPolicy.ExternalSkinHostRefreshLatestOnlyKey)
            );
            Assert.That(
                request.LogReason,
                Is.EqualTo(UiWorkRequestPolicy.ExternalSkinHostRefreshLogReason)
            );
            Assert.That(
                request.BoundedDrain,
                Is.EqualTo(UiWorkRequestPolicy.BoundedDrainDispatcherShutdownGuard)
            );
            Assert.That(request.TimeoutPolicy, Is.EqualTo(UiWorkRequestPolicy.TimeoutPolicyNone));
            Assert.That(request.HasCoalesceKey, Is.True);
            Assert.That(request.HasLatestOnlyKey, Is.True);
        });
    }

    [TestCase(false, false, false, false, UiWorkRequestPolicy.RejectReasonDispatcherMissing)]
    [TestCase(true, true, false, false, UiWorkRequestPolicy.RejectReasonShutdownStarted)]
    [TestCase(true, false, true, false, UiWorkRequestPolicy.RejectReasonShutdownFinished)]
    [TestCase(true, false, false, true, UiWorkRequestPolicy.AcceptReasonNone)]
    public void CanAcceptForDispatcher_Dispatcher終了状態を理由付きで判定する(
        bool hasDispatcher,
        bool hasShutdownStarted,
        bool hasShutdownFinished,
        bool expectedAccepted,
        string expectedReason
    )
    {
        UiWorkRequest request = UiWorkRequestPolicy.CreateThumbnailProgressSnapshotRefreshRequest();

        UiWorkRequestAcceptance acceptance = UiWorkRequestPolicy.CanAcceptForDispatcher(
            request,
            hasDispatcher,
            hasShutdownStarted,
            hasShutdownFinished
        );

        Assert.Multiple(() =>
        {
            Assert.That(acceptance.Accepted, Is.EqualTo(expectedAccepted));
            Assert.That(acceptance.SkipReason, Is.EqualTo(expectedReason));
            Assert.That(acceptance.LogReason, Is.EqualTo(request.LogReason));
            Assert.That(acceptance.BoundedDrain, Is.EqualTo(request.BoundedDrain));
            Assert.That(acceptance.TimeoutPolicy, Is.EqualTo(request.TimeoutPolicy));
            Assert.That(
                acceptance.ReleaseReason,
                Is.EqualTo(
                    expectedAccepted
                        ? UiWorkRequestPolicy.ReleaseReasonAccepted
                        : UiWorkRequestPolicy.ReleaseReasonRejected
                )
            );
        });
    }

    [Test]
    public void BuildRequestLifecycleLogFields_releaseReasonとboundedDrainを共通形式で出す()
    {
        UiWorkRequest request = UiWorkRequestPolicy.CreateEverythingWatchPollRequest();

        string logFields = UiWorkRequestPolicy.BuildRequestLifecycleLogFields(
            request,
            UiWorkRequestPolicy.ReleaseReasonDeferred
        );

        Assert.That(logFields, Does.Contain("log_reason=watch.everything-poll"));
        Assert.That(logFields, Does.Contain("release_reason=deferred"));
        Assert.That(logFields, Does.Contain("bounded_drain=cancellation-token"));
    }

    [Test]
    public void BuildRequestSchedulerLogFields_予約制御語彙を共通形式で出す()
    {
        UiWorkRequest request = UiWorkRequestPolicy.CreateEverythingWatchPollRequest();

        string logFields = UiWorkRequestPolicy.BuildRequestSchedulerLogFields(
            request,
            UiWorkRequestPolicy.ReleaseReasonDeferred
        );

        Assert.That(logFields, Does.Contain("log_reason=watch.everything-poll"));
        Assert.That(logFields, Does.Contain("release_reason=deferred"));
        Assert.That(logFields, Does.Contain("bounded_drain=cancellation-token"));
        Assert.That(logFields, Does.Contain("work_priority=WatchSmallDiff"));
        Assert.That(logFields, Does.Contain("coalesce_key='watch:everything-poll:coalesce'"));
        Assert.That(logFields, Does.Contain("latest_only_key='watch:everything-poll:latest-only'"));
        Assert.That(logFields, Does.Contain("timeout_policy=none"));
    }

    [Test]
    public void BuildRequestAdmissionLogFields_既存予約を入場語彙で説明する()
    {
        UiWorkRequest request = UiWorkRequestPolicy.CreateEverythingWatchPollRequest();

        string logFields = UiWorkRequestPolicy.BuildRequestAdmissionLogFields(
            request,
            UiWorkRequestPolicy.ReleaseReasonDeferred
        );

        Assert.That(logFields, Does.Contain("log_reason=watch.everything-poll"));
        Assert.That(logFields, Does.Contain("release_reason=deferred"));
        Assert.That(logFields, Does.Contain("work_priority=WatchSmallDiff"));
        Assert.That(logFields, Does.Contain("admission_action=Enqueue"));
        Assert.That(logFields, Does.Contain("admission_reason=queued"));
        Assert.That(logFields, Does.Contain("queue_capacity=1"));
    }
}
