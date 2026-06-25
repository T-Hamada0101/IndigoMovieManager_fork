using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class DebugRuntimeLogEvidencePolicyTests
{
    [Test]
    public void 全tokenがあるとcompleteになる()
    {
        DebugRuntimeLogEvidenceSummary summary = DebugRuntimeLogEvidencePolicy.Evaluate(
            [
                "startup ui_shell_contract=ui-shell-v1",
                "apply diff_contract=readmodel-diff-v1",
                "queue scheduler_contract=scheduler-v1",
                "decode image_contract=image-pipeline-v1",
                "save persist_contract=persistence-write-v1",
                "worker worker_contract=worker-job-v1",
                "skin core_route=skin-refresh",
                "player core_route=player-playback",
                "watch core_route=watch-ui-apply",
            ]
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.TotalRequiredCount, Is.EqualTo(9));
            Assert.That(summary.ObservedCount, Is.EqualTo(9));
            Assert.That(summary.IsComplete, Is.True);
            Assert.That(summary.MissingKeys, Is.Empty);
            Assert.That(
                summary.ObservedKeys,
                Is.EqualTo(
                    [
                        "ui-shell",
                        "readmodel-diff",
                        "scheduler",
                        "image",
                        "persistence",
                        "worker",
                        "skin-core",
                        "player-core",
                        "watch-core",
                    ]
                )
            );
        });
    }

    [Test]
    public void 一部tokenが欠けるとmissing_keysが安定順で返る()
    {
        DebugRuntimeLogEvidenceSummary summary = DebugRuntimeLogEvidencePolicy.Evaluate(
            [
                "startup ui_shell_contract=ui-shell-v1",
                "decode image_contract=image-pipeline-v1",
                "worker worker_contract=worker-job-v1",
                "watch core_route=watch-ui-apply",
            ]
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.ObservedCount, Is.EqualTo(4));
            Assert.That(summary.IsComplete, Is.False);
            Assert.That(
                summary.MissingKeys,
                Is.EqualTo(
                    [
                        "readmodel-diff",
                        "scheduler",
                        "persistence",
                        "skin-core",
                        "player-core",
                    ]
                )
            );
            Assert.That(
                summary.ObservedKeys,
                Is.EqualTo(["ui-shell", "image", "worker", "watch-core"])
            );
        });
    }

    [Test]
    public void 同じtokenが複数回あってもobserved_countは重複カウントしない()
    {
        DebugRuntimeLogEvidenceSummary summary = DebugRuntimeLogEvidencePolicy.Evaluate(
            [
                "first image_contract=image-pipeline-v1",
                "second image_contract=image-pipeline-v1",
                "third image_contract=image-pipeline-v1 worker_contract=worker-job-v1",
                "fourth worker_contract=worker-job-v1",
            ]
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.ObservedCount, Is.EqualTo(2));
            Assert.That(summary.ObservedKeys, Is.EqualTo(["image", "worker"]));
        });
    }

    [Test]
    public void BuildSummaryTextはgrepしやすい短い文字列を返す()
    {
        DebugRuntimeLogEvidenceSummary completeSummary = DebugRuntimeLogEvidencePolicy.Evaluate(
            [
                "startup ui_shell_contract=ui-shell-v1",
                "apply diff_contract=readmodel-diff-v1",
                "queue scheduler_contract=scheduler-v1",
                "decode image_contract=image-pipeline-v1",
                "save persist_contract=persistence-write-v1",
                "worker worker_contract=worker-job-v1",
                "skin core_route=skin-refresh",
                "player core_route=player-playback",
                "watch core_route=watch-ui-apply",
            ]
        );
        DebugRuntimeLogEvidenceSummary missingSummary = DebugRuntimeLogEvidencePolicy.Evaluate(
            [
                "startup ui_shell_contract=ui-shell-v1",
                "watch core_route=watch-ui-apply",
            ]
        );

        Assert.Multiple(() =>
        {
            Assert.That(
                completeSummary.BuildSummaryText(),
                Is.EqualTo("log_evidence=9/9 missing=none")
            );
            Assert.That(
                missingSummary.BuildSummaryText(),
                Is.EqualTo(
                    "log_evidence=2/9 missing=readmodel-diff,scheduler,image,persistence,worker,skin-core,player-core"
                )
            );
        });
    }
}
