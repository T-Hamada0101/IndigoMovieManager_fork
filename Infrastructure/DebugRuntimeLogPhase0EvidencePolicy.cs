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
        new("scroll-input", ["ui shell input: operation_reason=scroll", "page scroll end:"]),
        new("player-core", "core_route=player-playback"),
        new("watch-core", "core_route=watch-ui-apply"),
        new("image-pipeline", "image_contract=image-pipeline-v1"),
        new("persistence", "persist_contract=persistence-write-v1"),
        new("worker", "worker_contract=worker-job-v1"),
        new("thumbnail-worker", "worker_kind=thumbnail-create"),
        new("skin-core", "core_route=skin-refresh"),
    ];

    private static readonly RequiredPhase0EvidenceToken[] OptionalEvidenceTokens =
    [
        // manual reload 入力は Phase1 補助 evidence として扱い、Phase0 必須12件は増やさない。
        new("manual-reload-input", "ui shell input: operation_reason=manual-reload"),
        // ReadModel Diff 詳細は Phase2 補助 evidence として扱い、実機採取時の小変更判定を読みやすくする。
        new("readmodel-diff-single", "diff_change_set=single"),
        new("readmodel-diff-total", "diff_changed_total="),
        // Phase4 画像 pipeline の実機確認は補助 evidence に留め、必須12件の完了条件は動かさない。
        new(
            "image-aggregate-decode-plan",
            "image_log_reason=image.thumbnail-error-list.aggregate-decode-plan"
        ),
        new(
            "image-stale-discard",
            ["failure_reason=stale-image-request", "failure_reason=stale-player-right-rail"]
        ),
        // Worker DTO detail は Phase6 補助 evidence として扱い、Phase0 必須12件は増やさない。
        new("worker-diagnostic-context", "diagnostic_context_count="),
        new("worker-capability-count", "capability_count="),
        new("worker-metric-count", "metric_count="),
    ];

    public static DebugRuntimeLogPhase0EvidenceSummary Evaluate(IEnumerable<string> logLines)
    {
        ArgumentNullException.ThrowIfNull(logLines);

        HashSet<string> observedKeys = new(StringComparer.Ordinal);
        HashSet<string> optionalObservedKeys = new(StringComparer.Ordinal);

        // 採取済みログ行だけを読み、Phase0 操作確認用 token の有無へ畳み込む。
        foreach (string? line in logLines)
        {
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            foreach (RequiredPhase0EvidenceToken token in RequiredEvidenceTokens)
            {
                if (token.Matches(line))
                {
                    observedKeys.Add(token.Key);
                }
            }

            foreach (RequiredPhase0EvidenceToken token in OptionalEvidenceTokens)
            {
                if (token.Matches(line))
                {
                    optionalObservedKeys.Add(token.Key);
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
        string[] optionalObservedKeysInOrder = OptionalEvidenceTokens
            .Where(token => optionalObservedKeys.Contains(token.Key))
            .Select(token => token.Key)
            .ToArray();

        return new DebugRuntimeLogPhase0EvidenceSummary(
            RequiredEvidenceTokens.Length,
            observedKeysInOrder,
            missingKeys,
            OptionalEvidenceTokens.Length,
            optionalObservedKeysInOrder
        );
    }

    private readonly record struct RequiredPhase0EvidenceToken(string Key, string[] Tokens)
    {
        public RequiredPhase0EvidenceToken(string key, string token)
            : this(key, [token])
        {
        }

        public bool Matches(string line)
        {
            // 先頭を優先語彙にし、移行中の旧ログも同じ evidence key へ畳み込む。
            foreach (string token in Tokens)
            {
                if (line.Contains(token, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

public sealed class DebugRuntimeLogPhase0EvidenceSummary
{
    internal DebugRuntimeLogPhase0EvidenceSummary(
        int totalRequiredCount,
        IReadOnlyList<string> observedKeys,
        IReadOnlyList<string> missingKeys
    )
        : this(totalRequiredCount, observedKeys, missingKeys, 0, [])
    {
    }

    internal DebugRuntimeLogPhase0EvidenceSummary(
        int totalRequiredCount,
        IReadOnlyList<string> observedKeys,
        IReadOnlyList<string> missingKeys,
        int totalOptionalCount,
        IReadOnlyList<string> optionalObservedKeys
    )
    {
        TotalRequiredCount = totalRequiredCount;
        ObservedKeys = observedKeys;
        MissingKeys = missingKeys;
        TotalOptionalCount = totalOptionalCount;
        OptionalObservedKeys = optionalObservedKeys;
    }

    public int TotalRequiredCount { get; }

    public int ObservedCount => ObservedKeys.Count;

    public int TotalOptionalCount { get; }

    public int OptionalObservedCount => OptionalObservedKeys.Count;

    public bool IsComplete => MissingKeys.Count == 0;

    public IReadOnlyList<string> MissingKeys { get; }

    public IReadOnlyList<string> OptionalObservedKeys { get; }

    public IReadOnlyList<string> ObservedKeys { get; }

    public string BuildSummaryText()
    {
        string missing = MissingKeys.Count == 0 ? "none" : string.Join(",", MissingKeys);
        string summary = $"phase0_log_evidence={ObservedCount}/{TotalRequiredCount} missing={missing}";

        if (OptionalObservedKeys.Count == 0)
        {
            return summary;
        }

        return $"{summary} optional={string.Join(",", OptionalObservedKeys)}";
    }
}
