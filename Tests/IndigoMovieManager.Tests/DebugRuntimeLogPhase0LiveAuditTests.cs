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
        DebugRuntimeLogAuditSummary auditSummary = DebugRuntimeLogAuditSummaryPolicy.Evaluate(lines);

        if (lines.Length == 0 || lines.All(string.IsNullOrWhiteSpace))
        {
            FailWithSummary($"ログファイルが空です: {logPath}", auditSummary);
        }

        if (!auditSummary.RunWindow.HasTimestamp)
        {
            FailWithSummary($"最新runのtimestampを確認できません: {logPath}", auditSummary);
        }

        if (!auditSummary.Phase0Evidence.IsComplete)
        {
            FailWithSummary($"Phase0 evidence がまだ揃っていません: {logPath}", auditSummary);
        }

        TestContext.Out.WriteLine(auditSummary.BuildSummaryText());
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

    private static void FailWithSummary(string reason, DebugRuntimeLogAuditSummary summary)
    {
        Assert.Fail(
            string.Join(
                Environment.NewLine,
                reason,
                "実機操作で同一 Release run の search / sort / scroll / Player / watch / thumbnail / skin を採取してから再実行してください。",
                summary.BuildSummaryText()
            )
        );
    }
}
