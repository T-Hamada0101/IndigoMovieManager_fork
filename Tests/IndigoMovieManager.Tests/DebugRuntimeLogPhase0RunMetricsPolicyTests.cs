using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class DebugRuntimeLogPhase0RunMetricsPolicyTests
{
    [Test]
    public void UiHangUpdatedのdelayMsだけをnearestRankで要約する()
    {
        DebugRuntimeLogPhase0RunMetricsSummary summary =
            DebugRuntimeLogPhase0RunMetricsPolicy.Evaluate(
                [
                    "ui hang detected: delay_ms=999",
                    "ui hang updated: delay_ms=40",
                    "ui hang updated: delay_ms=10",
                    "ui hang updated: delay_ms=30",
                    "ui hang updated: delay_ms=20",
                    "ui hang updated: delay_ms=50",
                ]
            );

        Assert.Multiple(() =>
        {
            Assert.That(summary.UiHangDelaySampleCount, Is.EqualTo(5));
            Assert.That(summary.UiHangDelayP50Ms, Is.EqualTo(30));
            Assert.That(summary.UiHangDelayP95Ms, Is.EqualTo(50));
            Assert.That(summary.UiHangDelayMaxMs, Is.EqualTo(50));
        });
    }

    [Test]
    public void 非負でないdelayMsや不正fieldはsampleに含めない()
    {
        DebugRuntimeLogPhase0RunMetricsSummary summary =
            DebugRuntimeLogPhase0RunMetricsPolicy.Evaluate(
                [
                    "ui hang updated: delay_ms=-1",
                    "ui hang updated: delay_ms=bad",
                    "ui hang updated: other_delay_ms=100",
                    "ui hang recovered delay_ms=100",
                ]
            );

        Assert.That(
            summary.BuildSummaryText(),
            Is.EqualTo(
                "phase0_run_metrics=available ui_hang_delay_samples=0 ui_hang_delay_p50_ms=none ui_hang_delay_p95_ms=none ui_hang_delay_max_ms=none max_queue_depth=none stale_discard_log_count=0 full_fallback_log_count=0"
            )
        );
    }

    [Test]
    public void QueueDepthとstaleFallbackのlogLineCountを条件どおり要約する()
    {
        DebugRuntimeLogPhase0RunMetricsSummary summary =
            DebugRuntimeLogPhase0RunMetricsPolicy.Evaluate(
                [
                    "queue queue_depth_before=2 queue_depth_after=6 pending_count=4 pending_count_after=3",
                    "queue pending_count_after=9 queue_depth_after=-1",
                    "image failure_reason=stale-unrelated failure_reason=stale-image-request failure_reason=stale-player-right-rail",
                    "apply diff_contract=readmodel-diff-v1 diff_full_fallback_reason=none",
                    "apply diff_contract=readmodel-diff-v1 diff_full_fallback_reason=db-switch",
                    "apply diff_full_fallback_reason=query-changed",
                ]
            );

        Assert.Multiple(() =>
        {
            Assert.That(summary.MaxQueueDepth, Is.EqualTo(9));
            Assert.That(summary.StaleDiscardLogCount, Is.EqualTo(1));
            Assert.That(summary.FullFallbackLogCount, Is.EqualTo(1));
        });
    }
}
