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
                "scroll ui shell input: operation_reason=scroll",
                "player core_route=player-playback",
                "watch core_route=watch-ui-apply",
                "image image_contract=image-pipeline-v1",
                "save persist_contract=persistence-write-v1",
                "worker worker_contract=worker-job-v1",
                "thumbnail worker_kind=thumbnail-create",
                "skin core_route=skin-refresh",
            ]
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.TotalRequiredCount, Is.EqualTo(12));
            Assert.That(summary.ObservedCount, Is.EqualTo(12));
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
                        "scroll-input",
                        "player-core",
                        "watch-core",
                        "image-pipeline",
                        "persistence",
                        "worker",
                        "thumbnail-worker",
                        "skin-core",
                    ]
                )
            );
        });
    }

    [Test]
    public void scroll_inputは新旧ログを同じkeyとして認識する()
    {
        DebugRuntimeLogPhase0EvidenceSummary newLogSummary =
            DebugRuntimeLogPhase0EvidencePolicy.Evaluate(
                ["scroll ui shell input: operation_reason=scroll"]
            );
        DebugRuntimeLogPhase0EvidenceSummary oldLogSummary =
            DebugRuntimeLogPhase0EvidencePolicy.Evaluate(["scroll page scroll end:"]);
        DebugRuntimeLogPhase0EvidenceSummary bothLogSummary =
            DebugRuntimeLogPhase0EvidencePolicy.Evaluate(
                [
                    "scroll ui shell input: operation_reason=scroll",
                    "scroll page scroll end:",
                ]
            );

        Assert.Multiple(() =>
        {
            Assert.That(newLogSummary.TotalRequiredCount, Is.EqualTo(12));
            Assert.That(newLogSummary.ObservedCount, Is.EqualTo(1));
            Assert.That(newLogSummary.ObservedKeys, Is.EqualTo(["scroll-input"]));
            Assert.That(oldLogSummary.ObservedCount, Is.EqualTo(1));
            Assert.That(oldLogSummary.ObservedKeys, Is.EqualTo(["scroll-input"]));
            Assert.That(bothLogSummary.ObservedCount, Is.EqualTo(1));
            Assert.That(bothLogSummary.ObservedKeys, Is.EqualTo(["scroll-input"]));
        });
    }

    [Test]
    public void manual_reload_inputはoptional_evidenceとして認識する()
    {
        DebugRuntimeLogPhase0EvidenceSummary summary = DebugRuntimeLogPhase0EvidencePolicy.Evaluate(
            [
                "input ui shell input: operation_reason=manual-reload ui_shell_contract=ui-shell-v1",
            ]
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.TotalRequiredCount, Is.EqualTo(12));
            Assert.That(summary.ObservedCount, Is.EqualTo(0));
            Assert.That(summary.IsComplete, Is.False);
            Assert.That(summary.TotalOptionalCount, Is.EqualTo(33));
            Assert.That(summary.OptionalObservedCount, Is.EqualTo(1));
            Assert.That(summary.OptionalObservedKeys, Is.EqualTo(["manual-reload-input"]));
            Assert.That(summary.MissingKeys, Does.Contain("search-input"));
            Assert.That(
                summary.BuildSummaryText(),
                Does.Contain("optional_evidence=1/33 optional=manual-reload-input")
            );
            Assert.That(summary.BuildSummaryText(), Does.EndWith("optional=manual-reload-input"));
        });
    }

    [Test]
    public void ui_shell_snapshot_detail補助evidenceはoptionalとして認識する()
    {
        DebugRuntimeLogPhase0EvidenceSummary summary = DebugRuntimeLogPhase0EvidencePolicy.Evaluate(
            [
                "snapshot ui_shell_contract=ui-shell-v1 is_user_priority_active=True is_manual_mode=False is_watch_ui_suppressed=False is_recent_viewport_active=True is_player_playback_active=False",
            ]
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.TotalRequiredCount, Is.EqualTo(12));
            Assert.That(summary.ObservedCount, Is.EqualTo(0));
            Assert.That(summary.TotalOptionalCount, Is.EqualTo(33));
            Assert.That(summary.OptionalObservedCount, Is.EqualTo(5));
            Assert.That(
                summary.OptionalObservedKeys,
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
            Assert.That(
                summary.BuildSummaryText(),
                Does.Contain(
                    "optional_evidence=5/33 optional=ui-shell-user-priority-active,ui-shell-manual-mode,ui-shell-watch-suppressed,ui-shell-recent-viewport-active,ui-shell-player-playback-active"
                )
            );
        });
    }

    [Test]
    public void ui_shell_snapshot_detail候補だけの行はoptionalとして認識しない()
    {
        DebugRuntimeLogPhase0EvidenceSummary summary = DebugRuntimeLogPhase0EvidencePolicy.Evaluate(
            [
                "snapshot is_user_priority_active=True is_manual_mode=False is_watch_ui_suppressed=False is_recent_viewport_active=True is_player_playback_active=False",
                "snapshot ui_shell_contract=ui-shell-v1",
            ]
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.ObservedKeys, Is.Empty);
            Assert.That(summary.OptionalObservedKeys, Is.Empty);
            Assert.That(summary.TotalOptionalCount, Is.EqualTo(33));
        });
    }

    [Test]
    public void readmodel_diff詳細補助evidenceはoptionalとして認識する()
    {
        DebugRuntimeLogPhase0EvidenceSummary summary = DebugRuntimeLogPhase0EvidencePolicy.Evaluate(
            [
                "watch diff_contract=readmodel-diff-v1 diff_change_set=single diff_changed_total=1",
                "apply diff_contract=readmodel-diff-v1 diff_full_fallback_reason=none diff_source_revision=10 diff_view_revision=11 diff_changed_total=120",
            ]
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.TotalRequiredCount, Is.EqualTo(12));
            Assert.That(summary.ObservedCount, Is.EqualTo(0));
            Assert.That(summary.TotalOptionalCount, Is.EqualTo(33));
            Assert.That(summary.OptionalObservedCount, Is.EqualTo(5));
            Assert.That(
                summary.OptionalObservedKeys,
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
            Assert.That(
                summary.BuildSummaryText(),
                Does.EndWith(
                    "optional=readmodel-diff-single,readmodel-diff-total,readmodel-diff-source-revision,readmodel-diff-view-revision,readmodel-diff-full-fallback-reason"
                )
            );
        });
    }

    [Test]
    public void readmodel_diff詳細候補だけの行はoptionalとして認識しない()
    {
        DebugRuntimeLogPhase0EvidenceSummary summary = DebugRuntimeLogPhase0EvidencePolicy.Evaluate(
            [
                "watch diff_change_set=single diff_changed_total=1",
                "apply diff_full_fallback_reason=none diff_source_revision=10 diff_view_revision=11",
                "apply diff_contract=readmodel-diff-v1",
            ]
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.ObservedKeys, Is.Empty);
            Assert.That(summary.OptionalObservedKeys, Is.Empty);
            Assert.That(summary.TotalOptionalCount, Is.EqualTo(33));
        });
    }

    [Test]
    public void scheduler_detail補助evidenceはoptionalとして認識する()
    {
        DebugRuntimeLogPhase0EvidenceSummary summary = DebugRuntimeLogPhase0EvidencePolicy.Evaluate(
            [
                "queue scheduler_contract=scheduler-v1 admission_action=enqueued accepted=True target_index=-1",
                "queue scheduler_contract=scheduler-v1 sequence=1 has_request=True pending_count_after=0",
                "queue scheduler_contract=scheduler-v1 timeout_released=true pending_count_after=4",
            ]
        );
        DebugRuntimeLogPhase0EvidenceSummary nonSchedulerSummary =
            DebugRuntimeLogPhase0EvidencePolicy.Evaluate(
                ["diagnostic repeat skin refresh queued: accepted=True"]
            );

        Assert.Multiple(() =>
        {
            Assert.That(summary.TotalRequiredCount, Is.EqualTo(12));
            Assert.That(summary.ObservedCount, Is.EqualTo(0));
            Assert.That(summary.TotalOptionalCount, Is.EqualTo(33));
            Assert.That(summary.OptionalObservedCount, Is.EqualTo(5));
            Assert.That(
                summary.OptionalObservedKeys,
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
            Assert.That(
                summary.BuildSummaryText(),
                Does.EndWith(
                    "optional=scheduler-accepted,scheduler-target-index,scheduler-has-request,scheduler-timeout-released,scheduler-pending-count-after"
                )
            );
            Assert.That(nonSchedulerSummary.OptionalObservedKeys, Is.Empty);
        });
    }

    [Test]
    public void image_pipeline補助evidenceはoptionalとして認識する()
    {
        DebugRuntimeLogPhase0EvidenceSummary summary = DebugRuntimeLogPhase0EvidencePolicy.Evaluate(
            [
                "image image_log_reason=image.thumbnail-error-list.aggregate-decode-plan image_contract=image-pipeline-v1",
                "detail image_contract=image-pipeline-v1 failure_reason=stale-image-request",
                "player image_contract=image-pipeline-v1 failure_reason=stale-player-right-rail",
            ]
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.TotalRequiredCount, Is.EqualTo(12));
            Assert.That(summary.ObservedKeys, Is.EqualTo(["image-pipeline"]));
            Assert.That(summary.TotalOptionalCount, Is.EqualTo(33));
            Assert.That(summary.OptionalObservedCount, Is.EqualTo(2));
            Assert.That(
                summary.OptionalObservedKeys,
                Is.EqualTo(["image-aggregate-decode-plan", "image-stale-discard"])
            );
            Assert.That(
                summary.BuildSummaryText(),
                Does.EndWith("optional=image-aggregate-decode-plan,image-stale-discard")
            );
        });
    }

    [Test]
    public void image_pipeline補助evidence候補だけの行はoptionalとして認識しない()
    {
        DebugRuntimeLogPhase0EvidenceSummary summary = DebugRuntimeLogPhase0EvidencePolicy.Evaluate(
            [
                "image image_log_reason=image.thumbnail-error-list.aggregate-decode-plan",
                "detail failure_reason=stale-image-request",
                "player failure_reason=stale-player-right-rail",
                "image image_contract=image-pipeline-v1",
            ]
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.ObservedKeys, Is.EqualTo(["image-pipeline"]));
            Assert.That(summary.OptionalObservedKeys, Is.Empty);
            Assert.That(summary.TotalOptionalCount, Is.EqualTo(33));
        });
    }

    [Test]
    public void worker_DTO_detail補助evidenceはoptionalとして認識する()
    {
        DebugRuntimeLogPhase0EvidenceSummary summary = DebugRuntimeLogPhase0EvidencePolicy.Evaluate(
            [
                "worker worker_contract=worker-job-v1 diagnostic_context_count=7",
                "worker worker_contract=worker-job-v1 capability_count=3",
                "worker worker_contract=worker-job-v1 metric_count=2",
            ]
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.TotalRequiredCount, Is.EqualTo(12));
            Assert.That(summary.ObservedKeys, Is.EqualTo(["worker"]));
            Assert.That(summary.TotalOptionalCount, Is.EqualTo(33));
            Assert.That(summary.OptionalObservedCount, Is.EqualTo(3));
            Assert.That(
                summary.OptionalObservedKeys,
                Is.EqualTo(
                    [
                        "worker-diagnostic-context",
                        "worker-capability-count",
                        "worker-metric-count",
                    ]
                )
            );
            Assert.That(
                summary.BuildSummaryText(),
                Does.EndWith(
                    "optional=worker-diagnostic-context,worker-capability-count,worker-metric-count"
                )
            );
        });
    }

    [Test]
    public void worker_DTO_detail候補だけの行はoptionalとして認識しない()
    {
        DebugRuntimeLogPhase0EvidenceSummary summary = DebugRuntimeLogPhase0EvidencePolicy.Evaluate(
            [
                "worker diagnostic_context_count=7 capability_count=3 metric_count=2",
                "worker worker_contract=worker-job-v1",
            ]
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.ObservedKeys, Is.EqualTo(["worker"]));
            Assert.That(summary.OptionalObservedKeys, Is.Empty);
            Assert.That(summary.TotalOptionalCount, Is.EqualTo(33));
        });
    }

    [Test]
    public void phase7_core_route_detail補助evidenceはoptionalとして認識する()
    {
        DebugRuntimeLogPhase0EvidenceSummary summary = DebugRuntimeLogPhase0EvidencePolicy.Evaluate(
            [
                "skin core_route=skin-refresh operation_reason=skin.host-refresh definition_mode=external",
                "player core_route=player-playback player_surface_ready=True player_transition=start",
                "watch core_route=watch-ui-apply watch_apply_kind=query-only watch_reason=watch-query-only",
                "noise operation_reason=skin.host-refresh definition_mode=missing-route",
                "noise player_surface_ready=True player_transition=stop",
                "noise watch_apply_kind=full watch_reason=fallback",
            ]
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.TotalRequiredCount, Is.EqualTo(12));
            Assert.That(summary.ObservedKeys, Is.EqualTo(["player-core", "watch-core", "skin-core"]));
            Assert.That(summary.TotalOptionalCount, Is.EqualTo(33));
            Assert.That(summary.OptionalObservedCount, Is.EqualTo(6));
            Assert.That(
                summary.OptionalObservedKeys,
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
            Assert.That(
                summary.BuildSummaryText(),
                Does.EndWith(
                    "optional=skin-operation-reason,skin-definition-mode,player-surface-ready,player-transition,watch-apply-kind,watch-reason"
                )
            );
        });
    }

    [Test]
    public void phase7_core_route_detail候補だけの行はoptionalとして認識しない()
    {
        DebugRuntimeLogPhase0EvidenceSummary summary = DebugRuntimeLogPhase0EvidencePolicy.Evaluate(
            [
                "skin operation_reason=skin.host-refresh definition_mode=external",
                "player player_surface_ready=True player_transition=start",
                "watch watch_apply_kind=query-only watch_reason=watch-query-only",
                "skin core_route=skin-refresh",
                "player core_route=player-playback",
                "watch core_route=watch-ui-apply",
            ]
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.ObservedKeys, Is.EqualTo(["player-core", "watch-core", "skin-core"]));
            Assert.That(summary.OptionalObservedKeys, Is.Empty);
            Assert.That(summary.TotalOptionalCount, Is.EqualTo(33));
        });
    }

    [Test]
    public void persistence_detail補助evidenceは契約名と同じ行だけoptionalとして認識する()
    {
        DebugRuntimeLogPhase0EvidenceSummary summary = DebugRuntimeLogPhase0EvidencePolicy.Evaluate(
            [
                "save persist_contract=persistence-write-v1 write_succeeded=True persist_state=persisted dirty=False",
                "save persist_contract=persistence-write-v1 failed=0 retryable=False notify_ui=False",
                "other dirty=True failed=1 retryable=True notify_ui=True",
            ]
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.TotalRequiredCount, Is.EqualTo(12));
            Assert.That(summary.ObservedKeys, Is.EqualTo(["persistence"]));
            Assert.That(summary.TotalOptionalCount, Is.EqualTo(33));
            Assert.That(summary.OptionalObservedCount, Is.EqualTo(6));
            Assert.That(
                summary.OptionalObservedKeys,
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
            Assert.That(
                summary.BuildSummaryText(),
                Does.EndWith(
                    "optional=persistence-write-succeeded,persistence-state,persistence-dirty,persistence-failed,persistence-retryable,persistence-notify-ui"
                )
            );
        });
    }

    [Test]
    public void persistence_detail候補だけの行はoptionalとして認識しない()
    {
        DebugRuntimeLogPhase0EvidenceSummary summary = DebugRuntimeLogPhase0EvidencePolicy.Evaluate(
            [
                "watch dirty=True failed=1 retryable=True notify_ui=True",
                "save write_succeeded=True persist_state=persisted",
            ]
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.ObservedKeys, Is.Empty);
            Assert.That(summary.OptionalObservedKeys, Is.Empty);
            Assert.That(summary.TotalOptionalCount, Is.EqualTo(33));
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
                        "scroll-input",
                        "player-core",
                        "image-pipeline",
                        "worker",
                        "thumbnail-worker",
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
                    "scroll ui shell input: operation_reason=scroll",
                    "player core_route=player-playback",
                    "watch core_route=watch-ui-apply",
                    "image image_contract=image-pipeline-v1",
                    "save persist_contract=persistence-write-v1",
                    "worker worker_contract=worker-job-v1",
                    "thumbnail worker_kind=thumbnail-create",
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
                Is.EqualTo("phase0_log_evidence=12/12 missing=none")
            );
            Assert.That(
                missingSummary.BuildSummaryText(),
                Is.EqualTo(
                    "phase0_log_evidence=2/12 missing=startup-input-ready,search-input,sort-input,scroll-input,player-core,image-pipeline,persistence,worker,thumbnail-worker,skin-core"
                )
            );
        });
    }
}
