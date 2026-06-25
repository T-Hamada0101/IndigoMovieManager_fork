#nullable enable

using System.Collections.Generic;

namespace IndigoMovieManager.Infrastructure;

public static class DebugRuntimeLogAuditSummaryPolicy
{
    public static DebugRuntimeLogAuditSummary Evaluate(IEnumerable<string>? lines)
    {
        DebugRuntimeLogRunSliceResult runSlice = DebugRuntimeLogRunSlicePolicy.SliceLatestRun(
            lines
        );
        DebugRuntimeLogEvidenceSummary contractEvidence = DebugRuntimeLogEvidencePolicy.Evaluate(
            runSlice.Lines
        );
        DebugRuntimeLogPhase0EvidenceSummary phase0Evidence =
            DebugRuntimeLogPhase0EvidencePolicy.Evaluate(runSlice.Lines);

        return new DebugRuntimeLogAuditSummary(runSlice, contractEvidence, phase0Evidence);
    }
}

public sealed class DebugRuntimeLogAuditSummary
{
    internal DebugRuntimeLogAuditSummary(
        DebugRuntimeLogRunSliceResult runSlice,
        DebugRuntimeLogEvidenceSummary contractEvidence,
        DebugRuntimeLogPhase0EvidenceSummary phase0Evidence
    )
    {
        RunSlice = runSlice;
        ContractEvidence = contractEvidence;
        Phase0Evidence = phase0Evidence;
    }

    public DebugRuntimeLogRunSliceResult RunSlice { get; }

    public DebugRuntimeLogEvidenceSummary ContractEvidence { get; }

    public DebugRuntimeLogPhase0EvidenceSummary Phase0Evidence { get; }

    public string BuildSummaryText()
    {
        // 監査入口では、最新run、契約evidence、Phase0操作evidenceを固定順で並べる。
        return string.Join(
            "\n",
            RunSlice.BuildSummaryText(),
            ContractEvidence.BuildSummaryText(),
            Phase0Evidence.BuildSummaryText()
        );
    }
}
