using System;
using System.Collections.Generic;

namespace IndigoMovieManager;

// 実行器へ進む前に、入場・置換・timeout の判断だけを小さく固定する。
internal readonly record struct UiWorkSchedulerPendingRequest(
    long Sequence,
    UiWorkRequest Request
);

internal readonly record struct UiWorkSchedulerAdmissionDecision(
    UiWorkSchedulerAdmissionAction Action,
    bool Accepted,
    int TargetIndex,
    string AdmissionReason,
    string SkipReason,
    string ReleaseReason,
    string ReplacedReleaseReason,
    int QueueDepthBefore,
    int QueueDepthAfter,
    int BoundedCapacity,
    string TimeoutPolicy
);

internal readonly record struct UiWorkSchedulerNextRequestDecision(
    bool HasRequest,
    int Index,
    UiWorkRequest Request,
    string Reason
);

internal readonly record struct UiWorkSchedulerTimeoutDecision(
    bool ShouldRelease,
    string ReleaseReason,
    string TimeoutPolicy,
    long ElapsedMs,
    long TimeoutMs
);

internal enum UiWorkSchedulerAdmissionAction
{
    Enqueue,
    ReplaceLatestOnly,
    ReplaceCoalesced,
    PreemptLowerPriority,
    RejectCapacityDisabled,
    RejectCapacityFull,
}

internal static class UiWorkSchedulerPolicy
{
    internal const string AdmissionReasonQueued = "queued";
    internal const string AdmissionReasonLatestOnlyReplaced = "latest-only-replaced";
    internal const string AdmissionReasonCoalesced = "coalesced";
    internal const string AdmissionReasonPriorityPreempted = "priority-preempted";
    internal const string AdmissionReasonNextSelected = "next-selected";
    internal const string AdmissionReasonNoPending = "no-pending";
    internal const string RejectReasonCapacityDisabled = "capacity-disabled";
    internal const string RejectReasonCapacityFull = "capacity-full";

    internal static UiWorkSchedulerAdmissionDecision EvaluateAdmission(
        UiWorkRequest request,
        IReadOnlyList<UiWorkSchedulerPendingRequest> pendingRequests,
        int boundedCapacity
    )
    {
        pendingRequests ??= Array.Empty<UiWorkSchedulerPendingRequest>();
        int depthBefore = pendingRequests.Count;

        if (boundedCapacity <= 0)
        {
            return Reject(
                UiWorkSchedulerAdmissionAction.RejectCapacityDisabled,
                request,
                RejectReasonCapacityDisabled,
                depthBefore,
                boundedCapacity
            );
        }

        // latest-only は「古い同種要求を捨て、最新だけ残す」ため容量判定より先に見る。
        int latestOnlyIndex = FindLatestOnlyReplacementIndex(request, pendingRequests);
        if (latestOnlyIndex >= 0)
        {
            return AcceptReplacement(
                UiWorkSchedulerAdmissionAction.ReplaceLatestOnly,
                request,
                latestOnlyIndex,
                AdmissionReasonLatestOnlyReplaced,
                depthBefore,
                boundedCapacity
            );
        }

        // coalesce は同じ作業枠へ畳み込み、queue depth を増やさない。
        int coalesceIndex = FindCoalesceReplacementIndex(request, pendingRequests);
        if (coalesceIndex >= 0)
        {
            return AcceptReplacement(
                UiWorkSchedulerAdmissionAction.ReplaceCoalesced,
                request,
                coalesceIndex,
                AdmissionReasonCoalesced,
                depthBefore,
                boundedCapacity
            );
        }

        if (depthBefore < boundedCapacity)
        {
            return new UiWorkSchedulerAdmissionDecision(
                Action: UiWorkSchedulerAdmissionAction.Enqueue,
                Accepted: true,
                TargetIndex: -1,
                AdmissionReason: AdmissionReasonQueued,
                SkipReason: UiWorkRequestPolicy.AcceptReasonNone,
                ReleaseReason: UiWorkRequestPolicy.ReleaseReasonAccepted,
                ReplacedReleaseReason: UiWorkRequestPolicy.AcceptReasonNone,
                QueueDepthBefore: depthBefore,
                QueueDepthAfter: depthBefore + 1,
                BoundedCapacity: boundedCapacity,
                TimeoutPolicy: NormalizeTimeoutPolicy(request.TimeoutPolicy)
            );
        }

        // 満杯でも、ユーザー操作に近い要求だけは低優先の背後処理を1枠押し出せる。
        int preemptIndex = FindLowerPriorityPreemptionIndex(request, pendingRequests);
        if (preemptIndex >= 0)
        {
            return AcceptReplacement(
                UiWorkSchedulerAdmissionAction.PreemptLowerPriority,
                request,
                preemptIndex,
                AdmissionReasonPriorityPreempted,
                depthBefore,
                boundedCapacity
            );
        }

        return Reject(
            UiWorkSchedulerAdmissionAction.RejectCapacityFull,
            request,
            RejectReasonCapacityFull,
            depthBefore,
            boundedCapacity
        );
    }

    internal static UiWorkSchedulerNextRequestDecision SelectNextToRun(
        IReadOnlyList<UiWorkSchedulerPendingRequest> pendingRequests
    )
    {
        pendingRequests ??= Array.Empty<UiWorkSchedulerPendingRequest>();
        if (pendingRequests.Count == 0)
        {
            return new UiWorkSchedulerNextRequestDecision(
                HasRequest: false,
                Index: -1,
                Request: default,
                Reason: AdmissionReasonNoPending
            );
        }

        int selectedIndex = 0;
        for (int i = 1; i < pendingRequests.Count; i++)
        {
            UiWorkSchedulerPendingRequest selected = pendingRequests[selectedIndex];
            UiWorkSchedulerPendingRequest candidate = pendingRequests[i];
            if (ShouldRunBefore(candidate, selected))
            {
                selectedIndex = i;
            }
        }

        return new UiWorkSchedulerNextRequestDecision(
            HasRequest: true,
            Index: selectedIndex,
            Request: pendingRequests[selectedIndex].Request,
            Reason: AdmissionReasonNextSelected
        );
    }

    internal static UiWorkSchedulerTimeoutDecision EvaluateTimeout(
        UiWorkRequest request,
        TimeSpan elapsed,
        TimeSpan timeoutBudget
    )
    {
        string timeoutPolicy = NormalizeTimeoutPolicy(request.TimeoutPolicy);
        long elapsedMs = Math.Max(0, (long)elapsed.TotalMilliseconds);
        long timeoutMs = Math.Max(0, (long)timeoutBudget.TotalMilliseconds);
        bool timeoutEnabled =
            !string.Equals(timeoutPolicy, UiWorkRequestPolicy.TimeoutPolicyNone, StringComparison.Ordinal)
            && timeoutMs > 0;
        bool shouldRelease = timeoutEnabled && elapsedMs >= timeoutMs;

        return new UiWorkSchedulerTimeoutDecision(
            ShouldRelease: shouldRelease,
            ReleaseReason: shouldRelease
                ? UiWorkRequestPolicy.ReleaseReasonTimeout
                : UiWorkRequestPolicy.AcceptReasonNone,
            TimeoutPolicy: timeoutPolicy,
            ElapsedMs: elapsedMs,
            TimeoutMs: timeoutMs
        );
    }

    internal static string BuildAdmissionLogFields(
        UiWorkRequest request,
        UiWorkSchedulerAdmissionDecision decision
    )
    {
        return
            $"{UiWorkRequestPolicy.BuildRequestSchedulerLogFields(request, decision.ReleaseReason)} admission_action={decision.Action} admission_reason={decision.AdmissionReason} skip_reason={decision.SkipReason} queue_depth_before={decision.QueueDepthBefore} queue_depth_after={decision.QueueDepthAfter} bounded_capacity={decision.BoundedCapacity} queue_capacity={decision.BoundedCapacity} replaced_release_reason={decision.ReplacedReleaseReason}";
    }

    internal static string BuildTimeoutLogFields(UiWorkSchedulerTimeoutDecision decision)
    {
        return
            $"release_reason={decision.ReleaseReason} timeout_policy={decision.TimeoutPolicy} timeout_elapsed_ms={decision.ElapsedMs} timeout_budget_ms={decision.TimeoutMs}";
    }

    private static UiWorkSchedulerAdmissionDecision AcceptReplacement(
        UiWorkSchedulerAdmissionAction action,
        UiWorkRequest request,
        int targetIndex,
        string admissionReason,
        int depthBefore,
        int boundedCapacity
    )
    {
        return new UiWorkSchedulerAdmissionDecision(
            Action: action,
            Accepted: true,
            TargetIndex: targetIndex,
            AdmissionReason: admissionReason,
            SkipReason: UiWorkRequestPolicy.AcceptReasonNone,
            ReleaseReason: UiWorkRequestPolicy.ReleaseReasonAccepted,
            ReplacedReleaseReason: UiWorkRequestPolicy.ReleaseReasonCanceled,
            QueueDepthBefore: depthBefore,
            QueueDepthAfter: depthBefore,
            BoundedCapacity: boundedCapacity,
            TimeoutPolicy: NormalizeTimeoutPolicy(request.TimeoutPolicy)
        );
    }

    private static UiWorkSchedulerAdmissionDecision Reject(
        UiWorkSchedulerAdmissionAction action,
        UiWorkRequest request,
        string skipReason,
        int depthBefore,
        int boundedCapacity
    )
    {
        return new UiWorkSchedulerAdmissionDecision(
            Action: action,
            Accepted: false,
            TargetIndex: -1,
            AdmissionReason: skipReason,
            SkipReason: skipReason,
            ReleaseReason: UiWorkRequestPolicy.ReleaseReasonRejected,
            ReplacedReleaseReason: UiWorkRequestPolicy.AcceptReasonNone,
            QueueDepthBefore: depthBefore,
            QueueDepthAfter: depthBefore,
            BoundedCapacity: boundedCapacity,
            TimeoutPolicy: NormalizeTimeoutPolicy(request.TimeoutPolicy)
        );
    }

    private static int FindLatestOnlyReplacementIndex(
        UiWorkRequest request,
        IReadOnlyList<UiWorkSchedulerPendingRequest> pendingRequests
    )
    {
        if (!request.HasLatestOnlyKey)
        {
            return -1;
        }

        for (int i = pendingRequests.Count - 1; i >= 0; i--)
        {
            if (
                string.Equals(
                    pendingRequests[i].Request.LatestOnlyKey,
                    request.LatestOnlyKey,
                    StringComparison.Ordinal
                )
            )
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindCoalesceReplacementIndex(
        UiWorkRequest request,
        IReadOnlyList<UiWorkSchedulerPendingRequest> pendingRequests
    )
    {
        if (!request.HasCoalesceKey)
        {
            return -1;
        }

        for (int i = pendingRequests.Count - 1; i >= 0; i--)
        {
            if (
                string.Equals(
                    pendingRequests[i].Request.CoalesceKey,
                    request.CoalesceKey,
                    StringComparison.Ordinal
                )
            )
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindLowerPriorityPreemptionIndex(
        UiWorkRequest request,
        IReadOnlyList<UiWorkSchedulerPendingRequest> pendingRequests
    )
    {
        int selectedIndex = -1;
        for (int i = 0; i < pendingRequests.Count; i++)
        {
            UiWorkSchedulerPendingRequest candidate = pendingRequests[i];
            if (candidate.Request.Priority <= request.Priority)
            {
                continue;
            }

            if (
                selectedIndex < 0
                || IsWorsePreemptionTarget(candidate, pendingRequests[selectedIndex])
            )
            {
                selectedIndex = i;
            }
        }

        return selectedIndex;
    }

    private static bool IsWorsePreemptionTarget(
        UiWorkSchedulerPendingRequest candidate,
        UiWorkSchedulerPendingRequest current
    )
    {
        if (candidate.Request.Priority != current.Request.Priority)
        {
            return candidate.Request.Priority > current.Request.Priority;
        }

        return candidate.Sequence > current.Sequence;
    }

    private static bool ShouldRunBefore(
        UiWorkSchedulerPendingRequest candidate,
        UiWorkSchedulerPendingRequest current
    )
    {
        if (candidate.Request.Priority != current.Request.Priority)
        {
            return candidate.Request.Priority < current.Request.Priority;
        }

        return candidate.Sequence < current.Sequence;
    }

    private static string NormalizeTimeoutPolicy(string timeoutPolicy)
    {
        return string.IsNullOrWhiteSpace(timeoutPolicy)
            ? UiWorkRequestPolicy.TimeoutPolicyNone
            : timeoutPolicy;
    }
}
