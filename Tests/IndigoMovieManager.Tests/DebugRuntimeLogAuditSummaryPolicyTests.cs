using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class DebugRuntimeLogAuditSummaryPolicyTests
{
    [Test]
    public void 複数runがある場合は最新runだけでevidenceを集計する()
    {
        string oldFirst = BuildLine(1, "old ui_shell_contract=ui-shell-v1");
        string oldSecond = BuildLine(2, "old diff_contract=readmodel-diff-v1");
        string newFirst = BuildLine(1, "new first-page shown");
        string newSecond = BuildLine(2, "new core_route=watch-ui-apply");

        DebugRuntimeLogAuditSummary summary = DebugRuntimeLogAuditSummaryPolicy.Evaluate(
            [oldFirst, oldSecond, newFirst, newSecond]
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.RunSlice.Lines, Is.EqualTo([newFirst, newSecond]));
            Assert.That(summary.RunSlice.DetectedResetCount, Is.EqualTo(1));
            Assert.That(summary.ContractEvidence.ObservedKeys, Is.EqualTo(["watch-core"]));
            Assert.That(
                summary.ContractEvidence.MissingKeys,
                Does.Contain("ui-shell").And.Contain("readmodel-diff")
            );
            Assert.That(
                summary.Phase0Evidence.ObservedKeys,
                Is.EqualTo(["startup-first-page", "watch-core"])
            );
        });
    }

    [Test]
    public void 全tokenが最新runにある時はmissing_noneになる()
    {
        DebugRuntimeLogAuditSummary summary = DebugRuntimeLogAuditSummaryPolicy.Evaluate(
            BuildSequencedLines(AllEvidenceMessages())
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.ContractEvidence.IsComplete, Is.True);
            Assert.That(summary.Phase0Evidence.IsComplete, Is.True);
            Assert.That(summary.ContractEvidence.BuildSummaryText(), Is.EqualTo("log_evidence=9/9 missing=none"));
            Assert.That(
                summary.Phase0Evidence.BuildSummaryText(),
                Is.EqualTo("phase0_log_evidence=12/12 missing=none")
            );
        });
    }

    [Test]
    public void 欠けがある時も3行の順序と文言が安定する()
    {
        DebugRuntimeLogAuditSummary summary = DebugRuntimeLogAuditSummaryPolicy.Evaluate(
            BuildSequencedLines(
                [
                    "startup first-page shown",
                    "watch core_route=watch-ui-apply",
                ]
            )
        );

        Assert.That(
            summary.BuildSummaryText(),
            Is.EqualTo(
                string.Join(
                    Environment.NewLine,
                    "log_run_lines=2/2 has_sequence=true sequence=1-2 resets=0",
                    "log_evidence=1/9 missing=ui-shell,readmodel-diff,scheduler,image,persistence,worker,skin-core,player-core",
                    "phase0_log_evidence=2/12 missing=startup-input-ready,search-input,sort-input,scroll-input,player-core,image-pipeline,persistence,worker,thumbnail-worker,skin-core"
                )
            )
        );
    }

    [Test]
    public void 空入力でも既存policyのsummaryと整合する()
    {
        DebugRuntimeLogAuditSummary summary = DebugRuntimeLogAuditSummaryPolicy.Evaluate([]);
        DebugRuntimeLogRunSliceResult expectedRunSlice =
            DebugRuntimeLogRunSlicePolicy.SliceLatestRun([]);
        DebugRuntimeLogEvidenceSummary expectedContractEvidence =
            DebugRuntimeLogEvidencePolicy.Evaluate(expectedRunSlice.Lines);
        DebugRuntimeLogPhase0EvidenceSummary expectedPhase0Evidence =
            DebugRuntimeLogPhase0EvidencePolicy.Evaluate(expectedRunSlice.Lines);

        Assert.Multiple(() =>
        {
            Assert.That(summary.RunSlice.BuildSummaryText(), Is.EqualTo(expectedRunSlice.BuildSummaryText()));
            Assert.That(
                summary.ContractEvidence.BuildSummaryText(),
                Is.EqualTo(expectedContractEvidence.BuildSummaryText())
            );
            Assert.That(
                summary.Phase0Evidence.BuildSummaryText(),
                Is.EqualTo(expectedPhase0Evidence.BuildSummaryText())
            );
            Assert.That(
                summary.BuildSummaryText(),
                Is.EqualTo(
                    string.Join(
                        Environment.NewLine,
                        "log_run_lines=0/0 has_sequence=false sequence=none resets=0",
                        "log_evidence=0/9 missing=ui-shell,readmodel-diff,scheduler,image,persistence,worker,skin-core,player-core,watch-core",
                        "phase0_log_evidence=0/12 missing=startup-first-page,startup-input-ready,search-input,sort-input,scroll-input,player-core,watch-core,image-pipeline,persistence,worker,thumbnail-worker,skin-core"
                    )
                )
            );
        });
    }

    private static string[] BuildSequencedLines(IReadOnlyList<string> messages)
    {
        return messages.Select((message, index) => BuildLine(index + 1, message)).ToArray();
    }

    private static string[] AllEvidenceMessages()
    {
        return
        [
            "startup first-page shown",
            "startup input ready",
            "input ui shell input: operation_reason=search ui_shell_contract=ui-shell-v1",
            "input ui shell input: operation_reason=sort",
            "scroll page scroll end:",
            "apply diff_contract=readmodel-diff-v1",
            "queue scheduler_contract=scheduler-v1",
            "image image_contract=image-pipeline-v1",
            "save persist_contract=persistence-write-v1",
            "worker worker_contract=worker-job-v1",
            "thumbnail worker_kind=thumbnail-create",
            "skin core_route=skin-refresh",
            "player core_route=player-playback",
            "watch core_route=watch-ui-apply",
        ];
    }

    private static string BuildLine(long sequence, string message)
    {
        return DebugRuntimeLog.BuildLineForTesting(
            new DateTime(2026, 6, 25, 10, 0, 0).AddMilliseconds(sequence),
            "ui-tempo",
            message,
            sequence
        );
    }
}
