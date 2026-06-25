#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace IndigoMovieManager.Infrastructure;

public static class DebugRuntimeLogEvidencePolicy
{
    private static readonly RequiredEvidenceToken[] RequiredEvidenceTokens =
    [
        new("ui-shell", "ui_shell_contract=ui-shell-v1"),
        new("readmodel-diff", "diff_contract=readmodel-diff-v1"),
        new("scheduler", "scheduler_contract=scheduler-v1"),
        new("image", "image_contract=image-pipeline-v1"),
        new("persistence", "persist_contract=persistence-write-v1"),
        new("worker", "worker_contract=worker-job-v1"),
        new("skin-core", "core_route=skin-refresh"),
        new("player-core", "core_route=player-playback"),
        new("watch-core", "core_route=watch-ui-apply"),
    ];

    public static DebugRuntimeLogEvidenceSummary Evaluate(IEnumerable<string> logLines)
    {
        ArgumentNullException.ThrowIfNull(logLines);

        HashSet<string> observedKeys = new(StringComparer.Ordinal);

        // 採取済みログを行単位でなめ、見つかった契約 token だけを安定 key に畳む。
        foreach (string? line in logLines)
        {
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            foreach (RequiredEvidenceToken token in RequiredEvidenceTokens)
            {
                if (line.Contains(token.Token, StringComparison.Ordinal))
                {
                    observedKeys.Add(token.Key);
                }
            }
        }

        string[] observedKeysInOrder = RequiredEvidenceTokens
            .Where(token => observedKeys.Contains(token.Key))
            .Select(token => token.Key)
            .ToArray();
        string[] missingKeys = RequiredEvidenceTokens
            .Where(token => !observedKeys.Contains(token.Key))
            .Select(token => token.Key)
            .ToArray();

        return new DebugRuntimeLogEvidenceSummary(
            RequiredEvidenceTokens.Length,
            observedKeysInOrder,
            missingKeys
        );
    }

    private readonly record struct RequiredEvidenceToken(string Key, string Token);
}

public sealed class DebugRuntimeLogEvidenceSummary
{
    internal DebugRuntimeLogEvidenceSummary(
        int totalRequiredCount,
        IReadOnlyList<string> observedKeys,
        IReadOnlyList<string> missingKeys
    )
    {
        TotalRequiredCount = totalRequiredCount;
        ObservedKeys = observedKeys;
        MissingKeys = missingKeys;
    }

    public int TotalRequiredCount { get; }

    public int ObservedCount => ObservedKeys.Count;

    public bool IsComplete => MissingKeys.Count == 0;

    public IReadOnlyList<string> MissingKeys { get; }

    public IReadOnlyList<string> ObservedKeys { get; }

    public string BuildSummaryText()
    {
        string missing = MissingKeys.Count == 0 ? "none" : string.Join(",", MissingKeys);
        return $"log_evidence={ObservedCount}/{TotalRequiredCount} missing={missing}";
    }
}
