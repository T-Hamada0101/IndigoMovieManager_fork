using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class DebugRuntimeLogPhase0EvidencePolicyTests
{
    [Test]
    public void 全tokenがあるとcompleteになる()
    {
        DebugRuntimeLogPhase0EvidenceSummary summary = DebugRuntimeLogPhase0EvidencePolicy.Evaluate(
            [
                "startup first-page shown",
                "startup input ready",
                "input ui shell input: operation_reason=search",
                "input ui shell input: operation_reason=sort",
                "player core_route=player-playback",
                "watch core_route=watch-ui-apply",
                "image image_contract=image-pipeline-v1",
                "save persist_contract=persistence-write-v1",
                "worker worker_contract=worker-job-v1",
                "skin core_route=skin-refresh",
            ]
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.TotalRequiredCount, Is.EqualTo(10));
            Assert.That(summary.ObservedCount, Is.EqualTo(10));
            Assert.That(summary.IsComplete, Is.True);
            Assert.That(summary.MissingKeys, Is.Empty);
            Assert.That(
                summary.ObservedKeys,
                Is.EqualTo(
                    [
                        "startup-first-page",
                        "startup-input-ready",
                        "search-input",
                        "sort-input",
                        "player-core",
                        "watch-core",
                        "image-pipeline",
                        "persistence",
                        "worker",
                        "skin-core",
                    ]
                )
            );
        });
    }

    [Test]
    public void 一部tokenが欠けるとmissing_keysが安定順で返る()
    {
        DebugRuntimeLogPhase0EvidenceSummary summary = DebugRuntimeLogPhase0EvidencePolicy.Evaluate(
            [
                "startup first-page shown",
                "input ui shell input: operation_reason=search",
                "watch core_route=watch-ui-apply",
                "save persist_contract=persistence-write-v1",
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
                        "startup-input-ready",
                        "sort-input",
                        "player-core",
                        "image-pipeline",
                        "worker",
                        "skin-core",
                    ]
                )
            );
            Assert.That(
                summary.ObservedKeys,
                Is.EqualTo(["startup-first-page", "search-input", "watch-core", "persistence"])
            );
        });
    }

    [Test]
    public void 同じtokenが複数回あってもobserved_countは重複カウントしない()
    {
        DebugRuntimeLogPhase0EvidenceSummary summary = DebugRuntimeLogPhase0EvidencePolicy.Evaluate(
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
            Assert.That(summary.ObservedKeys, Is.EqualTo(["image-pipeline", "worker"]));
        });
    }

    [Test]
    public void BuildSummaryTextはPhase0向けの短い文字列を返す()
    {
        DebugRuntimeLogPhase0EvidenceSummary completeSummary =
            DebugRuntimeLogPhase0EvidencePolicy.Evaluate(
                [
                    "startup first-page shown",
                    "startup input ready",
                    "input ui shell input: operation_reason=search",
                    "input ui shell input: operation_reason=sort",
                    "player core_route=player-playback",
                    "watch core_route=watch-ui-apply",
                    "image image_contract=image-pipeline-v1",
                    "save persist_contract=persistence-write-v1",
                    "worker worker_contract=worker-job-v1",
                    "skin core_route=skin-refresh",
                ]
            );
        DebugRuntimeLogPhase0EvidenceSummary missingSummary =
            DebugRuntimeLogPhase0EvidencePolicy.Evaluate(
                [
                    "startup first-page shown",
                    "watch core_route=watch-ui-apply",
                ]
            );

        Assert.Multiple(() =>
        {
            Assert.That(
                completeSummary.BuildSummaryText(),
                Is.EqualTo("phase0_log_evidence=10/10 missing=none")
            );
            Assert.That(
                missingSummary.BuildSummaryText(),
                Is.EqualTo(
                    "phase0_log_evidence=2/10 missing=startup-input-ready,search-input,sort-input,player-core,image-pipeline,persistence,worker,skin-core"
                )
            );
        });
    }
}
