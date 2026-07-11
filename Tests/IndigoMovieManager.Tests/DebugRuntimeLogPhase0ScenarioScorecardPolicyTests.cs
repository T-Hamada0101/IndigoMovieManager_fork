using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class DebugRuntimeLogPhase0ScenarioScorecardPolicyTests
{
    [Test]
    public void 既存evidenceが揃うと主要8シナリオのlog_evidenceが完了する()
    {
        DebugRuntimeLogPhase0ScenarioScorecard scorecard = Evaluate(AllScenarioEvidenceLines());

        Assert.Multiple(() =>
        {
            Assert.That(
                scorecard.Scenarios.Select(scenario => scenario.Key),
                Is.EqualTo([
                    "startup",
                    "search-sort-scroll",
                    "tab-selection-page",
                    "watch-small-diff",
                    "player",
                    "image",
                    "skin",
                    "persistence-shutdown",
                ])
            );
            Assert.That(scorecard.LogEvidenceCompleteCount, Is.EqualTo(8));
            Assert.That(scorecard.IsLogEvidenceComplete, Is.True);
            Assert.That(scorecard.ManualVisualReviewRequired, Is.True);
            Assert.That(scorecard.IsPhase0Complete, Is.False);
            Assert.That(
                scorecard.Scenarios,
                Has.All.Property("ManualVisualReviewRequired").EqualTo(true)
            );
        });
    }

    [Test]
    public void 不足keyと観測済みdetailをシナリオ単位で返す()
    {
        DebugRuntimeLogPhase0ScenarioScorecard scorecard = Evaluate([
            "input ui shell input: operation_reason=search ui_shell_contract=ui-shell-v1 is_user_priority_active=True",
            "input ui shell input: operation_reason=upper-tab-switch",
            "apply diff_contract=readmodel-diff-v1 diff_change_set=single",
        ]);

        DebugRuntimeLogPhase0ScenarioScore search = scorecard.GetScenario("search-sort-scroll");
        DebugRuntimeLogPhase0ScenarioScore tab = scorecard.GetScenario("tab-selection-page");

        Assert.Multiple(() =>
        {
            Assert.That(search.LogEvidenceComplete, Is.False);
            Assert.That(
                search.MissingKeys,
                Is.EqualTo(["scheduler", "sort-input", "scroll-input"])
            );
            Assert.That(
                search.ObservedDetailKeys,
                Is.EqualTo(["ui-shell-user-priority-active", "readmodel-diff-single"])
            );
            Assert.That(tab.LogEvidenceComplete, Is.False);
            Assert.That(tab.MissingKeys, Is.EqualTo(["log-tab-switch-input"]));
            Assert.That(
                tab.ObservedDetailKeys,
                Is.EqualTo(["upper-tab-switch-input", "ui-shell-user-priority-active"])
            );
        });
    }

    [Test]
    public void tab_selection_pageはlogとselection_focus_blank目視を分離する()
    {
        DebugRuntimeLogPhase0ScenarioScorecard scorecard = Evaluate([
            "input ui shell input: operation_reason=search ui_shell_contract=ui-shell-v1",
            "input ui shell input: operation_reason=upper-tab-switch",
            "input ui shell input: operation_reason=log-tab-switch",
        ]);

        DebugRuntimeLogPhase0ScenarioScore tab = scorecard.GetScenario("tab-selection-page");

        Assert.Multiple(() =>
        {
            Assert.That(tab.LogEvidenceComplete, Is.True);
            Assert.That(tab.MissingKeys, Is.Empty);
            Assert.That(
                tab.ObservedDetailKeys,
                Is.EqualTo(["upper-tab-switch-input", "log-tab-switch-input"])
            );
            Assert.That(tab.ManualVisualReviewRequired, Is.True);
            Assert.That(
                tab.ManualVisualReviewKeys,
                Is.EqualTo(["selection", "focus", "page-or-scroll-position", "blank", "focus-not-stolen"])
            );
            Assert.That(scorecard.IsPhase0Complete, Is.False);
        });
    }

    [Test]
    public void Phase1の操作連続性目視キーを主要3シナリオへ追加する()
    {
        DebugRuntimeLogPhase0ScenarioScorecard scorecard = Evaluate([]);

        Assert.Multiple(() =>
        {
            Assert.That(
                scorecard.GetScenario("search-sort-scroll").ManualVisualReviewKeys,
                Is.EqualTo([
                    "input-continuity",
                    "selection",
                    "focus",
                    "scroll",
                    "blank",
                    "multi-selection",
                    "scroll-anchor",
                    "operation-feedback",
                    "continued-input-during-feedback",
                ])
            );
            Assert.That(
                scorecard.GetScenario("player").ManualVisualReviewKeys,
                Is.EqualTo(["playback-continuity", "selection", "focus", "blank", "operation-feedback"])
            );
        });
    }

    [Test]
    public void BuildSummaryTextはlogと目視を安定した別行で返す()
    {
        DebugRuntimeLogPhase0ScenarioScorecard scorecard = Evaluate([
            "input ui shell input: operation_reason=search ui_shell_contract=ui-shell-v1",
            "input ui shell input: operation_reason=upper-tab-switch",
            "input ui shell input: operation_reason=log-tab-switch",
        ]);

        string[] lines = scorecard.BuildSummaryText().Split(Environment.NewLine);

        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Length.EqualTo(3));
            Assert.That(
                lines[0],
                Is.EqualTo(
                    "phase0_scenario_log_evidence=1/8 missing_scenarios=startup,search-sort-scroll,watch-small-diff,player,image,skin,persistence-shutdown"
                )
            );
            Assert.That(
                lines[1],
                Does.Contain(
                    "tab-selection-page{complete=true,missing=none,details=upper-tab-switch-input,log-tab-switch-input}"
                )
            );
            Assert.That(
                lines[2],
                Does.StartWith("phase0_manual_visual_review=required phase0_complete=false checks=")
            );
            Assert.That(
                lines[2],
                Does.Contain("tab-selection-page=selection,focus,page-or-scroll-position,blank,focus-not-stolen")
            );
        });
    }

    [Test]
    public void startupはcold_start_evidenceだけで完了する()
    {
        DebugRuntimeLogPhase0ScenarioScorecard scorecard = Evaluate([
            "startup first-page shown",
            "startup input ready",
        ]);

        DebugRuntimeLogPhase0ScenarioScore startup = scorecard.GetScenario("startup");

        Assert.Multiple(() =>
        {
            Assert.That(startup.LogEvidenceComplete, Is.True);
            Assert.That(startup.MissingKeys, Is.Empty);
            Assert.That(scorecard.GetScenario("search-sort-scroll").LogEvidenceComplete, Is.False);
        });
    }

    [Test]
    public void persistence_shutdownはpersistence_evidenceだけで完了する()
    {
        DebugRuntimeLogPhase0ScenarioScorecard scorecard = Evaluate([
            "save persist_contract=persistence-write-v1",
        ]);

        DebugRuntimeLogPhase0ScenarioScore persistence = scorecard.GetScenario(
            "persistence-shutdown"
        );

        Assert.Multiple(() =>
        {
            Assert.That(persistence.LogEvidenceComplete, Is.True);
            Assert.That(persistence.MissingKeys, Is.Empty);
            Assert.That(persistence.ObservedDetailKeys, Is.Empty);
        });
    }

    private static DebugRuntimeLogPhase0ScenarioScorecard Evaluate(IEnumerable<string> lines)
    {
        string[] snapshot = lines.ToArray();
        return DebugRuntimeLogPhase0ScenarioScorecardPolicy.Evaluate(
            DebugRuntimeLogEvidencePolicy.Evaluate(snapshot),
            DebugRuntimeLogPhase0EvidencePolicy.Evaluate(snapshot)
        );
    }

    private static string[] AllScenarioEvidenceLines()
    {
        return
        [
            "startup first-page shown",
            "startup input ready",
            "input ui shell input: operation_reason=search ui_shell_contract=ui-shell-v1 is_user_priority_active=True is_recent_viewport_active=True is_player_playback_active=True",
            "input ui shell input: operation_reason=sort",
            "scroll page scroll end:",
            "input ui shell input: operation_reason=upper-tab-switch",
            "input ui shell input: operation_reason=log-tab-switch",
            "apply diff_contract=readmodel-diff-v1 diff_change_set=single diff_changed_total=1 diff_source_revision=2 diff_view_revision=3 diff_full_fallback_reason=none",
            "queue scheduler_contract=scheduler-v1 accepted=True target_index=0 has_request=True timeout_released=False pending_count_after=0",
            "watch core_route=watch-ui-apply watch_apply_kind=query-only watch_reason=small-diff",
            "player core_route=player-playback player_surface_ready=True player_transition=play",
            "image image_contract=image-pipeline-v1 image_log_reason=image.thumbnail-error-list.aggregate-decode-plan failure_reason=stale-image-request",
            "worker worker_contract=worker-job-v1 diagnostic_context_count=1 capability_count=1 metric_count=1",
            "thumbnail worker_kind=thumbnail-create",
            "skin core_route=skin-refresh operation_reason=skin.host-refresh definition_mode=cached",
            "save persist_contract=persistence-write-v1 write_succeeded=True persist_state=clean dirty=False failed=False retryable=False notify_ui=False",
        ];
    }
}
