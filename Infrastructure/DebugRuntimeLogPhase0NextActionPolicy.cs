#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace IndigoMovieManager.Infrastructure;

public static class DebugRuntimeLogPhase0NextActionPolicy
{
    private static readonly Phase0NextActionRule[] ActionRules =
    [
        new("startup", ["startup-first-page", "startup-input-ready"]),
        new("search", ["search-input"]),
        new("sort", ["sort-input"]),
        new("scroll", ["scroll-input"]),
        new("player", ["player-core"]),
        new("watch", ["watch-core"]),
        new("image", ["image-pipeline"]),
        new("persistence", ["persistence"]),
        new("thumbnail", ["worker", "thumbnail-worker"]),
        new("skin", ["skin-core"]),
    ];

    public static DebugRuntimeLogPhase0NextActionSummary Evaluate(
        DebugRuntimeLogPhase0EvidenceSummary evidenceSummary
    )
    {
        ArgumentNullException.ThrowIfNull(evidenceSummary);

        HashSet<string> missingKeys = new(evidenceSummary.MissingKeys, StringComparer.Ordinal);

        // 不足tokenを採取時の操作カテゴリへ畳み、出力順はこのpolicyで固定する。
        string[] actionKeys = ActionRules
            .Where(rule => rule.Matches(missingKeys))
            .Select(rule => rule.ActionKey)
            .ToArray();

        return new DebugRuntimeLogPhase0NextActionSummary(actionKeys);
    }

    private readonly record struct Phase0NextActionRule(
        string ActionKey,
        IReadOnlyList<string> MissingKeys
    )
    {
        public bool Matches(IReadOnlySet<string> missingKeys)
        {
            return MissingKeys.Any(missingKeys.Contains);
        }
    }
}

public sealed class DebugRuntimeLogPhase0NextActionSummary
{
    internal DebugRuntimeLogPhase0NextActionSummary(IReadOnlyList<string> actionKeys)
    {
        ActionKeys = actionKeys;
    }

    public IReadOnlyList<string> ActionKeys { get; }

    public bool IsComplete => ActionKeys.Count == 0;

    public string BuildSummaryText()
    {
        string actions = IsComplete ? "none" : string.Join(",", ActionKeys);
        return $"phase0_next_actions={actions}";
    }
}
