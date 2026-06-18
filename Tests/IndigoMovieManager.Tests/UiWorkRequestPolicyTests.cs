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
        });
    }
}
