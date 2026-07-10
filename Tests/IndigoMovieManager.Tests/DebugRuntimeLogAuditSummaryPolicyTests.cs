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
            Assert.That(summary.RunWindow.TimestampLineCount, Is.EqualTo(2));
            Assert.That(summary.ContractEvidence.ObservedKeys, Is.EqualTo(["watch-core"]));
            Assert.That(
                summary.ContractEvidence.MissingKeys,
                Does.Contain("ui-shell").And.Contain("readmodel-diff")
            );
            Assert.That(
                summary.Phase0Evidence.ObservedKeys,
                Is.EqualTo(["startup-first-page", "watch-core"])
            );
            Assert.That(
                summary.Phase0NextActions.ActionKeys,
                Is.EqualTo(
                    [
                        "startup",
                        "search",
                        "sort",
                        "scroll",
                        "player",
                        "image",
                        "persistence",
                        "thumbnail",
                        "skin",
                    ]
                )
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
            Assert.That(summary.Phase0NextActions.IsComplete, Is.True);
            Assert.That(summary.IsComplete, Is.True);
            Assert.That(summary.AuditStatusKey, Is.EqualTo("complete"));
            Assert.That(summary.Phase0ScenarioScorecard.IsLogEvidenceComplete, Is.False);
            Assert.That(summary.Phase0ScenarioScorecard.IsPhase0Complete, Is.False);
            Assert.That(summary.ContractEvidence.BuildSummaryText(), Is.EqualTo("log_evidence=9/9 missing=none"));
            Assert.That(
                summary.Phase0Evidence.BuildSummaryText(),
                Is.EqualTo("phase0_log_evidence=12/12 missing=none")
            );
            Assert.That(
                summary.BuildSummaryText().Split(Environment.NewLine).Last(),
                Is.EqualTo("phase0_audit_complete=true")
            );
        });
    }

    [Test]
    public void Phase0_next_actionsがnoneでもcontract不足ならcompleteにしない()
    {
        string[] messages = AllEvidenceMessages()
            .Where(message =>
                !message.Contains("diff_contract", StringComparison.Ordinal)
                && !message.Contains("scheduler_contract", StringComparison.Ordinal)
            )
            .ToArray();
        DebugRuntimeLogAuditSummary summary = DebugRuntimeLogAuditSummaryPolicy.Evaluate(
            BuildSequencedLines(messages)
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.ContractEvidence.IsComplete, Is.False);
            Assert.That(summary.Phase0Evidence.IsComplete, Is.True);
            Assert.That(summary.Phase0NextActions.IsComplete, Is.True);
            Assert.That(summary.IsComplete, Is.False);
            Assert.That(summary.AuditStatusKey, Is.EqualTo("missing-contract-evidence"));
            Assert.That(
                summary.BuildSummaryText().Split(Environment.NewLine).Last(),
                Is.EqualTo("phase0_audit_complete=false")
            );
        });
    }

    [Test]
    public void timestampが無い時はevidence完了でもstatusはmissing_timestampになる()
    {
        DebugRuntimeLogAuditSummary summary = DebugRuntimeLogAuditSummaryPolicy.Evaluate(
            AllEvidenceMessages()
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.ContractEvidence.IsComplete, Is.True);
            Assert.That(summary.Phase0Evidence.IsComplete, Is.True);
            Assert.That(summary.IsComplete, Is.True);
            Assert.That(summary.AuditStatusKey, Is.EqualTo("missing-timestamp"));
            Assert.That(
                summary.BuildSummaryText(),
                Does.Contain("phase0_audit_status=missing-timestamp")
            );
            Assert.That(
                summary.BuildSummaryText().Split(Environment.NewLine).Last(),
                Is.EqualTo("phase0_audit_complete=true")
            );
        });
    }

    [Test]
    public void contract完了でPhase0必須不足ならstatusはmissing_phase0_evidenceになる()
    {
        string[] messages =
        [
            "input ui shell input: operation_reason=search ui_shell_contract=ui-shell-v1",
            "apply diff_contract=readmodel-diff-v1",
            "queue scheduler_contract=scheduler-v1",
            "image image_contract=image-pipeline-v1",
            "save persist_contract=persistence-write-v1",
            "worker worker_contract=worker-job-v1",
            "skin core_route=skin-refresh",
            "player core_route=player-playback",
            "watch core_route=watch-ui-apply",
        ];
        DebugRuntimeLogAuditSummary summary = DebugRuntimeLogAuditSummaryPolicy.Evaluate(
            BuildSequencedLines(messages)
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.ContractEvidence.IsComplete, Is.True);
            Assert.That(summary.Phase0Evidence.IsComplete, Is.False);
            Assert.That(summary.IsComplete, Is.False);
            Assert.That(summary.AuditStatusKey, Is.EqualTo("missing-phase0-evidence"));
            Assert.That(
                summary.BuildSummaryText(),
                Does.Contain("phase0_audit_status=missing-phase0-evidence")
            );
        });
    }

    [Test]
    public void manual_reload_inputはoptional_evidenceとしてsummaryに残る()
    {
        DebugRuntimeLogAuditSummary summary = DebugRuntimeLogAuditSummaryPolicy.Evaluate(
            BuildSequencedLines(["input ui shell input: operation_reason=manual-reload"])
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.Phase0Evidence.ObservedCount, Is.EqualTo(0));
            Assert.That(
                summary.Phase0Evidence.OptionalObservedKeys,
                Is.EqualTo(["manual-reload-input"])
            );
            Assert.That(summary.Phase0Evidence.IsComplete, Is.False);
            Assert.That(
                summary.Phase0Evidence.BuildSummaryText(),
                Does.Contain("optional_evidence=1/35 optional=manual-reload-input")
            );
            Assert.That(
                summary.Phase0Evidence.BuildSummaryText(),
                Does.EndWith("optional=manual-reload-input")
            );
            Assert.That(summary.Phase0NextActions.ActionKeys, Does.Contain("search"));
        });
    }

    [Test]
    public void tab_switch_inputはoptional_evidenceとしてsummaryに残る()
    {
        DebugRuntimeLogAuditSummary summary = DebugRuntimeLogAuditSummaryPolicy.Evaluate(
            BuildSequencedLines(
                [
                    "input ui shell input: operation_reason=upper-tab-switch",
                    "input ui shell input: operation_reason=log-tab-switch",
                ]
            )
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.Phase0Evidence.ObservedCount, Is.EqualTo(0));
            Assert.That(
                summary.Phase0Evidence.OptionalObservedKeys,
                Is.EqualTo(["upper-tab-switch-input", "log-tab-switch-input"])
            );
            Assert.That(summary.Phase0Evidence.IsComplete, Is.False);
            Assert.That(
                summary.Phase0Evidence.BuildSummaryText(),
                Does.Contain(
                    "optional_evidence=2/35 optional=upper-tab-switch-input,log-tab-switch-input"
                )
            );
            Assert.That(
                summary.Phase0Evidence.BuildSummaryText(),
                Does.EndWith("optional=upper-tab-switch-input,log-tab-switch-input")
            );
            Assert.That(summary.Phase0NextActions.ActionKeys, Does.Contain("search"));
        });
    }

    [Test]
    public void ui_shell_snapshot_detail補助evidenceはsummaryに残る()
    {
        DebugRuntimeLogAuditSummary summary = DebugRuntimeLogAuditSummaryPolicy.Evaluate(
            BuildSequencedLines(
                [
                    "snapshot ui_shell_contract=ui-shell-v1 is_user_priority_active=True is_manual_mode=False is_watch_ui_suppressed=False is_recent_viewport_active=True is_player_playback_active=False",
                ]
            )
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.Phase0Evidence.ObservedCount, Is.EqualTo(0));
            Assert.That(
                summary.Phase0Evidence.OptionalObservedKeys,
                Is.EqualTo(
                    [
                        "ui-shell-user-priority-active",
                        "ui-shell-manual-mode",
                        "ui-shell-watch-suppressed",
                        "ui-shell-recent-viewport-active",
                        "ui-shell-player-playback-active",
                    ]
                )
            );
            Assert.That(summary.Phase0Evidence.IsComplete, Is.False);
            Assert.That(
                summary.Phase0Evidence.BuildSummaryText(),
                Does.Contain(
                    "optional_evidence=5/35 optional=ui-shell-user-priority-active,ui-shell-manual-mode,ui-shell-watch-suppressed,ui-shell-recent-viewport-active,ui-shell-player-playback-active"
                )
            );
            Assert.That(summary.Phase0NextActions.ActionKeys, Does.Contain("search"));
        });
    }

    [Test]
    public void ui_shell_snapshot_detail候補だけの行はsummaryに残さない()
    {
        DebugRuntimeLogAuditSummary summary = DebugRuntimeLogAuditSummaryPolicy.Evaluate(
            BuildSequencedLines(
                [
                    "snapshot is_user_priority_active=True is_manual_mode=False is_watch_ui_suppressed=False is_recent_viewport_active=True is_player_playback_active=False",
                    "snapshot ui_shell_contract=ui-shell-v1",
                ]
            )
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.Phase0Evidence.ObservedKeys, Is.Empty);
            Assert.That(summary.Phase0Evidence.OptionalObservedKeys, Is.Empty);
            Assert.That(
                summary.Phase0Evidence.BuildSummaryText(),
                Does.Not.Contain("optional=")
            );
        });
    }

    [Test]
    public void readmodel_diff詳細補助evidenceはsummaryに残る()
    {
        DebugRuntimeLogAuditSummary summary = DebugRuntimeLogAuditSummaryPolicy.Evaluate(
            BuildSequencedLines(
                [
                    "watch diff_contract=readmodel-diff-v1 diff_change_set=single diff_changed_total=1",
                    "apply diff_contract=readmodel-diff-v1 diff_full_fallback_reason=none diff_source_revision=10 diff_view_revision=11 diff_changed_total=120",
                ]
            )
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.Phase0Evidence.ObservedCount, Is.EqualTo(0));
            Assert.That(
                summary.Phase0Evidence.OptionalObservedKeys,
                Is.EqualTo(
                    [
                        "readmodel-diff-single",
                        "readmodel-diff-total",
                        "readmodel-diff-source-revision",
                        "readmodel-diff-view-revision",
                        "readmodel-diff-full-fallback-reason",
                    ]
                )
            );
            Assert.That(summary.Phase0Evidence.IsComplete, Is.False);
            Assert.That(
                summary.Phase0Evidence.BuildSummaryText(),
                Does.EndWith(
                    "optional=readmodel-diff-single,readmodel-diff-total,readmodel-diff-source-revision,readmodel-diff-view-revision,readmodel-diff-full-fallback-reason"
                )
            );
            Assert.That(summary.Phase0NextActions.ActionKeys, Does.Contain("watch"));
        });
    }

    [Test]
    public void scheduler_detail補助evidenceはsummaryに残る()
    {
        DebugRuntimeLogAuditSummary summary = DebugRuntimeLogAuditSummaryPolicy.Evaluate(
            BuildSequencedLines(
                [
                    "queue scheduler_contract=scheduler-v1 admission_action=enqueued accepted=True target_index=-1",
                    "queue scheduler_contract=scheduler-v1 sequence=1 has_request=True pending_count_after=0",
                    "queue scheduler_contract=scheduler-v1 timeout_released=true pending_count_after=4",
                ]
            )
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.Phase0Evidence.ObservedCount, Is.EqualTo(0));
            Assert.That(
                summary.Phase0Evidence.OptionalObservedKeys,
                Is.EqualTo(
                    [
                        "scheduler-accepted",
                        "scheduler-target-index",
                        "scheduler-has-request",
                        "scheduler-timeout-released",
                        "scheduler-pending-count-after",
                    ]
                )
            );
            Assert.That(summary.Phase0Evidence.IsComplete, Is.False);
            Assert.That(
                summary.Phase0Evidence.BuildSummaryText(),
                Does.EndWith(
                    "optional=scheduler-accepted,scheduler-target-index,scheduler-has-request,scheduler-timeout-released,scheduler-pending-count-after"
                )
            );
            Assert.That(summary.Phase0NextActions.ActionKeys, Does.Contain("watch"));
        });
    }

    [Test]
    public void image_pipeline補助evidenceはsummaryに残る()
    {
        DebugRuntimeLogAuditSummary summary = DebugRuntimeLogAuditSummaryPolicy.Evaluate(
            BuildSequencedLines(
                [
                    "image image_contract=image-pipeline-v1 image_log_reason=image.thumbnail-error-list.aggregate-decode-plan",
                    "detail image_contract=image-pipeline-v1 failure_reason=stale-image-request",
                    "player image_contract=image-pipeline-v1 failure_reason=stale-player-right-rail",
                ]
            )
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.Phase0Evidence.ObservedCount, Is.EqualTo(1));
            Assert.That(
                summary.Phase0Evidence.OptionalObservedKeys,
                Is.EqualTo(["image-aggregate-decode-plan", "image-stale-discard"])
            );
            Assert.That(summary.Phase0Evidence.IsComplete, Is.False);
            Assert.That(
                summary.Phase0Evidence.BuildSummaryText(),
                Does.EndWith("optional=image-aggregate-decode-plan,image-stale-discard")
            );
            Assert.That(summary.Phase0NextActions.ActionKeys, Does.Not.Contain("image"));
        });
    }

    [Test]
    public void persistence_detail補助evidenceはsummaryに残る()
    {
        DebugRuntimeLogAuditSummary summary = DebugRuntimeLogAuditSummaryPolicy.Evaluate(
            BuildSequencedLines(
                [
                    "save persist_contract=persistence-write-v1 write_succeeded=True persist_state=persisted dirty=False",
                    "save persist_contract=persistence-write-v1 failed=0 retryable=False notify_ui=False",
                    "watch dirty=True failed=1 retryable=True notify_ui=True",
                ]
            )
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.Phase0Evidence.ObservedKeys, Is.EqualTo(["persistence"]));
            Assert.That(
                summary.Phase0Evidence.OptionalObservedKeys,
                Is.EqualTo(
                    [
                        "persistence-write-succeeded",
                        "persistence-state",
                        "persistence-dirty",
                        "persistence-failed",
                        "persistence-retryable",
                        "persistence-notify-ui",
                    ]
                )
            );
            Assert.That(summary.Phase0Evidence.IsComplete, Is.False);
            Assert.That(
                summary.Phase0Evidence.BuildSummaryText(),
                Does.EndWith(
                    "optional=persistence-write-succeeded,persistence-state,persistence-dirty,persistence-failed,persistence-retryable,persistence-notify-ui"
                )
            );
            Assert.That(summary.Phase0NextActions.ActionKeys, Does.Not.Contain("persistence"));
        });
    }

    [Test]
    public void worker_DTO_detail補助evidenceはsummaryに残る()
    {
        DebugRuntimeLogAuditSummary summary = DebugRuntimeLogAuditSummaryPolicy.Evaluate(
            BuildSequencedLines(
                [
                    "worker worker_contract=worker-job-v1 diagnostic_context_count=7",
                    "worker worker_contract=worker-job-v1 capability_count=3",
                    "worker worker_contract=worker-job-v1 metric_count=2",
                ]
            )
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.Phase0Evidence.ObservedKeys, Is.EqualTo(["worker"]));
            Assert.That(
                summary.Phase0Evidence.OptionalObservedKeys,
                Is.EqualTo(
                    [
                        "worker-diagnostic-context",
                        "worker-capability-count",
                        "worker-metric-count",
                    ]
                )
            );
            Assert.That(summary.Phase0Evidence.IsComplete, Is.False);
            Assert.That(
                summary.Phase0Evidence.BuildSummaryText(),
                Does.EndWith(
                    "optional=worker-diagnostic-context,worker-capability-count,worker-metric-count"
                )
            );
            Assert.That(summary.Phase0NextActions.ActionKeys, Does.Contain("thumbnail"));
        });
    }

    [Test]
    public void worker_DTO_detail候補だけの行はsummaryに残さない()
    {
        DebugRuntimeLogAuditSummary summary = DebugRuntimeLogAuditSummaryPolicy.Evaluate(
            BuildSequencedLines(
                [
                    "worker diagnostic_context_count=7 capability_count=3 metric_count=2",
                    "worker worker_contract=worker-job-v1",
                ]
            )
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.Phase0Evidence.ObservedKeys, Is.EqualTo(["worker"]));
            Assert.That(summary.Phase0Evidence.OptionalObservedKeys, Is.Empty);
            Assert.That(
                summary.Phase0Evidence.BuildSummaryText(),
                Does.Not.Contain("optional=")
            );
        });
    }

    [Test]
    public void phase7_core_route_detail補助evidenceはsummaryに残る()
    {
        DebugRuntimeLogAuditSummary summary = DebugRuntimeLogAuditSummaryPolicy.Evaluate(
            BuildSequencedLines(
                [
                    "skin core_route=skin-refresh operation_reason=skin.host-refresh definition_mode=external",
                    "player core_route=player-playback player_surface_ready=True player_transition=start",
                    "watch core_route=watch-ui-apply watch_apply_kind=query-only watch_reason=watch-query-only",
                    "noise operation_reason=skin.host-refresh definition_mode=missing-route",
                    "noise player_surface_ready=True player_transition=stop",
                    "noise watch_apply_kind=full watch_reason=fallback",
                ]
            )
        );

        Assert.Multiple(() =>
        {
            Assert.That(
                summary.Phase0Evidence.ObservedKeys,
                Is.EqualTo(["player-core", "watch-core", "skin-core"])
            );
            Assert.That(
                summary.Phase0Evidence.OptionalObservedKeys,
                Is.EqualTo(
                    [
                        "skin-operation-reason",
                        "skin-definition-mode",
                        "player-surface-ready",
                        "player-transition",
                        "watch-apply-kind",
                        "watch-reason",
                    ]
                )
            );
            Assert.That(summary.Phase0Evidence.IsComplete, Is.False);
            Assert.That(
                summary.Phase0Evidence.BuildSummaryText(),
                Does.EndWith(
                    "optional=skin-operation-reason,skin-definition-mode,player-surface-ready,player-transition,watch-apply-kind,watch-reason"
                )
            );
            Assert.That(summary.Phase0NextActions.ActionKeys, Does.Not.Contain("player"));
            Assert.That(summary.Phase0NextActions.ActionKeys, Does.Not.Contain("watch"));
            Assert.That(summary.Phase0NextActions.ActionKeys, Does.Not.Contain("skin"));
        });
    }

    [Test]
    public void 欠けがある時も既存summaryの順序と文言が安定する()
    {
        DebugRuntimeLogAuditSummary summary = DebugRuntimeLogAuditSummaryPolicy.Evaluate(
            BuildSequencedLines(
                [
                    "startup first-page shown",
                    "watch core_route=watch-ui-apply",
                ]
            )
        );

        string[] lines = summary.BuildSummaryText().Split(Environment.NewLine);

        Assert.Multiple(() =>
        {
            Assert.That(
                lines.Take(5),
                Is.EqualTo(
                    new[]
                    {
                        "log_run_lines=2/2 has_sequence=true sequence=1-2 resets=0",
                        "log_run_window=2026-06-25T10:00:00.001..2026-06-25T10:00:00.002 elapsed_ms=1 timestamp_lines=2/2",
                        "log_evidence=1/9 missing=ui-shell,readmodel-diff,scheduler,image,persistence,worker,skin-core,player-core",
                        "phase0_log_evidence=2/12 missing=startup-input-ready,search-input,sort-input,scroll-input,player-core,image-pipeline,persistence,worker,thumbnail-worker,skin-core",
                        "phase0_next_actions=startup,search,sort,scroll,player,image,persistence,thumbnail,skin",
                    }
                )
            );
            Assert.That(lines, Has.Length.EqualTo(10));
            Assert.That(
                lines[5],
                Is.EqualTo(
                    "phase0_scenario_log_evidence=0/8 missing_scenarios=startup,search-sort-scroll,tab-selection-page,watch-small-diff,player,image,skin,persistence-shutdown"
                )
            );
            Assert.That(lines[6], Does.StartWith("phase0_scenario_scorecard=startup{"));
            Assert.That(
                lines[7],
                Does.StartWith("phase0_manual_visual_review=required phase0_complete=false checks=")
            );
            Assert.That(lines[8], Is.EqualTo("phase0_audit_status=missing-contract-evidence"));
            Assert.That(lines[9], Is.EqualTo("phase0_audit_complete=false"));
        });
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
                summary.RunWindow.BuildSummaryText(),
                Is.EqualTo("log_run_window=none elapsed_ms=none timestamp_lines=0/0")
            );
            Assert.That(summary.Phase0NextActions.IsComplete, Is.False);
            string[] lines = summary.BuildSummaryText().Split(Environment.NewLine);
            Assert.That(
                lines.Take(5),
                Is.EqualTo(
                    new[]
                    {
                        "log_run_lines=0/0 has_sequence=false sequence=none resets=0",
                        "log_run_window=none elapsed_ms=none timestamp_lines=0/0",
                        "log_evidence=0/9 missing=ui-shell,readmodel-diff,scheduler,image,persistence,worker,skin-core,player-core,watch-core",
                        "phase0_log_evidence=0/12 missing=startup-first-page,startup-input-ready,search-input,sort-input,scroll-input,player-core,watch-core,image-pipeline,persistence,worker,thumbnail-worker,skin-core",
                        "phase0_next_actions=startup,search,sort,scroll,player,watch,image,persistence,thumbnail,skin",
                    }
                )
            );
            Assert.That(lines, Has.Length.EqualTo(10));
            Assert.That(
                lines[5],
                Is.EqualTo(
                    "phase0_scenario_log_evidence=0/8 missing_scenarios=startup,search-sort-scroll,tab-selection-page,watch-small-diff,player,image,skin,persistence-shutdown"
                )
            );
            Assert.That(
                lines[7],
                Does.Contain(
                    "tab-selection-page=selection,focus,page-or-scroll-position,blank"
                )
            );
            Assert.That(lines[8], Is.EqualTo("phase0_audit_status=missing-timestamp"));
            Assert.That(lines[9], Is.EqualTo("phase0_audit_complete=false"));
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
