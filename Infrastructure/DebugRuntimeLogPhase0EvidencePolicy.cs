#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace IndigoMovieManager.Infrastructure;

public static class DebugRuntimeLogPhase0EvidencePolicy
{
    private static readonly RequiredPhase0EvidenceToken[] RequiredEvidenceTokens =
    [
        new("startup-first-page", "first-page shown"),
        new("startup-input-ready", "input ready"),
        new("search-input", "ui shell input: operation_reason=search"),
        new("sort-input", "ui shell input: operation_reason=sort"),
        new("scroll-input", "page scroll end:"),
        new("player-core", "core_route=player-playback"),
        new("watch-core", "core_route=watch-ui-apply"),
        new("image-pipeline", "image_contract=image-pipeline-v1"),
        new("persistence", "persist_contract=persistence-write-v1"),
        new("worker", "worker_contract=worker-job-v1"),
        new("thumbnail-worker", "worker_kind=thumbnail-create"),
        new("skin-core", "core_route=skin-refresh"),
    ];

    public static DebugRuntimeLogPhase0EvidenceSummary Evaluate(IEnumerable<string> logLines)
    {
        ArgumentNullException.ThrowIfNull(logLines);

        HashSet<string> observedKeys = new(StringComparer.Ordinal);

        // 採取済みログ行だけを読み、Phase0 操作確認用 token の有無へ畳み込む。
        foreach (string? line in logLines)
        {
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            foreach (RequiredPhase0EvidenceToken token in RequiredEvidenceTokens)
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

        return new DebugRuntimeLogPhase0EvidenceSummary(
            RequiredEvidenceTokens.Length,
            observedKeysInOrder,
            missingKeys
        );
    }

    private readonly record struct RequiredPhase0EvidenceToken(string Key, string Token);
}

public sealed class DebugRuntimeLogPhase0EvidenceSummary
{
    internal DebugRuntimeLogPhase0EvidenceSummary(
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
        return $"phase0_log_evidence={ObservedCount}/{TotalRequiredCount} missing={missing}";
    }
}
