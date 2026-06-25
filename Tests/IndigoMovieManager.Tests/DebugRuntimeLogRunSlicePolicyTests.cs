using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class DebugRuntimeLogRunSlicePolicyTests
{
    [Test]
    public void SliceLatestRun_単一runなら全行を返す()
    {
        string first = BuildLine(1, "startup begin");
        string second = BuildLine(2, "first-page shown");
        string third = BuildLine(3, "input ready");

        DebugRuntimeLogRunSliceResult result = DebugRuntimeLogRunSlicePolicy.SliceLatestRun(
            new[] { first, second, third }
        );

        Assert.That(result.Lines, Is.EqualTo(new[] { first, second, third }));
        Assert.That(result.HasSequence, Is.True);
        Assert.That(result.StartSequence, Is.EqualTo(1));
        Assert.That(result.EndSequence, Is.EqualTo(3));
        Assert.That(result.DetectedResetCount, Is.EqualTo(0));
        Assert.That(result.SourceLineCount, Is.EqualTo(3));
    }

    [Test]
    public void SliceLatestRun_連番が戻ったら後半runだけ返す()
    {
        string oldFirst = BuildLine(1, "old startup");
        string oldLast = BuildLine(3, "old input ready");
        string newFirst = BuildLine(1, "new startup");
        string newSecond = BuildLine(2, "new first-page shown");

        DebugRuntimeLogRunSliceResult result = DebugRuntimeLogRunSlicePolicy.SliceLatestRun(
            new[] { oldFirst, oldLast, newFirst, newSecond }
        );

        Assert.That(result.Lines, Is.EqualTo(new[] { newFirst, newSecond }));
        Assert.That(result.HasSequence, Is.True);
        Assert.That(result.StartSequence, Is.EqualTo(1));
        Assert.That(result.EndSequence, Is.EqualTo(2));
        Assert.That(result.DetectedResetCount, Is.EqualTo(1));
        Assert.That(result.SourceLineCount, Is.EqualTo(4));
    }

    [Test]
    public void SliceLatestRun_sequenceなし行が最新run内にあっても一緒に残る()
    {
        string oldLast = BuildLine(3, "old input ready");
        string newFirst = BuildLine(1, "new startup");
        string helper = "diagnostic helper line without sequence";
        string newSecond = BuildLine(2, "new input ready");

        DebugRuntimeLogRunSliceResult result = DebugRuntimeLogRunSlicePolicy.SliceLatestRun(
            new[] { oldLast, newFirst, helper, newSecond }
        );

        Assert.That(result.Lines, Is.EqualTo(new[] { newFirst, helper, newSecond }));
        Assert.That(result.HasSequence, Is.True);
        Assert.That(result.StartSequence, Is.EqualTo(1));
        Assert.That(result.EndSequence, Is.EqualTo(2));
        Assert.That(result.DetectedResetCount, Is.EqualTo(1));
        Assert.That(result.SourceLineCount, Is.EqualTo(4));
    }

    [Test]
    public void SliceLatestRun_空または空白だけなら空結果を返す()
    {
        DebugRuntimeLogRunSliceResult result = DebugRuntimeLogRunSlicePolicy.SliceLatestRun(
            new[] { "", "   ", "\t" }
        );

        Assert.That(result.Lines, Is.Empty);
        Assert.That(result.HasSequence, Is.False);
        Assert.That(result.StartSequence, Is.Null);
        Assert.That(result.EndSequence, Is.Null);
        Assert.That(result.DetectedResetCount, Is.EqualTo(0));
        Assert.That(result.SourceLineCount, Is.EqualTo(3));
    }

    [Test]
    public void BuildSummaryText_sequenceありならgrepしやすい要約を返す()
    {
        string oldLast = BuildLine(3, "old input ready");
        string newFirst = BuildLine(1, "new startup");
        string helper = "diagnostic helper line without sequence";
        string newLast = BuildLine(3, "new input ready");

        DebugRuntimeLogRunSliceResult result = DebugRuntimeLogRunSlicePolicy.SliceLatestRun(
            new[] { oldLast, newFirst, helper, newLast }
        );

        Assert.That(
            result.BuildSummaryText(),
            Is.EqualTo("log_run_lines=3/4 has_sequence=true sequence=1-3 resets=1")
        );
    }

    [Test]
    public void BuildSummaryText_sequenceなしならsequence_noneを返す()
    {
        DebugRuntimeLogRunSliceResult result = DebugRuntimeLogRunSlicePolicy.SliceLatestRun(
            new[] { "diagnostic helper line", "another helper line" }
        );

        Assert.That(
            result.BuildSummaryText(),
            Is.EqualTo("log_run_lines=2/2 has_sequence=false sequence=none resets=0")
        );
    }

    private static string BuildLine(long sequence, string message)
    {
        return DebugRuntimeLog.BuildLineForTesting(
            new DateTime(2026, 6, 25, 10, 0, 0).AddMilliseconds(sequence),
            "ui-tempo",
            message,
            sequence
        );
    }
}
