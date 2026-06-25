using System.Globalization;
using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class DebugRuntimeLogRunWindowPolicyTests
{
    [Test]
    public void 複数timestamp行からfirst_last_elapsedと件数を返す()
    {
        DateTime first = new(2026, 6, 25, 18, 15, 30, 451);
        DateTime middle = new(2026, 6, 25, 18, 15, 30, 500);
        DateTime last = new(2026, 6, 25, 18, 15, 30, 530);

        DebugRuntimeLogRunWindowSummary summary = DebugRuntimeLogRunWindowPolicy.Evaluate(
            [BuildLine(first), BuildLine(middle), BuildLine(last)]
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.SourceLineCount, Is.EqualTo(3));
            Assert.That(summary.TimestampLineCount, Is.EqualTo(3));
            Assert.That(summary.HasTimestamp, Is.True);
            Assert.That(summary.FirstTimestamp, Is.EqualTo(first));
            Assert.That(summary.LastTimestamp, Is.EqualTo(last));
            Assert.That(summary.ElapsedMilliseconds, Is.EqualTo(79));
            Assert.That(
                summary.BuildSummaryText(),
                Is.EqualTo(
                    "log_run_window=2026-06-25T18:15:30.451..2026-06-25T18:15:30.530 elapsed_ms=79 timestamp_lines=3/3"
                )
            );
        });
    }

    [Test]
    public void timestampなし行が混じってもsourceだけに数える()
    {
        DateTime first = new(2026, 6, 25, 18, 15, 30, 451);
        DateTime last = new(2026, 6, 25, 18, 15, 30, 530);

        DebugRuntimeLogRunWindowSummary summary = DebugRuntimeLogRunWindowPolicy.Evaluate(
            [
                BuildLine(first),
                "diagnostic helper line without timestamp",
                "",
                BuildLine(last),
            ]
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.SourceLineCount, Is.EqualTo(4));
            Assert.That(summary.TimestampLineCount, Is.EqualTo(2));
            Assert.That(summary.FirstTimestamp, Is.EqualTo(first));
            Assert.That(summary.LastTimestamp, Is.EqualTo(last));
            Assert.That(summary.ElapsedMilliseconds, Is.EqualTo(79));
            Assert.That(
                summary.BuildSummaryText(),
                Is.EqualTo(
                    "log_run_window=2026-06-25T18:15:30.451..2026-06-25T18:15:30.530 elapsed_ms=79 timestamp_lines=2/4"
                )
            );
        });
    }

    [Test]
    public void timestampなし空null入力でも例外なく安定summaryを返す()
    {
        DebugRuntimeLogRunWindowSummary none = DebugRuntimeLogRunWindowPolicy.Evaluate(
            ["diagnostic helper line", " 2026-06-25 18:15:30.451 leading-space"]
        );
        DebugRuntimeLogRunWindowSummary empty = DebugRuntimeLogRunWindowPolicy.Evaluate([]);
        DebugRuntimeLogRunWindowSummary nullInput = DebugRuntimeLogRunWindowPolicy.Evaluate(null);

        Assert.Multiple(() =>
        {
            Assert.That(none.HasTimestamp, Is.False);
            Assert.That(none.FirstTimestamp, Is.Null);
            Assert.That(none.LastTimestamp, Is.Null);
            Assert.That(none.ElapsedMilliseconds, Is.Null);
            Assert.That(
                none.BuildSummaryText(),
                Is.EqualTo("log_run_window=none elapsed_ms=none timestamp_lines=0/2")
            );
            Assert.That(
                empty.BuildSummaryText(),
                Is.EqualTo("log_run_window=none elapsed_ms=none timestamp_lines=0/0")
            );
            Assert.That(
                nullInput.BuildSummaryText(),
                Is.EqualTo("log_run_window=none elapsed_ms=none timestamp_lines=0/0")
            );
        });
    }

    [Test]
    public void 行頭以外の日時っぽい文字列はtimestampとして扱わない()
    {
        DebugRuntimeLogRunWindowSummary summary = DebugRuntimeLogRunWindowPolicy.Evaluate(
            [
                "prefix 2026-06-25 18:15:30.451 #000001 [ui-tempo] hidden timestamp",
                "message contains 2026-06-25 18:15:30.530 but not at head",
            ]
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.SourceLineCount, Is.EqualTo(2));
            Assert.That(summary.TimestampLineCount, Is.EqualTo(0));
            Assert.That(summary.HasTimestamp, Is.False);
            Assert.That(
                summary.BuildSummaryText(),
                Is.EqualTo("log_run_window=none elapsed_ms=none timestamp_lines=0/2")
            );
        });
    }

    private static string BuildLine(DateTime timestamp)
    {
        return string.Concat(
            timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            " #000001 [ui-tempo] message"
        );
    }
}
