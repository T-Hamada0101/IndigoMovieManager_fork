using IndigoMovieManager;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UiWorkSchedulerRuntimeTests
{
    [Test]
    public void Queue_満杯時は高優先要求が低優先要求を押し出して先に実行される()
    {
        UiWorkSchedulerRuntime runtime = new(boundedCapacity: 2);

        runtime.Queue(CreateRequest(UiWorkPriority.ThumbnailRefresh, "thumb", "thumb"));
        runtime.Queue(CreateRequest(UiWorkPriority.SkinCatalog, "skin", "skin"));
        UiWorkSchedulerRuntimeQueueResult queued = runtime.Queue(
            CreateRequest(UiWorkPriority.Input, "input", "input")
        );

        UiWorkSchedulerRuntimeTakeResult next = runtime.TryTakeNext();

        Assert.Multiple(() =>
        {
            Assert.That(
                queued.Decision.Action,
                Is.EqualTo(UiWorkSchedulerAdmissionAction.PreemptLowerPriority)
            );
            Assert.That(
                queued.Decision.AdmissionReason,
                Is.EqualTo(UiWorkSchedulerPolicy.AdmissionReasonPriorityPreempted)
            );
            Assert.That(
                runtime.PendingRequests.Select(x => x.Request.CoalesceKey),
                Is.EquivalentTo(new[] { "thumb" })
            );
            Assert.That(next.HasRequest, Is.True);
            Assert.That(next.PendingRequest.Request.Priority, Is.EqualTo(UiWorkPriority.Input));
            Assert.That(next.PendingRequest.Request.CoalesceKey, Is.EqualTo("input"));
        });
    }

    [Test]
    public void Queue_latestOnlyは古い要求を最新要求へ置き換える()
    {
        UiWorkSchedulerRuntime runtime = new(boundedCapacity: 4);

        runtime.Queue(CreateRequest(UiWorkPriority.WatchSmallDiff, "watch", "watch-latest"));
        UiWorkSchedulerRuntimeQueueResult queued = runtime.Queue(
            CreateRequest(UiWorkPriority.WatchReload, "watch", "watch-latest")
        );

        UiWorkSchedulerRuntimeTakeResult next = runtime.TryTakeNext();

        Assert.Multiple(() =>
        {
            Assert.That(
                queued.Decision.Action,
                Is.EqualTo(UiWorkSchedulerAdmissionAction.ReplaceLatestOnly)
            );
            Assert.That(
                queued.Decision.AdmissionReason,
                Is.EqualTo(UiWorkSchedulerPolicy.AdmissionReasonLatestOnlyReplaced)
            );
            Assert.That(queued.PendingCount, Is.EqualTo(1));
            Assert.That(next.PendingRequest.Sequence, Is.EqualTo(2));
            Assert.That(next.PendingRequest.Request.Priority, Is.EqualTo(UiWorkPriority.WatchReload));
        });
    }

    [Test]
    public void Queue_coalesceは同じ作業枠へ畳み込む()
    {
        UiWorkSchedulerRuntime runtime = new(boundedCapacity: 4);

        runtime.Queue(CreateRequest(UiWorkPriority.ThumbnailRefresh, "thumb-coalesce", ""));
        UiWorkSchedulerRuntimeQueueResult queued = runtime.Queue(
            CreateRequest(UiWorkPriority.VisibleImage, "thumb-coalesce", "")
        );

        UiWorkSchedulerRuntimeTakeResult next = runtime.TryTakeNext();

        Assert.Multiple(() =>
        {
            Assert.That(
                queued.Decision.Action,
                Is.EqualTo(UiWorkSchedulerAdmissionAction.ReplaceCoalesced)
            );
            Assert.That(
                queued.Decision.AdmissionReason,
                Is.EqualTo(UiWorkSchedulerPolicy.AdmissionReasonCoalesced)
            );
            Assert.That(queued.PendingCount, Is.EqualTo(1));
            Assert.That(next.PendingRequest.Sequence, Is.EqualTo(2));
            Assert.That(next.PendingRequest.Request.Priority, Is.EqualTo(UiWorkPriority.VisibleImage));
        });
    }

    [Test]
    public void Queue_サムネ進捗SnapshotRefreshは容量1で最新要求だけ残す()
    {
        UiWorkSchedulerRuntime runtime = new(boundedCapacity: 1);

        runtime.Queue(UiWorkRequestPolicy.CreateThumbnailProgressSnapshotRefreshRequest());
        UiWorkSchedulerRuntimeQueueResult queued = runtime.Queue(
            UiWorkRequestPolicy.CreateThumbnailProgressSnapshotRefreshRequest()
        );

        UiWorkSchedulerRuntimeTakeResult next = runtime.TryTakeNext();

        Assert.Multiple(() =>
        {
            Assert.That(
                queued.Decision.Action,
                Is.EqualTo(UiWorkSchedulerAdmissionAction.ReplaceLatestOnly)
            );
            Assert.That(
                queued.Decision.AdmissionReason,
                Is.EqualTo(UiWorkSchedulerPolicy.AdmissionReasonLatestOnlyReplaced)
            );
            Assert.That(queued.PendingCount, Is.EqualTo(1));
            Assert.That(next.HasRequest, Is.True);
            Assert.That(next.PendingRequest.Sequence, Is.EqualTo(2));
            Assert.That(
                next.PendingRequest.Request.LogReason,
                Is.EqualTo(UiWorkRequestPolicy.ThumbnailProgressSnapshotRefreshLogReason)
            );
            Assert.That(next.PendingCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void Queue_EverythingWatchPollは容量1で入場後すぐ実行候補へ取り出せる()
    {
        UiWorkSchedulerRuntime runtime = new(boundedCapacity: 1);
        UiWorkRequest request = UiWorkRequestPolicy.CreateEverythingWatchPollRequest();

        UiWorkSchedulerRuntimeQueueResult queued = runtime.Queue(request);
        UiWorkSchedulerRuntimeTakeResult next = runtime.TryTakeNext();

        Assert.Multiple(() =>
        {
            Assert.That(
                queued.Decision.Action,
                Is.EqualTo(UiWorkSchedulerAdmissionAction.Enqueue)
            );
            Assert.That(
                queued.Decision.AdmissionReason,
                Is.EqualTo(UiWorkSchedulerPolicy.AdmissionReasonQueued)
            );
            Assert.That(queued.PendingCount, Is.EqualTo(1));
            Assert.That(next.HasRequest, Is.True);
            Assert.That(
                next.PendingRequest.Request.LogReason,
                Is.EqualTo(UiWorkRequestPolicy.EverythingWatchPollLogReason)
            );
            Assert.That(
                next.PendingRequest.Request.Priority,
                Is.EqualTo(UiWorkPriority.WatchSmallDiff)
            );
            Assert.That(
                next.PendingRequest.Request.BoundedDrain,
                Is.EqualTo(UiWorkRequestPolicy.BoundedDrainCancellationToken)
            );
            Assert.That(next.PendingCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void Queue_WatchUiReloadは容量1で入場後すぐ実行候補へ取り出せる()
    {
        UiWorkSchedulerRuntime runtime = new(boundedCapacity: 1);
        UiWorkRequest request = UiWorkRequestPolicy.CreateWatchUiReloadRequest(
            useQueryOnlyReload: false
        );

        UiWorkSchedulerRuntimeQueueResult queued = runtime.Queue(request);
        UiWorkSchedulerRuntimeTakeResult next = runtime.TryTakeNext();

        Assert.Multiple(() =>
        {
            Assert.That(
                queued.Decision.Action,
                Is.EqualTo(UiWorkSchedulerAdmissionAction.Enqueue)
            );
            Assert.That(
                queued.Decision.AdmissionReason,
                Is.EqualTo(UiWorkSchedulerPolicy.AdmissionReasonQueued)
            );
            Assert.That(queued.PendingCount, Is.EqualTo(1));
            Assert.That(next.HasRequest, Is.True);
            Assert.That(
                next.PendingRequest.Request.LogReason,
                Is.EqualTo(UiWorkRequestPolicy.WatchUiReloadFullFallbackLogReason)
            );
            Assert.That(
                next.PendingRequest.Request.Priority,
                Is.EqualTo(UiWorkPriority.WatchReload)
            );
            Assert.That(
                next.PendingRequest.Request.BoundedDrain,
                Is.EqualTo(UiWorkRequestPolicy.BoundedDrainDeferredRequestCts)
            );
            Assert.That(next.PendingCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void Queue_KanaBackfillMovieViewRefreshは容量1で最新要求だけ残す()
    {
        UiWorkSchedulerRuntime runtime = new(boundedCapacity: 1);

        runtime.Queue(UiWorkRequestPolicy.CreateKanaBackfillMovieViewRefreshRequest());
        UiWorkSchedulerRuntimeQueueResult queued = runtime.Queue(
            UiWorkRequestPolicy.CreateKanaBackfillMovieViewRefreshRequest()
        );

        UiWorkSchedulerRuntimeTakeResult next = runtime.TryTakeNext();

        Assert.Multiple(() =>
        {
            Assert.That(
                queued.Decision.Action,
                Is.EqualTo(UiWorkSchedulerAdmissionAction.ReplaceLatestOnly)
            );
            Assert.That(
                queued.Decision.AdmissionReason,
                Is.EqualTo(UiWorkSchedulerPolicy.AdmissionReasonLatestOnlyReplaced)
            );
            Assert.That(queued.PendingCount, Is.EqualTo(1));
            Assert.That(next.HasRequest, Is.True);
            Assert.That(next.PendingRequest.Sequence, Is.EqualTo(2));
            Assert.That(
                next.PendingRequest.Request.LogReason,
                Is.EqualTo(UiWorkRequestPolicy.KanaBackfillMovieViewRefreshLogReason)
            );
            Assert.That(
                next.PendingRequest.Request.Priority,
                Is.EqualTo(UiWorkPriority.WatchSmallDiff)
            );
            Assert.That(next.PendingCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void ReleaseTimedOut_timeout有効要求だけを解放してログ語彙へ落とす()
    {
        UiWorkSchedulerRuntime runtime = new(boundedCapacity: 3);

        runtime.Queue(
            CreateRequest(
                UiWorkPriority.WatchReload,
                "watch-timeout",
                "watch-timeout",
                timeoutPolicy: "shutdown-drain:50ms"
            )
        );
        runtime.Queue(
            CreateRequest(
                UiWorkPriority.ThumbnailRefresh,
                "thumb-none",
                "thumb-none",
                timeoutPolicy: UiWorkRequestPolicy.TimeoutPolicyNone
            )
        );

        UiWorkSchedulerRuntimeDrainResult drain = runtime.ReleaseTimedOut(
            TimeSpan.FromMilliseconds(80),
            TimeSpan.FromMilliseconds(50)
        );

        Assert.Multiple(() =>
        {
            Assert.That(drain.QueueDepthBefore, Is.EqualTo(2));
            Assert.That(drain.QueueDepthAfter, Is.EqualTo(1));
            Assert.That(drain.ReleasedRequests, Has.Count.EqualTo(1));
            Assert.That(
                drain.ReleasedRequests[0].Decision.ReleaseReason,
                Is.EqualTo(UiWorkRequestPolicy.ReleaseReasonTimeout)
            );
            Assert.That(
                drain.ReleasedRequests[0].PendingRequest.Request.CoalesceKey,
                Is.EqualTo("watch-timeout")
            );
            Assert.That(drain.ReleasedRequests[0].LogFields, Does.Contain("log_reason=test.watchreload"));
            Assert.That(
                drain.ReleasedRequests[0].LogFields,
                Does.Contain(UiWorkSchedulerPolicy.SchedulerContractLogField)
            );
            Assert.That(drain.ReleasedRequests[0].LogFields, Does.Contain("release_reason=timeout"));
            Assert.That(
                drain.ReleasedRequests[0].LogFields,
                Does.Contain("bounded_drain=cancellation-token")
            );
            Assert.That(
                drain.ReleasedRequests[0].LogFields,
                Does.Contain("timeout_policy=shutdown-drain:50ms")
            );
            Assert.That(
                drain.ReleasedRequests[0].LogFields,
                Does.Contain("timeout_released=true")
            );
            Assert.That(drain.ReleasedRequests[0].LogFields, Does.Contain("timeout_elapsed_ms=80"));
            Assert.That(drain.ReleasedRequests[0].LogFields, Does.Contain("sequence=1"));
            Assert.That(
                drain.ReleasedRequests[0].LogFields,
                Does.Contain("pending_count_after=1")
            );
            Assert.That(runtime.PendingRequests.Single().Request.CoalesceKey, Is.EqualTo("thumb-none"));
        });
    }

    private static UiWorkRequest CreateRequest(
        UiWorkPriority priority,
        string coalesceKey,
        string latestOnlyKey,
        string timeoutPolicy = ""
    )
    {
        return new UiWorkRequest(
            priority,
            coalesceKey,
            latestOnlyKey,
            $"test.{priority.ToString().ToLowerInvariant()}",
            UiWorkRequestPolicy.BoundedDrainCancellationToken,
            timeoutPolicy
        );
    }
}
