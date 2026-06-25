#nullable enable

using System;
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
        DebugRuntimeLogRunWindowSummary runWindow =
            DebugRuntimeLogRunWindowPolicy.Evaluate(runSlice.Lines);
        DebugRuntimeLogPhase0EvidenceSummary phase0Evidence =
            DebugRuntimeLogPhase0EvidencePolicy.Evaluate(runSlice.Lines);
        DebugRuntimeLogPhase0NextActionSummary phase0NextActions =
            DebugRuntimeLogPhase0NextActionPolicy.Evaluate(phase0Evidence);

        return new DebugRuntimeLogAuditSummary(
            runSlice,
            runWindow,
            contractEvidence,
            phase0Evidence,
            phase0NextActions
        );
    }
}

public sealed class DebugRuntimeLogAuditSummary
{
    internal DebugRuntimeLogAuditSummary(
        DebugRuntimeLogRunSliceResult runSlice,
        DebugRuntimeLogRunWindowSummary runWindow,
        DebugRuntimeLogEvidenceSummary contractEvidence,
        DebugRuntimeLogPhase0EvidenceSummary phase0Evidence,
        DebugRuntimeLogPhase0NextActionSummary phase0NextActions
    )
    {
        RunSlice = runSlice;
        RunWindow = runWindow;
        ContractEvidence = contractEvidence;
        Phase0Evidence = phase0Evidence;
        Phase0NextActions = phase0NextActions;
    }

    public DebugRuntimeLogRunSliceResult RunSlice { get; }

    public DebugRuntimeLogRunWindowSummary RunWindow { get; }

    public DebugRuntimeLogEvidenceSummary ContractEvidence { get; }

    public DebugRuntimeLogPhase0EvidenceSummary Phase0Evidence { get; }

    public DebugRuntimeLogPhase0NextActionSummary Phase0NextActions { get; }

    public string BuildSummaryText()
    {
        // 監査入口では、run範囲、契約evidence、Phase0の次操作を固定順で並べる。
        return string.Join(
            Environment.NewLine,
            RunSlice.BuildSummaryText(),
            RunWindow.BuildSummaryText(),
            ContractEvidence.BuildSummaryText(),
            Phase0Evidence.BuildSummaryText(),
            Phase0NextActions.BuildSummaryText()
        );
    }
}
