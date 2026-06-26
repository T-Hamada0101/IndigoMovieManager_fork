using System.Text;
using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager.Tests;

[TestFixture]
[NonParallelizable]
public sealed class DebugRuntimeLogPhase0LiveAuditTests
{
    private const string EnabledEnvName = "IMM_PHASE0_LOG_AUDIT_LIVE";
    private const string LogPathEnvName = "IMM_PHASE0_LOG_AUDIT_PATH";

    [Test]
    public void OptIn_live_auditは実機採取済みdebug_runtime_logを検証するだけで実機採取の代替にしない()
    {
        if (Environment.GetEnvironmentVariable(EnabledEnvName)?.Trim() != "1")
        {
            Assert.Ignore($"{EnabledEnvName}=1 の時だけ採取済み debug-runtime.log を検証します。");
            return;
        }

        string logPath = ResolveLogPath();
        if (!File.Exists(logPath))
        {
            DebugRuntimeLogAuditSummary summary = DebugRuntimeLogAuditSummaryPolicy.Evaluate([]);
            FailWithSummary(
                $"ログファイルが見つかりません。{LogPathEnvName} で対象を指定できます: {logPath}",
                summary
            );
            return;
        }

        string[] lines = ReadAllLinesAllowingWriter(logPath);
        DebugRuntimeLogAuditSummary auditSummary = AssertLiveAuditIsComplete(logPath, lines);

        TestContext.Out.WriteLine(auditSummary.BuildSummaryText());
    }

    [Test]
    public void 合成ログでもcontract_evidence不足はsummary付きで失敗する()
    {
        string logPath = "%LOCALAPPDATA%\\IndigoMovieManager\\logs\\debug-runtime.log";
        string[] lines = BuildSequencedLines(
            [
                "startup first-page shown",
                "startup input ready",
                "ui shell input: operation_reason=search ui_shell_contract=ui-shell-v1",
                "ui shell input: operation_reason=sort",
                "page scroll end:",
                "image image_contract=image-pipeline-v1",
                "save persist_contract=persistence-write-v1",
                "worker worker_contract=worker-job-v1",
                "thumbnail worker_kind=thumbnail-create",
                "skin core_route=skin-refresh",
                "player core_route=player-playback",
                "watch core_route=watch-ui-apply",
            ]
        );
        DebugRuntimeLogAuditSummary summary = DebugRuntimeLogAuditSummaryPolicy.Evaluate(lines);

        NUnit.Framework.AssertionException? ex = Assert.Throws<NUnit.Framework.AssertionException>(
            () => AssertLiveAuditIsComplete(logPath, lines)
        );

        Assert.That(ex?.Message, Does.Contain("contract evidence"));
        Assert.That(ex?.Message, Does.Contain(logPath));
        Assert.That(ex?.Message, Does.Contain(summary.BuildSummaryText()));
        Assert.That(ex?.Message, Does.Contain("phase0_audit_complete=false"));
    }

    [Test]
    public void 合成ログでcontractとPhase0の全tokenが揃う時は失敗しない()
    {
        string[] lines = BuildSequencedLines(AllEvidenceMessages());

        DebugRuntimeLogAuditSummary summary = AssertLiveAuditIsComplete(
            "%LOCALAPPDATA%\\IndigoMovieManager\\logs\\debug-runtime.log",
            lines
        );

        Assert.Multiple(() =>
        {
            Assert.That(summary.ContractEvidence.IsComplete, Is.True);
            Assert.That(summary.Phase0Evidence.IsComplete, Is.True);
            Assert.That(
                summary.Phase0Evidence.OptionalObservedKeys,
                Is.EqualTo(
                    [
                        "manual-reload-input",
                        "readmodel-diff-single",
                        "readmodel-diff-total",
                        "image-aggregate-decode-plan",
                        "image-stale-discard",
                        "worker-diagnostic-context",
                        "worker-capability-count",
                        "worker-metric-count",
                    ]
                )
            );
            Assert.That(summary.IsComplete, Is.True);
        });
    }

    private static string ResolveLogPath()
    {
        string explicitPath = Environment.GetEnvironmentVariable(LogPathEnvName)?.Trim().Trim('"') ?? "";
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IndigoMovieManager",
            "logs",
            "debug-runtime.log"
        );
    }

    private static string[] ReadAllLinesAllowingWriter(string logPath)
    {
        // アプリ本体がログを書いている最中でも、監査側は同じファイルを読み取れるようにする。
        using FileStream stream = new(
            logPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        using StreamReader reader = new(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true
        );

        List<string> lines = [];
        while (reader.ReadLine() is string line)
        {
            lines.Add(line);
        }

        return lines.ToArray();
    }

    private static DebugRuntimeLogAuditSummary AssertLiveAuditIsComplete(
        string logPath,
        IReadOnlyCollection<string> lines
    )
    {
        DebugRuntimeLogAuditSummary auditSummary = DebugRuntimeLogAuditSummaryPolicy.Evaluate(lines);

        // live監査の完了条件をここへ集め、opt-in実行と合成ログテストで同じ順に検査する。
        if (lines.Count == 0 || lines.All(string.IsNullOrWhiteSpace))
        {
            FailWithSummary($"ログファイルが空です: {logPath}", auditSummary);
        }

        if (!auditSummary.RunWindow.HasTimestamp)
        {
            FailWithSummary($"最新runのtimestampを確認できません: {logPath}", auditSummary);
        }

        if (!auditSummary.ContractEvidence.IsComplete)
        {
            FailWithSummary($"contract evidence がまだ揃っていません: {logPath}", auditSummary);
        }

        if (!auditSummary.Phase0Evidence.IsComplete)
        {
            FailWithSummary($"Phase0 evidence がまだ揃っていません: {logPath}", auditSummary);
        }

        return auditSummary;
    }

    private static void FailWithSummary(string reason, DebugRuntimeLogAuditSummary summary)
    {
        Assert.Fail(
            string.Join(
                Environment.NewLine,
                reason,
                "実機操作で同一 Release run の startup / search / sort / scroll / Player / watch / image / persistence / thumbnail / skin を採取してから再実行してください。",
                summary.BuildSummaryText()
            )
        );
    }

    private static string[] BuildSequencedLines(IReadOnlyList<string> messages)
    {
        return messages.Select((message, index) => BuildLine(index + 1, message)).ToArray();
    }

    private static string[] AllEvidenceMessages()
    {
        return
        [
            "startup first-page shown",
            "startup input ready",
            "input ui shell input: operation_reason=search ui_shell_contract=ui-shell-v1",
            "input ui shell input: operation_reason=sort",
            "input ui shell input: operation_reason=manual-reload",
            "scroll page scroll end:",
            "apply diff_contract=readmodel-diff-v1 diff_change_set=single diff_changed_total=1",
            "queue scheduler_contract=scheduler-v1",
            "image image_contract=image-pipeline-v1",
            "image image_log_reason=image.thumbnail-error-list.aggregate-decode-plan",
            "detail failure_reason=stale-player-right-rail",
            "save persist_contract=persistence-write-v1",
            "worker worker_contract=worker-job-v1 diagnostic_context_count=7 capability_count=3",
            "worker result metric_count=2",
            "thumbnail worker_kind=thumbnail-create",
            "skin core_route=skin-refresh",
            "player core_route=player-playback",
            "watch core_route=watch-ui-apply",
        ];
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
