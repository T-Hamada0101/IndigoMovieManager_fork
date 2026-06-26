using System.Runtime.CompilerServices;
using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class DebugRuntimeLogPhase0LiveAuditSourcePolicyTests
{
    [Test]
    public void Live監査は環境変数opt_inでだけ実行する()
    {
        string source = GetTargetSource();
        string liveTest = GetMethodBlock(source, "public void OptIn_live_audit");

        Assert.Multiple(() =>
        {
            Assert.That(
                source,
                Does.Contain(
                    "private const string EnabledEnvName = \"IMM_PHASE0_LOG_AUDIT_LIVE\";"
                )
            );
            Assert.That(
                liveTest,
                Does.Contain("Environment.GetEnvironmentVariable(EnabledEnvName)?.Trim() != \"1\"")
            );
            Assert.That(liveTest, Does.Contain("Assert.Ignore("));
        });
    }

    [Test]
    public void ログパスはenv_overrideとLOCALAPPDATA既定だけを使う()
    {
        string source = GetTargetSource();
        string resolveMethod = GetMethodBlock(source, "private static string ResolveLogPath()");

        Assert.Multiple(() =>
        {
            Assert.That(
                source,
                Does.Contain(
                    "private const string LogPathEnvName = \"IMM_PHASE0_LOG_AUDIT_PATH\";"
                )
            );
            Assert.That(
                resolveMethod,
                Does.Contain("Environment.GetEnvironmentVariable(LogPathEnvName)?.Trim().Trim")
            );
            Assert.That(
                resolveMethod,
                Does.Contain(
                    "Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)"
                )
            );
            Assert.That(resolveMethod, Does.Contain("\"IndigoMovieManager\""));
            Assert.That(resolveMethod, Does.Contain("\"logs\""));
            Assert.That(resolveMethod, Does.Contain("\"debug-runtime.log\""));
            Assert.That(source, Does.Not.Contain(@"C:\Users\"));
            Assert.That(source, Does.Not.Contain(@"C:\\Users\\"));
            Assert.That(source, Does.Not.Contain("C:/Users/"));
        });
    }

    [Test]
    public void ログ読み込みは本体書き込み中でも共有読み取りできる()
    {
        string source = GetTargetSource();
        string readMethod = GetMethodBlock(
            source,
            "private static string[] ReadAllLinesAllowingWriter("
        );

        Assert.Multiple(() =>
        {
            Assert.That(readMethod, Does.Contain("FileMode.Open"));
            Assert.That(readMethod, Does.Contain("FileAccess.Read"));
            Assert.That(readMethod, Does.Contain("FileShare.ReadWrite | FileShare.Delete"));
        });
    }

    [Test]
    public void Live監査の完了条件はcontractとPhase0を両方見る()
    {
        string source = GetTargetSource();
        string liveTest = GetMethodBlock(source, "public void OptIn_live_audit");
        string auditMethod = GetMethodBlock(
            source,
            "private static DebugRuntimeLogAuditSummary AssertLiveAuditIsComplete("
        );

        Assert.Multiple(() =>
        {
            Assert.That(liveTest, Does.Contain("AssertLiveAuditIsComplete(logPath, lines)"));
            Assert.That(
                auditMethod,
                Does.Contain("DebugRuntimeLogAuditSummaryPolicy.Evaluate(lines)")
            );
            Assert.That(auditMethod, Does.Contain("!auditSummary.ContractEvidence.IsComplete"));
            Assert.That(auditMethod, Does.Contain("!auditSummary.Phase0Evidence.IsComplete"));
        });
    }

    [Test]
    public void 失敗メッセージはsummaryとPhase0操作語彙を出す()
    {
        string source = GetTargetSource();
        string failMethod = GetMethodBlock(source, "private static void FailWithSummary(");
        string guideText = DebugRuntimeLogPhase0NextActionPolicy.BuildFullCaptureGuideText();

        // 失敗時に次の実機採取操作がすぐ分かることを source policy として守る。
        Assert.Multiple(() =>
        {
            Assert.That(failMethod, Does.Contain("summary.BuildSummaryText()"));
            Assert.That(
                failMethod,
                Does.Contain("DebugRuntimeLogPhase0NextActionPolicy.BuildFullCaptureGuideText()")
            );
            Assert.That(
                failMethod,
                Does.Not.Contain(
                    "startup / search / sort / scroll / Player / watch / image / persistence / thumbnail / skin"
                )
            );
            Assert.That(
                guideText,
                Is.EqualTo(
                    "startup / search / sort / scroll / Player / watch / image / persistence / thumbnail / skin"
                )
            );
        });
    }

    private static string GetTargetSource()
    {
        return GetRepoText(
            "Tests",
            "IndigoMovieManager.Tests",
            "DebugRuntimeLogPhase0LiveAuditTests.cs"
        );
    }

    private static string GetRepoText(params string[] relativePathParts)
    {
        string repoRoot = FindRepoRoot();
        string candidate = Path.Combine([repoRoot, .. relativePathParts]);
        Assert.That(File.Exists(candidate), Is.True, candidate);
        return File.ReadAllText(candidate);
    }

    private static string FindRepoRoot([CallerFilePath] string callerFilePath = "")
    {
        // 呼び出し元から親へたどり、テスト実行場所に依存しない repo root を探す。
        DirectoryInfo? current = new(Path.GetDirectoryName(callerFilePath) ?? Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (
                File.Exists(Path.Combine(current.FullName, "IndigoMovieManager.csproj"))
                && Directory.Exists(Path.Combine(current.FullName, "Tests"))
            )
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        Assert.Fail("repo root を解決できませんでした。");
        return "";
    }

    private static string GetMethodBlock(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{signature} が見つかりません。");

        int bodyStart = source.IndexOf('{', start);
        Assert.That(bodyStart, Is.GreaterThanOrEqualTo(0), $"{signature} の本文開始が見つかりません。");

        int depth = 0;
        for (int index = bodyStart; index < source.Length; index++)
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

        Assert.Fail($"{signature} の本文終了が見つかりません。");
        return "";
    }
}
