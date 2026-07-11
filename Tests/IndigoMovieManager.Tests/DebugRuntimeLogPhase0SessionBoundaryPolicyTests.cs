using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class DebugRuntimeLogPhase0SessionBoundaryPolicyTests
{
    [Test]
    public void session開始時刻以後で連番が1からならsatisfiedを返す()
    {
        DateTime sessionStartedLocal = new(2026, 7, 11, 9, 30, 0, 123);

        DebugRuntimeLogPhase0SessionBoundaryResult result =
            DebugRuntimeLogPhase0SessionBoundaryPolicy.Evaluate(
                CreateRunWindow(sessionStartedLocal),
                CreateRunSlice(hasSequence: true, startSequence: 1),
                sessionStartedLocal
            );

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSatisfied, Is.True);
            Assert.That(result.Reason, Is.EqualTo("satisfied"));
        });
    }

    [Test]
    public void session開始より前のrunはrun_before_sessionを返す()
    {
        DateTime sessionStartedLocal = new(2026, 7, 11, 9, 30, 0, 123);
        DateTime runStartedLocal = sessionStartedLocal.AddMilliseconds(-1);

        DebugRuntimeLogPhase0SessionBoundaryResult result =
            DebugRuntimeLogPhase0SessionBoundaryPolicy.Evaluate(
                CreateRunWindow(runStartedLocal),
                CreateRunSlice(hasSequence: true, startSequence: 1),
                sessionStartedLocal
            );

        Assert.That(result, Is.EqualTo(new DebugRuntimeLogPhase0SessionBoundaryResult(false, "run-before-session")));
    }

    [Test]
    public void timestampなしはmissing_run_timestampを返す()
    {
        DebugRuntimeLogPhase0SessionBoundaryResult result =
            DebugRuntimeLogPhase0SessionBoundaryPolicy.Evaluate(
                new DebugRuntimeLogRunWindowSummary(0, 0, null, null),
                CreateRunSlice(hasSequence: true, startSequence: 1),
                new DateTime(2026, 7, 11, 9, 30, 0, 123)
            );

        Assert.That(result, Is.EqualTo(new DebugRuntimeLogPhase0SessionBoundaryResult(false, "missing-run-timestamp")));
    }

    [Test]
    public void sequenceなしはmissing_sequenceを返す()
    {
        DateTime sessionStartedLocal = new(2026, 7, 11, 9, 30, 0, 123);

        DebugRuntimeLogPhase0SessionBoundaryResult result =
            DebugRuntimeLogPhase0SessionBoundaryPolicy.Evaluate(
                CreateRunWindow(sessionStartedLocal),
                CreateRunSlice(hasSequence: false, startSequence: null),
                sessionStartedLocal
            );

        Assert.That(result, Is.EqualTo(new DebugRuntimeLogPhase0SessionBoundaryResult(false, "missing-sequence")));
    }

    [Test]
    public void 開始sequenceが1以外ならstart_sequence_not_oneを返す()
    {
        DateTime sessionStartedLocal = new(2026, 7, 11, 9, 30, 0, 123);

        DebugRuntimeLogPhase0SessionBoundaryResult result =
            DebugRuntimeLogPhase0SessionBoundaryPolicy.Evaluate(
                CreateRunWindow(sessionStartedLocal),
                CreateRunSlice(hasSequence: true, startSequence: 2),
                sessionStartedLocal
            );

        Assert.That(result, Is.EqualTo(new DebugRuntimeLogPhase0SessionBoundaryResult(false, "start-sequence-not-one")));
    }

    [Test]
    public void session開始時刻はローカル時刻の厳密形式だけを受け付ける()
    {
        bool parsed = DebugRuntimeLogPhase0SessionBoundaryPolicy.TryParseSessionStartedLocal(
            "2026-07-11 09:30:00.123",
            out DateTime sessionStartedLocal
        );
        bool malformed = DebugRuntimeLogPhase0SessionBoundaryPolicy.TryParseSessionStartedLocal(
            "2026-07-11T09:30:00.123",
            out _
        );

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(sessionStartedLocal, Is.EqualTo(new DateTime(2026, 7, 11, 9, 30, 0, 123)));
            Assert.That(sessionStartedLocal.Kind, Is.EqualTo(DateTimeKind.Unspecified));
            Assert.That(malformed, Is.False);
        });
    }

    private static DebugRuntimeLogRunWindowSummary CreateRunWindow(DateTime firstTimestamp)
    {
        return new DebugRuntimeLogRunWindowSummary(
            SourceLineCount: 2,
            TimestampLineCount: 2,
            FirstTimestamp: firstTimestamp,
            LastTimestamp: firstTimestamp.AddMilliseconds(1)
        );
    }

    private static DebugRuntimeLogRunSliceResult CreateRunSlice(bool hasSequence, long? startSequence)
    {
        return new DebugRuntimeLogRunSliceResult(
            Lines: [],
            HasSequence: hasSequence,
            StartSequence: startSequence,
            EndSequence: startSequence,
            DetectedResetCount: 0,
            SourceLineCount: 0
        );
    }
}
