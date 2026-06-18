using System;
using System.Collections.Generic;

namespace IndigoMovieManager;

internal readonly record struct UiWorkSchedulerRuntimeQueueResult(
    UiWorkSchedulerAdmissionDecision Decision,
    int PendingCount
);

internal readonly record struct UiWorkSchedulerRuntimeTakeResult(
    bool HasRequest,
    UiWorkSchedulerPendingRequest PendingRequest,
    UiWorkSchedulerNextRequestDecision Decision,
    int PendingCount
);

internal readonly record struct UiWorkSchedulerRuntimeTimedOutRelease(
    UiWorkSchedulerPendingRequest PendingRequest,
    UiWorkSchedulerTimeoutDecision Decision,
    string LogFields
);

internal readonly record struct UiWorkSchedulerRuntimeDrainResult(
    int QueueDepthBefore,
    int QueueDepthAfter,
    IReadOnlyList<UiWorkSchedulerRuntimeTimedOutRelease> ReleasedRequests
);

// 実行やスレッド管理へ進まず、policyの判定をpending状態へ反映するだけの最小runtime。
internal sealed class UiWorkSchedulerRuntime
{
    private readonly List<UiWorkSchedulerPendingRequest> _pendingRequests = [];
    private readonly int _boundedCapacity;
    private long _nextSequence;

    internal UiWorkSchedulerRuntime(int boundedCapacity)
    {
        _boundedCapacity = boundedCapacity;
    }

    internal int PendingCount => _pendingRequests.Count;

    internal IReadOnlyList<UiWorkSchedulerPendingRequest> PendingRequests =>
        _pendingRequests.ToArray();

    internal UiWorkSchedulerRuntimeQueueResult Queue(UiWorkRequest request)
    {
        UiWorkSchedulerAdmissionDecision decision = UiWorkSchedulerPolicy.EvaluateAdmission(
            request,
            _pendingRequests,
            _boundedCapacity
        );

        if (decision.Accepted)
        {
            UiWorkSchedulerPendingRequest pendingRequest = new(++_nextSequence, request);
            if (decision.TargetIndex >= 0)
            {
                _pendingRequests[decision.TargetIndex] = pendingRequest;
            }
            else
            {
                _pendingRequests.Add(pendingRequest);
            }
        }

        return new UiWorkSchedulerRuntimeQueueResult(decision, _pendingRequests.Count);
    }

    internal UiWorkSchedulerRuntimeTakeResult TryTakeNext()
    {
        UiWorkSchedulerNextRequestDecision decision = UiWorkSchedulerPolicy.SelectNextToRun(
            _pendingRequests
        );

        if (!decision.HasRequest)
        {
            return new UiWorkSchedulerRuntimeTakeResult(
                HasRequest: false,
                PendingRequest: default,
                Decision: decision,
                PendingCount: _pendingRequests.Count
            );
        }

        UiWorkSchedulerPendingRequest pendingRequest = _pendingRequests[decision.Index];
        _pendingRequests.RemoveAt(decision.Index);
        return new UiWorkSchedulerRuntimeTakeResult(
            HasRequest: true,
            PendingRequest: pendingRequest,
            Decision: decision,
            PendingCount: _pendingRequests.Count
        );
    }

    internal UiWorkSchedulerRuntimeDrainResult ReleaseTimedOut(
        TimeSpan elapsed,
        TimeSpan timeoutBudget
    )
    {
        int depthBefore = _pendingRequests.Count;
        List<UiWorkSchedulerRuntimeTimedOutRelease> releasedRequests = [];

        // 後ろから外して、未評価のindexを動かさない。
        for (int i = _pendingRequests.Count - 1; i >= 0; i--)
        {
            UiWorkSchedulerPendingRequest pendingRequest = _pendingRequests[i];
            UiWorkSchedulerTimeoutDecision timeoutDecision = UiWorkSchedulerPolicy.EvaluateTimeout(
                pendingRequest.Request,
                elapsed,
                timeoutBudget
            );
            if (!timeoutDecision.ShouldRelease)
            {
                continue;
            }

            string logFields =
                $"{UiWorkRequestPolicy.BuildRequestSchedulerLogFields(pendingRequest.Request, timeoutDecision.ReleaseReason)} {UiWorkSchedulerPolicy.BuildTimeoutLogFields(timeoutDecision)}";
            releasedRequests.Add(
                new UiWorkSchedulerRuntimeTimedOutRelease(
                    pendingRequest,
                    timeoutDecision,
                    logFields
                )
            );
            _pendingRequests.RemoveAt(i);
        }

        releasedRequests.Reverse();
        return new UiWorkSchedulerRuntimeDrainResult(
            depthBefore,
            _pendingRequests.Count,
            releasedRequests.ToArray()
        );
    }
}
