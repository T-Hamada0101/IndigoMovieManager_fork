#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace IndigoMovieManager.Infrastructure;

public static class DebugRuntimeLogPhase0ScenarioScorecardPolicy
{
    private static readonly ScenarioDefinition[] ScenarioDefinitions =
    [
        new(
            "startup",
            [],
            ["startup-first-page", "startup-input-ready"],
            [],
            [],
            ["first-useful-display", "input-ready", "blank"]
        ),
        new(
            "search-sort-scroll",
            ["ui-shell", "readmodel-diff", "scheduler"],
            ["search-input", "sort-input", "scroll-input"],
            [],
            [
                "ui-shell-user-priority-active",
                "ui-shell-recent-viewport-active",
                "readmodel-diff-single",
                "readmodel-diff-total",
                "readmodel-diff-source-revision",
                "readmodel-diff-view-revision",
                "readmodel-diff-full-fallback-reason",
                "scheduler-accepted",
                "scheduler-target-index",
                "scheduler-has-request",
                "scheduler-timeout-released",
                "scheduler-pending-count-after",
            ],
            [
                "input-continuity",
                "selection",
                "focus",
                "scroll",
                "blank",
                "multi-selection",
                "scroll-anchor",
                "operation-feedback",
                "continued-input-during-feedback",
            ]
        ),
        new(
            "tab-selection-page",
            ["ui-shell"],
            [],
            ["upper-tab-switch-input", "log-tab-switch-input"],
            [
                "upper-tab-switch-input",
                "log-tab-switch-input",
                "ui-shell-user-priority-active",
                "ui-shell-recent-viewport-active",
            ],
            ["selection", "focus", "page-or-scroll-position", "blank", "focus-not-stolen"]
        ),
        new(
            "watch-small-diff",
            ["watch-core", "readmodel-diff", "scheduler"],
            ["watch-core"],
            [],
            [
                "watch-apply-kind",
                "watch-reason",
                "readmodel-diff-single",
                "readmodel-diff-total",
                "readmodel-diff-source-revision",
                "readmodel-diff-view-revision",
                "readmodel-diff-full-fallback-reason",
                "scheduler-accepted",
                "scheduler-target-index",
                "scheduler-has-request",
                "scheduler-timeout-released",
                "scheduler-pending-count-after",
            ],
            ["selection", "scroll", "blank", "stale-rollback"]
        ),
        new(
            "player",
            ["player-core"],
            ["player-core"],
            [],
            ["ui-shell-player-playback-active", "player-surface-ready", "player-transition"],
            ["playback-continuity", "selection", "focus", "blank", "operation-feedback"]
        ),
        new(
            "image",
            ["image", "worker"],
            ["image-pipeline", "thumbnail-worker"],
            [],
            [
                "image-aggregate-decode-plan",
                "image-stale-discard",
                "worker-diagnostic-context",
                "worker-capability-count",
                "worker-metric-count",
            ],
            ["visible-first", "stale-image", "blank"]
        ),
        new(
            "skin",
            ["skin-core"],
            ["skin-core"],
            [],
            ["skin-operation-reason", "skin-definition-mode"],
            ["content-visible", "focus", "blank", "flicker"]
        ),
        new(
            "persistence-shutdown",
            ["persistence"],
            ["persistence"],
            [],
            [],
            ["setting-retained", "shutdown-completes", "blank"]
        ),
    ];

    public static DebugRuntimeLogPhase0ScenarioScorecard Evaluate(
        DebugRuntimeLogEvidenceSummary contractEvidence,
        DebugRuntimeLogPhase0EvidenceSummary phase0Evidence
    )
    {
        ArgumentNullException.ThrowIfNull(contractEvidence);
        ArgumentNullException.ThrowIfNull(phase0Evidence);

        HashSet<string> observedContractKeys = new(
            contractEvidence.ObservedKeys,
            StringComparer.Ordinal
        );
        HashSet<string> observedPhase0Keys = new(
            phase0Evidence.ObservedKeys,
            StringComparer.Ordinal
        );
        HashSet<string> observedOptionalKeys = new(
            phase0Evidence.OptionalObservedKeys,
            StringComparer.Ordinal
        );

        DebugRuntimeLogPhase0ScenarioScore[] scenarios = ScenarioDefinitions
            .Select(definition =>
                EvaluateScenario(
                    definition,
                    observedContractKeys,
                    observedPhase0Keys,
                    observedOptionalKeys
                )
            )
            .ToArray();

        return new DebugRuntimeLogPhase0ScenarioScorecard(scenarios);
    }

    private static DebugRuntimeLogPhase0ScenarioScore EvaluateScenario(
        ScenarioDefinition definition,
        IReadOnlySet<string> observedContractKeys,
        IReadOnlySet<string> observedPhase0Keys,
        IReadOnlySet<string> observedOptionalKeys
    )
    {
        // 既存evidenceの所属だけを組み替え、runtimeログ契約は増やさない。
        string[] missingKeys = definition
            .RequiredContractKeys.Where(key => !observedContractKeys.Contains(key))
            .Concat(definition.RequiredPhase0Keys.Where(key => !observedPhase0Keys.Contains(key)))
            .Concat(
                definition.RequiredOptionalKeys.Where(key => !observedOptionalKeys.Contains(key))
            )
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string[] observedDetailKeys = definition
            .DetailKeys.Where(observedOptionalKeys.Contains)
            .ToArray();

        return new DebugRuntimeLogPhase0ScenarioScore(
            definition.Key,
            missingKeys,
            observedDetailKeys,
            definition.ManualVisualReviewKeys
        );
    }

    private sealed record ScenarioDefinition(
        string Key,
        string[] RequiredContractKeys,
        string[] RequiredPhase0Keys,
        string[] RequiredOptionalKeys,
        string[] DetailKeys,
        string[] ManualVisualReviewKeys
    );
}

public sealed class DebugRuntimeLogPhase0ScenarioScorecard
{
    internal DebugRuntimeLogPhase0ScenarioScorecard(
        IReadOnlyList<DebugRuntimeLogPhase0ScenarioScore> scenarios
    )
    {
        Scenarios = scenarios;
    }

    public IReadOnlyList<DebugRuntimeLogPhase0ScenarioScore> Scenarios { get; }

    public int LogEvidenceCompleteCount =>
        Scenarios.Count(scenario => scenario.LogEvidenceComplete);

    public bool IsLogEvidenceComplete => LogEvidenceCompleteCount == Scenarios.Count;

    public bool ManualVisualReviewRequired => true;

    public bool IsPhase0Complete => false;

    public DebugRuntimeLogPhase0ScenarioScore GetScenario(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return Scenarios.Single(scenario =>
            string.Equals(scenario.Key, key, StringComparison.Ordinal)
        );
    }

    public string BuildSummaryText()
    {
        string missingScenarios = string.Join(
            ",",
            Scenarios
                .Where(scenario => !scenario.LogEvidenceComplete)
                .Select(scenario => scenario.Key)
        );
        if (missingScenarios.Length == 0)
        {
            missingScenarios = "none";
        }

        string scenarioDetails = string.Join(
            "|",
            Scenarios.Select(scenario =>
                $"{scenario.Key}{{complete={(scenario.LogEvidenceComplete ? "true" : "false")},missing={JoinOrNone(scenario.MissingKeys)},details={JoinOrNone(scenario.ObservedDetailKeys)}}}"
            )
        );
        string manualVisualReview = string.Join(
            "|",
            Scenarios.Select(scenario =>
                $"{scenario.Key}={string.Join(",", scenario.ManualVisualReviewKeys)}"
            )
        );

        return string.Join(
            Environment.NewLine,
            $"phase0_scenario_log_evidence={LogEvidenceCompleteCount}/{Scenarios.Count} missing_scenarios={missingScenarios}",
            $"phase0_scenario_scorecard={scenarioDetails}",
            $"phase0_manual_visual_review=required phase0_complete=false checks={manualVisualReview}"
        );
    }

    private static string JoinOrNone(IReadOnlyList<string> keys)
    {
        return keys.Count == 0 ? "none" : string.Join(",", keys);
    }
}

public sealed class DebugRuntimeLogPhase0ScenarioScore
{
    internal DebugRuntimeLogPhase0ScenarioScore(
        string key,
        IReadOnlyList<string> missingKeys,
        IReadOnlyList<string> observedDetailKeys,
        IReadOnlyList<string> manualVisualReviewKeys
    )
    {
        Key = key;
        MissingKeys = missingKeys;
        ObservedDetailKeys = observedDetailKeys;
        ManualVisualReviewKeys = manualVisualReviewKeys;
    }

    public string Key { get; }

    public bool LogEvidenceComplete => MissingKeys.Count == 0;

    public IReadOnlyList<string> MissingKeys { get; }

    public IReadOnlyList<string> ObservedDetailKeys { get; }

    public bool ManualVisualReviewRequired => true;

    public IReadOnlyList<string> ManualVisualReviewKeys { get; }
}
