using System.IO;
using System.Runtime.CompilerServices;
using IndigoMovieManager;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UiWorkSchedulerPolicyTests
{
    [Test]
    public void EvaluateAdmission_空きがあればqueueへ入れる()
    {
        UiWorkRequest request = CreateRequest(UiWorkPriority.ThumbnailRefresh);

        UiWorkSchedulerAdmissionDecision decision = UiWorkSchedulerPolicy.EvaluateAdmission(
            request,
            Array.Empty<UiWorkSchedulerPendingRequest>(),
            boundedCapacity: 2
        );

        Assert.Multiple(() =>
        {
            Assert.That(decision.Accepted, Is.True);
            Assert.That(decision.Action, Is.EqualTo(UiWorkSchedulerAdmissionAction.Enqueue));
            Assert.That(decision.AdmissionReason, Is.EqualTo(UiWorkSchedulerPolicy.AdmissionReasonQueued));
            Assert.That(decision.QueueDepthBefore, Is.EqualTo(0));
            Assert.That(decision.QueueDepthAfter, Is.EqualTo(1));
            Assert.That(decision.ReleaseReason, Is.EqualTo(UiWorkRequestPolicy.ReleaseReasonAccepted));
            Assert.That(decision.TimeoutPolicy, Is.EqualTo(UiWorkRequestPolicy.TimeoutPolicyNone));
        });
    }

    [Test]
    public void EvaluateAdmission_latestOnlyKey一致なら最新要求で既存枠を置換する()
    {
        UiWorkRequest oldRequest = CreateRequest(
            UiWorkPriority.WatchSmallDiff,
            coalesceKey: "watch:coalesce",
            latestOnlyKey: "watch:latest"
        );
        UiWorkRequest newRequest = CreateRequest(
            UiWorkPriority.WatchReload,
            coalesceKey: "watch:coalesce",
            latestOnlyKey: "watch:latest"
        );

        UiWorkSchedulerAdmissionDecision decision = UiWorkSchedulerPolicy.EvaluateAdmission(
            newRequest,
            [new UiWorkSchedulerPendingRequest(1, oldRequest)],
            boundedCapacity: 1
        );

        Assert.Multiple(() =>
        {
            Assert.That(decision.Accepted, Is.True);
            Assert.That(
                decision.Action,
                Is.EqualTo(UiWorkSchedulerAdmissionAction.ReplaceLatestOnly)
            );
            Assert.That(
                decision.AdmissionReason,
                Is.EqualTo(UiWorkSchedulerPolicy.AdmissionReasonLatestOnlyReplaced)
            );
            Assert.That(decision.TargetIndex, Is.EqualTo(0));
            Assert.That(decision.QueueDepthAfter, Is.EqualTo(1));
            Assert.That(
                decision.ReplacedReleaseReason,
                Is.EqualTo(UiWorkRequestPolicy.ReleaseReasonCanceled)
            );
        });
    }

    [Test]
    public void EvaluateAdmission_coalesceKey一致ならqueueDepthを増やさず畳む()
    {
        UiWorkRequest oldRequest = CreateRequest(
            UiWorkPriority.ThumbnailRefresh,
            coalesceKey: "thumbnail:coalesce",
            latestOnlyKey: ""
        );
        UiWorkRequest newRequest = CreateRequest(
            UiWorkPriority.ThumbnailRefresh,
            coalesceKey: "thumbnail:coalesce",
            latestOnlyKey: ""
        );

        UiWorkSchedulerAdmissionDecision decision = UiWorkSchedulerPolicy.EvaluateAdmission(
            newRequest,
            [new UiWorkSchedulerPendingRequest(7, oldRequest)],
            boundedCapacity: 1
        );

        Assert.Multiple(() =>
        {
            Assert.That(decision.Accepted, Is.True);
            Assert.That(
                decision.Action,
                Is.EqualTo(UiWorkSchedulerAdmissionAction.ReplaceCoalesced)
            );
            Assert.That(
                decision.AdmissionReason,
                Is.EqualTo(UiWorkSchedulerPolicy.AdmissionReasonCoalesced)
            );
            Assert.That(decision.TargetIndex, Is.EqualTo(0));
            Assert.That(decision.QueueDepthBefore, Is.EqualTo(1));
            Assert.That(decision.QueueDepthAfter, Is.EqualTo(1));
        });
    }

    [Test]
    public void EvaluateAdmission_満杯でも高優先要求は低優先枠を押し出す()
    {
        UiWorkRequest thumbnail = CreateRequest(UiWorkPriority.ThumbnailRefresh, "thumb", "thumb");
        UiWorkRequest skin = CreateRequest(UiWorkPriority.SkinCatalog, "skin", "skin");
        UiWorkRequest input = CreateRequest(UiWorkPriority.Input, "input", "input");

        UiWorkSchedulerAdmissionDecision decision = UiWorkSchedulerPolicy.EvaluateAdmission(
            input,
            [
                new UiWorkSchedulerPendingRequest(1, thumbnail),
                new UiWorkSchedulerPendingRequest(2, skin),
            ],
            boundedCapacity: 2
        );

        Assert.Multiple(() =>
        {
            Assert.That(decision.Accepted, Is.True);
            Assert.That(
                decision.Action,
                Is.EqualTo(UiWorkSchedulerAdmissionAction.PreemptLowerPriority)
            );
            Assert.That(
                decision.AdmissionReason,
                Is.EqualTo(UiWorkSchedulerPolicy.AdmissionReasonPriorityPreempted)
            );
            Assert.That(decision.TargetIndex, Is.EqualTo(1));
            Assert.That(decision.QueueDepthAfter, Is.EqualTo(2));
            Assert.That(
                decision.ReplacedReleaseReason,
                Is.EqualTo(UiWorkRequestPolicy.ReleaseReasonCanceled)
            );
        });
    }

    [Test]
    public void EvaluateAdmission_満杯で同等以下の優先度なら拒否する()
    {
        UiWorkRequest input = CreateRequest(UiWorkPriority.Input, "input", "input");
        UiWorkRequest selection = CreateRequest(UiWorkPriority.Selection, "selection", "selection");
        UiWorkRequest thumbnail = CreateRequest(UiWorkPriority.ThumbnailRefresh, "thumb", "thumb");

        UiWorkSchedulerAdmissionDecision decision = UiWorkSchedulerPolicy.EvaluateAdmission(
            thumbnail,
            [
                new UiWorkSchedulerPendingRequest(1, input),
                new UiWorkSchedulerPendingRequest(2, selection),
            ],
            boundedCapacity: 2
        );

        Assert.Multiple(() =>
        {
            Assert.That(decision.Accepted, Is.False);
            Assert.That(
                decision.Action,
                Is.EqualTo(UiWorkSchedulerAdmissionAction.RejectCapacityFull)
            );
            Assert.That(decision.SkipReason, Is.EqualTo(UiWorkSchedulerPolicy.RejectReasonCapacityFull));
            Assert.That(decision.QueueDepthAfter, Is.EqualTo(2));
            Assert.That(decision.ReleaseReason, Is.EqualTo(UiWorkRequestPolicy.ReleaseReasonRejected));
        });
    }

    [Test]
    public void SelectNextToRun_優先度が高く同優先なら古い要求を選ぶ()
    {
        UiWorkRequest thumbnail = CreateRequest(UiWorkPriority.ThumbnailRefresh, "thumb", "thumb");
        UiWorkRequest inputNewer = CreateRequest(UiWorkPriority.Input, "input-new", "input-new");
        UiWorkRequest inputOlder = CreateRequest(UiWorkPriority.Input, "input-old", "input-old");

        UiWorkSchedulerNextRequestDecision decision = UiWorkSchedulerPolicy.SelectNextToRun(
            [
                new UiWorkSchedulerPendingRequest(1, thumbnail),
                new UiWorkSchedulerPendingRequest(3, inputNewer),
                new UiWorkSchedulerPendingRequest(2, inputOlder),
            ]
        );

        Assert.Multiple(() =>
        {
            Assert.That(decision.HasRequest, Is.True);
            Assert.That(decision.Index, Is.EqualTo(2));
            Assert.That(decision.Request.CoalesceKey, Is.EqualTo("input-old"));
            Assert.That(decision.Reason, Is.EqualTo(UiWorkSchedulerPolicy.AdmissionReasonNextSelected));
        });
    }

    [Test]
    public void EvaluateTimeout_noneはbudget超過でもreleaseしない()
    {
        UiWorkRequest request = CreateRequest(
            UiWorkPriority.WatchSmallDiff,
            timeoutPolicy: UiWorkRequestPolicy.TimeoutPolicyNone
        );

        UiWorkSchedulerTimeoutDecision decision = UiWorkSchedulerPolicy.EvaluateTimeout(
            request,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(1)
        );

        Assert.Multiple(() =>
        {
            Assert.That(decision.ShouldRelease, Is.False);
            Assert.That(decision.ReleaseReason, Is.EqualTo(UiWorkRequestPolicy.AcceptReasonNone));
            Assert.That(decision.TimeoutPolicy, Is.EqualTo(UiWorkRequestPolicy.TimeoutPolicyNone));
            Assert.That(decision.ElapsedMs, Is.EqualTo(10000));
            Assert.That(decision.TimeoutMs, Is.EqualTo(1000));
        });
    }

    [Test]
    public void EvaluateTimeout_timeoutPolicy有効時はbudget超過でtimeout解放にする()
    {
        UiWorkRequest request = CreateRequest(
            UiWorkPriority.WatchSmallDiff,
            timeoutPolicy: "watch-ui:500ms"
        );

        UiWorkSchedulerTimeoutDecision decision = UiWorkSchedulerPolicy.EvaluateTimeout(
            request,
            TimeSpan.FromMilliseconds(750),
            TimeSpan.FromMilliseconds(500)
        );

        Assert.Multiple(() =>
        {
            Assert.That(decision.ShouldRelease, Is.True);
            Assert.That(decision.ReleaseReason, Is.EqualTo(UiWorkRequestPolicy.ReleaseReasonTimeout));
            Assert.That(decision.TimeoutPolicy, Is.EqualTo("watch-ui:500ms"));
            Assert.That(decision.ElapsedMs, Is.EqualTo(750));
            Assert.That(decision.TimeoutMs, Is.EqualTo(500));
        });
    }

    [Test]
    public void BuildAdmissionLogFields_入場判定語彙を共通形式で出す()
    {
        UiWorkRequest request = CreateRequest(UiWorkPriority.Input, "input", "input");
        UiWorkSchedulerAdmissionDecision decision = UiWorkSchedulerPolicy.EvaluateAdmission(
            request,
            Array.Empty<UiWorkSchedulerPendingRequest>(),
            boundedCapacity: 1
        );

        string logFields = UiWorkSchedulerPolicy.BuildAdmissionLogFields(request, decision);

        AssertSchedulerContractOnce(logFields);
        Assert.That(logFields, Does.Contain("log_reason=test.input"));
        Assert.That(logFields, Does.Contain("release_reason=accepted"));
        Assert.That(logFields, Does.Contain("work_priority=Input"));
        Assert.That(logFields, Does.Contain("admission_action=Enqueue"));
        Assert.That(logFields, Does.Contain("accepted=True"));
        Assert.That(logFields, Does.Contain("target_index=-1"));
        Assert.That(logFields, Does.Contain("admission_reason=queued"));
        Assert.That(logFields, Does.Contain("skip_reason=none"));
        Assert.That(logFields, Does.Contain("queue_depth_before=0"));
        Assert.That(logFields, Does.Contain("queue_depth_after=1"));
        Assert.That(logFields, Does.Contain("bounded_capacity=1"));
        Assert.That(logFields, Does.Contain("queue_capacity=1"));
        Assert.That(logFields, Does.Contain("replaced_release_reason=none"));

        UiWorkSchedulerAdmissionDecision replaceDecision =
            UiWorkSchedulerPolicy.EvaluateAdmission(
                request,
                [new UiWorkSchedulerPendingRequest(1, request)],
                boundedCapacity: 1
            );
        string replaceLogFields = UiWorkSchedulerPolicy.BuildAdmissionLogFields(
            request,
            replaceDecision
        );

        AssertSchedulerContractOnce(replaceLogFields);
        Assert.That(replaceLogFields, Does.Contain("admission_action=ReplaceLatestOnly"));
        Assert.That(replaceLogFields, Does.Contain("accepted=True"));
        Assert.That(replaceLogFields, Does.Contain("target_index=0"));

        UiWorkSchedulerAdmissionDecision rejectDecision =
            UiWorkSchedulerPolicy.EvaluateAdmission(
                request,
                [new UiWorkSchedulerPendingRequest(1, request)],
                boundedCapacity: 0
            );
        string rejectLogFields = UiWorkSchedulerPolicy.BuildAdmissionLogFields(
            request,
            rejectDecision
        );

        AssertSchedulerContractOnce(rejectLogFields);
        Assert.That(rejectLogFields, Does.Contain("admission_action=RejectCapacityDisabled"));
        Assert.That(rejectLogFields, Does.Contain("accepted=False"));
        Assert.That(rejectLogFields, Does.Contain("target_index=-1"));
    }

    [Test]
    public void BuildTimeoutLogFields_timeout語彙を共通形式で出す()
    {
        UiWorkSchedulerTimeoutDecision decision = new(
            ShouldRelease: true,
            ReleaseReason: UiWorkRequestPolicy.ReleaseReasonTimeout,
            TimeoutPolicy: "watch-ui:500ms",
            ElapsedMs: 750,
            TimeoutMs: 500
        );

        string logFields = UiWorkSchedulerPolicy.BuildTimeoutLogFields(decision);

        AssertSchedulerContractOnce(logFields);
        Assert.That(logFields, Does.Contain("release_reason=timeout"));
        Assert.That(logFields, Does.Contain("timeout_policy=watch-ui:500ms"));
        Assert.That(logFields, Does.Contain("timeout_released=true"));
        Assert.That(logFields, Does.Contain("timeout_elapsed_ms=750"));
        Assert.That(logFields, Does.Contain("timeout_budget_ms=500"));
    }

    [Test]
    public void BuildTimeoutLogFields_timeout未解放をfalseで出す()
    {
        UiWorkSchedulerTimeoutDecision decision = new(
            ShouldRelease: false,
            ReleaseReason: UiWorkRequestPolicy.AcceptReasonNone,
            TimeoutPolicy: UiWorkRequestPolicy.TimeoutPolicyNone,
            ElapsedMs: 750,
            TimeoutMs: 500
        );

        string logFields = UiWorkSchedulerPolicy.BuildTimeoutLogFields(decision);

        AssertSchedulerContractOnce(logFields);
        Assert.That(logFields, Does.Contain("release_reason=none"));
        Assert.That(logFields, Does.Contain("timeout_policy=none"));
        Assert.That(logFields, Does.Contain("timeout_released=false"));
        Assert.That(logFields, Does.Contain("timeout_elapsed_ms=750"));
        Assert.That(logFields, Does.Contain("timeout_budget_ms=500"));
    }

    [Test]
    public void BuildTimeoutReleaseLogFields_timeout解放のpending状態を共通形式で出す()
    {
        UiWorkRequest request = CreateRequest(
            UiWorkPriority.WatchReload,
            "watch-timeout",
            "watch-timeout",
            timeoutPolicy: "watch-ui:500ms"
        );
        UiWorkSchedulerPendingRequest pendingRequest = new(17, request);
        UiWorkSchedulerTimeoutDecision decision = new(
            ShouldRelease: true,
            ReleaseReason: UiWorkRequestPolicy.ReleaseReasonTimeout,
            TimeoutPolicy: "watch-ui:500ms",
            ElapsedMs: 750,
            TimeoutMs: 500
        );

        string logFields = UiWorkSchedulerPolicy.BuildTimeoutReleaseLogFields(
            pendingRequest,
            decision,
            pendingCountAfter: 4
        );
        string clampedLogFields = UiWorkSchedulerPolicy.BuildTimeoutReleaseLogFields(
            pendingRequest,
            decision,
            pendingCountAfter: -2
        );

        AssertSchedulerContractOnce(logFields);
        AssertSchedulerContractOnce(clampedLogFields);
        Assert.That(logFields, Does.Contain("log_reason=test.watchreload"));
        Assert.That(logFields, Does.Contain("release_reason=timeout"));
        Assert.That(logFields, Does.Contain("work_priority=WatchReload"));
        Assert.That(logFields, Does.Contain("coalesce_key='watch-timeout'"));
        Assert.That(logFields, Does.Contain("timeout_policy=watch-ui:500ms"));
        Assert.That(logFields, Does.Contain("timeout_released=true"));
        Assert.That(logFields, Does.Contain("sequence=17"));
        Assert.That(logFields, Does.Contain("pending_count_after=4"));
        Assert.That(clampedLogFields, Does.Contain("pending_count_after=0"));
    }

    [Test]
    public void BuildTakeLogFields_pendingから既存実行入口へ渡した証跡を共通形式で出す()
    {
        UiWorkRequest request = CreateRequest(UiWorkPriority.WatchSmallDiff, "watch", "watch");
        UiWorkSchedulerPendingRequest pendingRequest = new(42, request);
        UiWorkSchedulerNextRequestDecision decision = new(
            HasRequest: true,
            Index: 1,
            Request: request,
            Reason: UiWorkSchedulerPolicy.AdmissionReasonNextSelected
        );

        string logFields = UiWorkSchedulerPolicy.BuildTakeLogFields(
            pendingRequest,
            decision,
            pendingCountAfter: 0,
            UiWorkRequestPolicy.ReleaseReasonReleased
        );

        AssertSchedulerContractOnce(logFields);
        Assert.That(logFields, Does.Contain("log_reason=test.watchsmalldiff"));
        Assert.That(logFields, Does.Contain("release_reason=released"));
        Assert.That(logFields, Does.Contain("work_priority=WatchSmallDiff"));
        Assert.That(logFields, Does.Contain("sequence=42"));
        Assert.That(logFields, Does.Contain("has_request=True"));
        Assert.That(logFields, Does.Contain("next_reason=next-selected"));
        Assert.That(logFields, Does.Contain("selected_index=1"));
        Assert.That(logFields, Does.Contain("pending_count_after=0"));
    }

    [Test]
    public void SourcePolicy_主要ログbuilderはscheduler_contractを共通fieldから一度だけ出す()
    {
        string source = GetRepoText("Views", "Main", "UiWorkSchedulerPolicy.cs");
        string admissionMethod = ExtractMethod(
            source,
            "internal static string BuildAdmissionLogFields("
        );
        string takeMethod = ExtractMethod(source, "internal static string BuildTakeLogFields(");
        string timeoutMethod = ExtractMethod(
            source,
            "internal static string BuildTimeoutLogFields(UiWorkSchedulerTimeoutDecision decision)"
        );
        string timeoutReleaseMethod = ExtractMethod(
            source,
            "internal static string BuildTimeoutReleaseLogFields("
        );

        Assert.Multiple(() =>
        {
            Assert.That(
                source,
                Does.Contain(
                    "internal const string SchedulerContractLogField = \"scheduler_contract=scheduler-v1\";"
                )
            );
            Assert.That(admissionMethod, Does.Contain("{SchedulerContractLogField}"));
            Assert.That(takeMethod, Does.Contain("{SchedulerContractLogField}"));
            Assert.That(timeoutMethod, Does.Contain("{SchedulerContractLogField}"));
            Assert.That(timeoutReleaseMethod, Does.Contain("BuildTimeoutLogFields(decision)"));
            Assert.That(timeoutReleaseMethod, Does.Not.Contain("SchedulerContractLogField"));
            Assert.That(timeoutReleaseMethod, Does.Not.Contain("scheduler_contract="));
            Assert.That(CountOccurrences(admissionMethod, "SchedulerContractLogField"), Is.EqualTo(1));
            Assert.That(CountOccurrences(takeMethod, "SchedulerContractLogField"), Is.EqualTo(1));
            Assert.That(CountOccurrences(timeoutMethod, "SchedulerContractLogField"), Is.EqualTo(1));
        });
    }

    private static UiWorkRequest CreateRequest(
        UiWorkPriority priority,
        string coalesceKey = "test:coalesce",
        string latestOnlyKey = "test:latest",
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

    private static void AssertSchedulerContractOnce(string logFields)
    {
        Assert.That(
            CountOccurrences(logFields, UiWorkSchedulerPolicy.SchedulerContractLogField),
            Is.EqualTo(1)
        );
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while (index < text.Length)
        {
            int foundIndex = text.IndexOf(value, index, StringComparison.Ordinal);
            if (foundIndex < 0)
            {
                break;
            }

            count++;
            index = foundIndex + value.Length;
        }

        return count;
    }

    private static string GetRepoText(params string[] relativePathParts)
    {
        foreach (DirectoryInfo searchRoot in EnumerateRepoSearchRoots())
        {
            DirectoryInfo? current = searchRoot;
            while (current != null)
            {
                string candidate = Path.Combine([current.FullName, .. relativePathParts]);
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }

                current = current.Parent;
            }
        }

        Assert.Fail($"{Path.Combine(relativePathParts)} の位置を repo root から解決できませんでした。");
        return "";
    }

    private static IEnumerable<DirectoryInfo> EnumerateRepoSearchRoots(
        [CallerFilePath] string callerFilePath = ""
    )
    {
        string? callerDirectory = Path.GetDirectoryName(callerFilePath);
        if (!string.IsNullOrWhiteSpace(callerDirectory))
        {
            yield return new DirectoryInfo(callerDirectory);
        }

        yield return new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        yield return new DirectoryInfo(TestContext.CurrentContext.WorkDirectory);
        yield return new DirectoryInfo(Directory.GetCurrentDirectory());
    }

    private static string ExtractMethod(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{signature} が見つかりません。");

        int bodyStart = source.IndexOf('{', start);
        Assert.That(bodyStart, Is.GreaterThanOrEqualTo(0), $"{signature} の本文開始が見つかりません。");

        int depth = 0;
        for (int i = bodyStart; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(start, i - start + 1);
                }
            }
        }

        Assert.Fail($"{signature} の本文終端が見つかりません。");
        return "";
    }
}
