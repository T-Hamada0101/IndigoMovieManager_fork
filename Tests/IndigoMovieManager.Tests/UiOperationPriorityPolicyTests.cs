using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using IndigoMovieManager;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UiOperationPriorityPolicyTests
{
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(false, false, false)]
    public void ShouldDeferBackgroundWork_手動以外の明示操作中だけ背後処理を逃がす(
        bool isUserPriorityActive,
        bool isManualMode,
        bool expected
    )
    {
        UiOperationSnapshot snapshot = new(
            IsUserPriorityActive: isUserPriorityActive,
            IsManualMode: isManualMode,
            IsWatchUiSuppressed: false,
            IsRecentViewportInteractionActive: false,
            IsPlayerPlaybackActive: false
        );

        bool actual = UiOperationPriorityPolicy.ShouldDeferBackgroundWork(snapshot);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void BuildSnapshotLogFields_UI入力状態を共通ログ語彙で返す()
    {
        UiOperationSnapshot snapshot = new(
            IsUserPriorityActive: true,
            IsManualMode: false,
            IsWatchUiSuppressed: true,
            IsRecentViewportInteractionActive: false,
            IsPlayerPlaybackActive: true
        );

        string fields = UiOperationPriorityPolicy.BuildSnapshotLogFields(snapshot);

        Assert.That(
            fields,
            Is.EqualTo(
                "is_user_priority_active=true is_manual_mode=false is_watch_ui_suppressed=true is_recent_viewport_active=false is_player_playback_active=true ui_shell_contract=ui-shell-v1"
            )
        );
        int firstContractIndex = fields.IndexOf(
            "ui_shell_contract=",
            StringComparison.Ordinal
        );
        int lastContractIndex = fields.LastIndexOf(
            "ui_shell_contract=",
            StringComparison.Ordinal
        );
        Assert.That(firstContractIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(lastContractIndex, Is.EqualTo(firstContractIndex));
    }

    [Test]
    public void SourcePolicy_UI入力ログcontractをhelperから戻さない()
    {
        string source = GetRepoText("Views", "Main", "UiOperationPriorityPolicy.cs");
        string buildMethod = ExtractMethod(
            source,
            "internal static string BuildSnapshotLogFields(UiOperationSnapshot snapshot)"
        );

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("internal const string UiShellContract = \"ui-shell-v1\";"));
            Assert.That(buildMethod, Does.Contain("ui_shell_contract={UiShellContract}"));
            Assert.That(buildMethod, Does.Not.Contain("ui-shell-v1"));
        });
    }

    [Test]
    public void ResolveEverythingPollDeferReason_UI抑止をuser_priorityより優先する()
    {
        UiOperationSnapshot snapshot = new(
            IsUserPriorityActive: true,
            IsManualMode: false,
            IsWatchUiSuppressed: true,
            IsRecentViewportInteractionActive: true,
            IsPlayerPlaybackActive: false
        );

        string reason = UiOperationPriorityPolicy.ResolveEverythingPollDeferReason(snapshot);

        Assert.That(reason, Is.EqualTo(UiOperationPriorityPolicy.DeferReasonUiSuppression));
    }

    [Test]
    public void ResolveEverythingPollDeferReason_recent_viewportはcatch_up不要の延期理由になる()
    {
        UiOperationSnapshot snapshot = new(
            IsUserPriorityActive: false,
            IsManualMode: false,
            IsWatchUiSuppressed: false,
            IsRecentViewportInteractionActive: true,
            IsPlayerPlaybackActive: false
        );

        string reason = UiOperationPriorityPolicy.ResolveEverythingPollDeferReason(snapshot);

        Assert.That(reason, Is.EqualTo(UiOperationPriorityPolicy.DeferReasonRecentViewport));
        Assert.That(
            UiOperationPriorityPolicy.ShouldQueueCatchUpForEverythingPollDefer(reason),
            Is.False
        );
    }

    [TestCase(UiOperationPriorityPolicy.DeferReasonUserPriority, true)]
    [TestCase(UiOperationPriorityPolicy.DeferReasonUiSuppression, true)]
    [TestCase(UiOperationPriorityPolicy.DeferReasonRecentViewport, false)]
    [TestCase(UiOperationPriorityPolicy.DeferReasonNone, false)]
    public void ShouldQueueCatchUpForEverythingPollDefer_recent_viewportだけcatch_upを積まない(
        string deferReason,
        bool expected
    )
    {
        bool actual = UiOperationPriorityPolicy.ShouldQueueCatchUpForEverythingPollDefer(
            deferReason
        );

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase(UiOperationPriorityPolicy.DeferReasonNone, true)]
    [TestCase(UiOperationPriorityPolicy.DeferReasonUserPriority, false)]
    [TestCase(UiOperationPriorityPolicy.DeferReasonUiSuppression, false)]
    [TestCase(UiOperationPriorityPolicy.DeferReasonRecentViewport, false)]
    public void ShouldProbeEverythingPollQueueLoad_延期中はqueue_probeしない(
        string deferReason,
        bool expected
    )
    {
        bool actual = UiOperationPriorityPolicy.ShouldProbeEverythingPollQueueLoad(deferReason);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void ShouldExtendEverythingPollDelay_recent_viewportと再生中はcalmへ寄せる()
    {
        UiOperationSnapshot recentViewport = new(
            IsUserPriorityActive: false,
            IsManualMode: false,
            IsWatchUiSuppressed: false,
            IsRecentViewportInteractionActive: true,
            IsPlayerPlaybackActive: false
        );
        string recentReason = UiOperationPriorityPolicy.ResolveEverythingPollDeferReason(
            recentViewport
        );

        UiOperationSnapshot playerPlayback = new(
            IsUserPriorityActive: false,
            IsManualMode: false,
            IsWatchUiSuppressed: false,
            IsRecentViewportInteractionActive: false,
            IsPlayerPlaybackActive: true
        );

        Assert.That(
            UiOperationPriorityPolicy.ShouldExtendEverythingPollDelay(
                recentViewport,
                recentReason
            ),
            Is.True
        );
        Assert.That(
            UiOperationPriorityPolicy.ShouldExtendEverythingPollDelay(
                playerPlayback,
                UiOperationPriorityPolicy.DeferReasonNone
            ),
            Is.True
        );
    }

    [TestCaseSource(nameof(UiOperationSnapshotCases))]
    public void UiOperationSnapshot_各UI操作を共通snapshotで優先判定する(
        string operationName,
        object snapshotObject,
        string expectedDeferReason,
        string expectedOperationReason,
        bool expectedCatchUp,
        bool expectedDelayExtension
    )
    {
        // public な NUnit 入口には internal 型を出せないため、ケース入力だけ object で受ける。
        UiOperationSnapshot snapshot = (UiOperationSnapshot)snapshotObject;
        string deferReason = UiOperationPriorityPolicy.ResolveEverythingPollDeferReason(
            snapshot
        );
        string operationReason = UiOperationPriorityPolicy.ResolveEverythingPollOperationReason(
            snapshot,
            deferReason
        );

        Assert.Multiple(() =>
        {
            Assert.That(deferReason, Is.EqualTo(expectedDeferReason), operationName);
            Assert.That(operationReason, Is.EqualTo(expectedOperationReason), operationName);
            Assert.That(
                UiOperationPriorityPolicy.ShouldQueueCatchUpForEverythingPollDefer(
                    deferReason
                ),
                Is.EqualTo(expectedCatchUp),
                operationName
            );
            Assert.That(
                UiOperationPriorityPolicy.ShouldExtendEverythingPollDelay(
                    snapshot,
                    deferReason
                ),
                Is.EqualTo(expectedDelayExtension),
                operationName
            );
        });
    }

    [Test]
    public void UiOperationPrioritySnapshot_旧名からUiOperationSnapshotへ変換できる()
    {
        UiOperationPrioritySnapshot legacySnapshot = new(
            IsUserPriorityActive: true,
            IsManualMode: false,
            IsWatchUiSuppressed: false,
            IsRecentViewportInteractionActive: false,
            IsPlayerPlaybackActive: false
        );

        UiOperationSnapshot snapshot = legacySnapshot;

        Assert.That(
            UiOperationPriorityPolicy.ResolveEverythingPollDeferReason(snapshot),
            Is.EqualTo(UiOperationPriorityPolicy.DeferReasonUserPriority)
        );
    }

    private static IEnumerable<TestCaseData> UiOperationSnapshotCases()
    {
        // 検索、sort、Player操作は user-priority として同じ入口から背後処理を逃がす。
        yield return BuildSnapshotCase(
            "search",
            new UiOperationSnapshot(true, false, false, false, false),
            UiOperationPriorityPolicy.DeferReasonUserPriority,
            UiOperationPriorityPolicy.DeferReasonUserPriority,
            expectedCatchUp: true,
            expectedDelayExtension: true
        );
        yield return BuildSnapshotCase(
            "sort",
            new UiOperationSnapshot(true, false, false, false, false),
            UiOperationPriorityPolicy.DeferReasonUserPriority,
            UiOperationPriorityPolicy.DeferReasonUserPriority,
            expectedCatchUp: true,
            expectedDelayExtension: true
        );
        yield return BuildSnapshotCase(
            "player-operation",
            new UiOperationSnapshot(true, false, false, false, false),
            UiOperationPriorityPolicy.DeferReasonUserPriority,
            UiOperationPriorityPolicy.DeferReasonUserPriority,
            expectedCatchUp: true,
            expectedDelayExtension: true
        );

        // viewport は軽く一周だけ見送り、解除後の catch-up は積まない。
        yield return BuildSnapshotCase(
            "viewport",
            new UiOperationSnapshot(false, false, false, true, false),
            UiOperationPriorityPolicy.DeferReasonRecentViewport,
            UiOperationPriorityPolicy.DeferReasonRecentViewport,
            expectedCatchUp: false,
            expectedDelayExtension: true
        );

        // manual reload は明示手動要求なので、UI抑止や viewport の札があっても背後扱いにしない。
        yield return BuildSnapshotCase(
            "manual-reload",
            new UiOperationSnapshot(true, true, true, true, false),
            UiOperationPriorityPolicy.DeferReasonNone,
            UiOperationPriorityPolicy.OperationReasonNormal,
            expectedCatchUp: false,
            expectedDelayExtension: false
        );

        // watch suppression は heavy watch を止め、解除後に一回だけ追いつかせる。
        yield return BuildSnapshotCase(
            "watch-suppression",
            new UiOperationSnapshot(false, false, true, false, false),
            UiOperationPriorityPolicy.DeferReasonUiSuppression,
            UiOperationPriorityPolicy.DeferReasonUiSuppression,
            expectedCatchUp: true,
            expectedDelayExtension: true
        );

        // 再生中は poll を止めず、wake-up 間隔だけ落ち着かせる。
        yield return BuildSnapshotCase(
            "playback",
            new UiOperationSnapshot(false, false, false, false, true),
            UiOperationPriorityPolicy.DeferReasonNone,
            UiOperationPriorityPolicy.OperationReasonPlayerPlayback,
            expectedCatchUp: false,
            expectedDelayExtension: true
        );
    }

    private static TestCaseData BuildSnapshotCase(
        string operationName,
        UiOperationSnapshot snapshot,
        string expectedDeferReason,
        string expectedOperationReason,
        bool expectedCatchUp,
        bool expectedDelayExtension
    )
    {
        return new TestCaseData(
            operationName,
            snapshot,
            expectedDeferReason,
            expectedOperationReason,
            expectedCatchUp,
            expectedDelayExtension
        ).SetName(
            $"UiOperationSnapshot_各UI操作を共通snapshotで優先判定する_{operationName}"
        );
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
