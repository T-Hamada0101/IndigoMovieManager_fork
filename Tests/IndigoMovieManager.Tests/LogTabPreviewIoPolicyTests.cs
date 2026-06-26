using System.IO;
using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class LogTabPreviewIoPolicyTests
{
    [Test]
    public void LogタブPreview更新は同期ファイルIOを背景helperへ逃がす()
    {
        string source = GetRepoText("BottomTabs", "LogTab", "MainWindow.BottomTab.Log.cs");
        string refreshMethod = ExtractMethod(
            source,
            "private async void RefreshLogTabPreview("
        );
        string loadMethod = ExtractMethod(
            source,
            "private static LogPreviewSnapshot LoadLogPreviewSnapshot("
        );
        string readMethod = ExtractMethod(source, "private static string ReadLogPreview(");
        string summaryMethod = ExtractMethod(
            source,
            "internal static string BuildLogPreviewTextWithSummary("
        );

        Assert.That(refreshMethod, Does.Contain("Task.Run("));
        Assert.That(refreshMethod, Does.Contain("LoadLogPreviewSnapshot("));
        Assert.That(refreshMethod, Does.Contain("requestId != _logTabPreviewRequestId"));
        Assert.That(refreshMethod, Does.Not.Contain("File.Exists("));
        Assert.That(refreshMethod, Does.Not.Contain("File.GetLastWriteTimeUtc("));
        Assert.That(refreshMethod, Does.Not.Contain("ReadLogPreview("));
        Assert.That(refreshMethod, Does.Not.Contain("DebugRuntimeLogRunSlicePolicy"));
        Assert.That(refreshMethod, Does.Not.Contain("DebugRuntimeLogEvidencePolicy"));
        Assert.That(refreshMethod, Does.Not.Contain("DebugRuntimeLogPhase0EvidencePolicy"));

        Assert.That(loadMethod, Does.Contain("File.Exists("));
        Assert.That(loadMethod, Does.Contain("File.GetLastWriteTimeUtc("));
        Assert.That(loadMethod, Does.Contain("ReadLogPreview(logPath)"));

        Assert.That(readMethod, Does.Contain("BuildLogPreviewTextWithSummary(text)"));
        Assert.That(summaryMethod, Does.Contain("DebugRuntimeLogAuditSummaryPolicy.Evaluate(lines)"));
        Assert.That(summaryMethod, Does.Contain("summary.BuildSummaryText()"));
        Assert.That(summaryMethod, Does.Not.Contain("DebugRuntimeLogRunSlicePolicy.SliceLatestRun("));
        Assert.That(summaryMethod, Does.Not.Contain("DebugRuntimeLogEvidencePolicy.Evaluate("));
        Assert.That(
            summaryMethod,
            Does.Not.Contain("DebugRuntimeLogPhase0EvidencePolicy.Evaluate(")
        );
    }

    [Test]
    public void BuildLogPreviewTextWithSummaryは固定順summaryの後に元previewを残す()
    {
        string[] previewLines =
        [
            BuildLine(1, "old ui_shell_contract=ui-shell-v1 first-page shown input ready"),
            BuildLine(
                2,
                "old diff_contract=readmodel-diff-v1 scheduler_contract=scheduler-v1 image_contract=image-pipeline-v1 persist_contract=persistence-write-v1 worker_contract=worker-job-v1 core_route=skin-refresh core_route=player-playback worker_kind=thumbnail-create"
            ),
            BuildLine(1, "new first-page shown"),
            BuildLine(2, "new input ready"),
            BuildLine(3, "new core_route=watch-ui-apply"),
        ];
        string previewText = string.Join(Environment.NewLine, previewLines);

        string result = IndigoMovieManager.MainWindow.BuildLogPreviewTextWithSummary(
            previewText
        );

        string[] resultLines = result.Split(Environment.NewLine);
        Assert.Multiple(() =>
        {
            Assert.That(
                resultLines[0],
                Is.EqualTo("log_run_lines=3/5 has_sequence=true sequence=1-3 resets=1")
            );
            Assert.That(
                resultLines[1],
                Is.EqualTo(
                    "log_run_window=2026-06-25T10:00:00.001..2026-06-25T10:00:00.003 elapsed_ms=2 timestamp_lines=3/3"
                )
            );
            Assert.That(
                resultLines[2],
                Is.EqualTo(
                    "log_evidence=1/9 missing=ui-shell,readmodel-diff,scheduler,image,persistence,worker,skin-core,player-core"
                )
            );
            Assert.That(
                resultLines[3],
                Is.EqualTo(
                    "phase0_log_evidence=3/12 missing=search-input,sort-input,scroll-input,player-core,image-pipeline,persistence,worker,thumbnail-worker,skin-core"
                )
            );
            Assert.That(
                resultLines[4],
                Is.EqualTo(
                    "phase0_next_actions=search,sort,scroll,player,image,persistence,thumbnail,skin"
                )
            );
            Assert.That(resultLines[5], Is.EqualTo("phase0_audit_complete=false"));
            Assert.That(resultLines[6], Is.Empty);
            Assert.That(resultLines.Skip(7), Is.EqualTo(previewLines));
        });
    }

    private static string GetRepoText(params string[] relativePathParts)
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current != null)
        {
            string candidate = Path.Combine([current.FullName, .. relativePathParts]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        Assert.Fail($"{Path.Combine(relativePathParts)} の位置を repo root から解決できませんでした。");
        return string.Empty;
    }

    private static string ExtractMethod(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{signature} が見つかりません。");

        int braceStart = source.IndexOf('{', start);
        Assert.That(braceStart, Is.GreaterThanOrEqualTo(0));

        int depth = 0;
        for (int index = braceStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(start, index - start + 1);
                }
            }
        }

        Assert.Fail($"{signature} の終端を解決できませんでした。");
        return string.Empty;
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
