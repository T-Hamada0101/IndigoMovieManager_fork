using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class DebugRuntimeLogPhase0NextActionPolicyTests
{
    [TestCase("startup-first-page")]
    [TestCase("startup-input-ready")]
    public void startup_2tokenのどちらかが欠けたらstartupだけを返す(string missingKey)
    {
        DebugRuntimeLogPhase0EvidenceSummary evidenceSummary = BuildEvidenceWithMissing(missingKey);

        DebugRuntimeLogPhase0NextActionSummary actionSummary =
            DebugRuntimeLogPhase0NextActionPolicy.Evaluate(evidenceSummary);

        Assert.Multiple(() =>
        {
            Assert.That(actionSummary.IsComplete, Is.False);
            Assert.That(actionSummary.ActionKeys, Is.EqualTo(["startup"]));
            Assert.That(actionSummary.BuildSummaryText(), Is.EqualTo("phase0_next_actions=startup"));
        });
    }

    [Test]
    public void workerとthumbnail_workerが両方欠けてもthumbnailだけを返す()
    {
        DebugRuntimeLogPhase0EvidenceSummary evidenceSummary = BuildEvidenceWithMissing(
            "worker",
            "thumbnail-worker"
        );

        DebugRuntimeLogPhase0NextActionSummary actionSummary =
            DebugRuntimeLogPhase0NextActionPolicy.Evaluate(evidenceSummary);

        Assert.That(actionSummary.ActionKeys, Is.EqualTo(["thumbnail"]));
    }

    [Test]
    public void 既存evidence_policyの結果からaction_summaryを作れる()
    {
        DebugRuntimeLogPhase0EvidenceSummary evidenceSummary =
            DebugRuntimeLogPhase0EvidencePolicy.Evaluate(
                [
                    "startup first-page shown",
                    "startup input ready",
                    "scroll ui shell input: operation_reason=scroll",
                    "player core_route=player-playback",
                    "watch core_route=watch-ui-apply",
                    "image image_contract=image-pipeline-v1",
                    "worker worker_contract=worker-job-v1",
                    "thumbnail worker_kind=thumbnail-create",
                    "skin core_route=skin-refresh",
                ]
            );

        DebugRuntimeLogPhase0NextActionSummary actionSummary =
            DebugRuntimeLogPhase0NextActionPolicy.Evaluate(evidenceSummary);

        Assert.That(actionSummary.ActionKeys, Is.EqualTo(["search", "sort", "persistence"]));
    }

    [Test]
    public void 全tokenがある時はnoneを返す()
    {
        DebugRuntimeLogPhase0EvidenceSummary evidenceSummary = BuildEvidenceWithMissing();

        DebugRuntimeLogPhase0NextActionSummary actionSummary =
            DebugRuntimeLogPhase0NextActionPolicy.Evaluate(evidenceSummary);

        Assert.Multiple(() =>
        {
            Assert.That(actionSummary.IsComplete, Is.True);
            Assert.That(actionSummary.ActionKeys, Is.Empty);
            Assert.That(actionSummary.BuildSummaryText(), Is.EqualTo("phase0_next_actions=none"));
        });
    }

    [Test]
    public void 不足keyの入力順に関係なく推奨順で返す()
    {
        DebugRuntimeLogPhase0EvidenceSummary evidenceSummary =
            new(
                12,
                [],
                [
                    "skin-core",
                    "thumbnail-worker",
                    "worker",
                    "persistence",
                    "image-pipeline",
                    "watch-core",
                    "player-core",
                    "scroll-input",
                    "sort-input",
                    "search-input",
                    "startup-input-ready",
                ]
            );

        DebugRuntimeLogPhase0NextActionSummary actionSummary =
            DebugRuntimeLogPhase0NextActionPolicy.Evaluate(evidenceSummary);

        Assert.That(
            actionSummary.BuildSummaryText(),
            Is.EqualTo(
                "phase0_next_actions=startup,search,sort,scroll,player,watch,image,persistence,thumbnail,skin"
            )
        );
    }

    [Test]
    public void 全採取対象guideは正本順で返す()
    {
        Assert.That(
            DebugRuntimeLogPhase0NextActionPolicy.BuildFullCaptureGuideText(),
            Is.EqualTo(
                "startup / search / sort / scroll / Player / watch / image / persistence / thumbnail / skin"
            )
        );
    }

    private static DebugRuntimeLogPhase0EvidenceSummary BuildEvidenceWithMissing(
        params string[] missingKeys
    )
    {
        HashSet<string> missingKeySet = new(missingKeys, StringComparer.Ordinal);

        // テストでは既存 evidence policy の入口から summary を作り、公開された不足keyだけを使う。
        string[] lines = AllEvidenceLines()
            .Where(pair => !missingKeySet.Contains(pair.Key))
            .Select(pair => pair.Value)
            .ToArray();

        return DebugRuntimeLogPhase0EvidencePolicy.Evaluate(lines);
    }

    private static KeyValuePair<string, string>[] AllEvidenceLines()
    {
        return
        [
            new("startup-first-page", "startup first-page shown"),
            new("startup-input-ready", "startup input ready"),
            new("search-input", "input ui shell input: operation_reason=search"),
            new("sort-input", "input ui shell input: operation_reason=sort"),
            new("scroll-input", "scroll ui shell input: operation_reason=scroll"),
            new("player-core", "player core_route=player-playback"),
            new("watch-core", "watch core_route=watch-ui-apply"),
            new("image-pipeline", "image image_contract=image-pipeline-v1"),
            new("persistence", "save persist_contract=persistence-write-v1"),
            new("worker", "worker worker_contract=worker-job-v1"),
            new("thumbnail-worker", "thumbnail worker_kind=thumbnail-create"),
            new("skin-core", "skin core_route=skin-refresh"),
        ];
    }
}
