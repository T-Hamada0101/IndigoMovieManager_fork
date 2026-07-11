using System.Runtime.CompilerServices;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class Phase0LiveAuditScriptPolicyTests
{
    [Test]
    public void 引数と既定値を固定する()
    {
        string source = GetTargetSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("[string]$LogPath"));
            Assert.That(source, Does.Contain("[string]$Configuration = 'Release'"));
            Assert.That(source, Does.Contain("[string]$Platform = 'x64'"));
            Assert.That(source, Does.Contain("[switch]$NoBuild"));
            Assert.That(source, Does.Contain("[string]$ManualReviewPath"));
            Assert.That(source, Does.Contain("Join-Path $env:LOCALAPPDATA 'IndigoMovieManager\\logs\\debug-runtime.log'"));
            Assert.That(source, Does.Contain("Join-Path $repoRoot 'Tests/IndigoMovieManager.Tests/IndigoMovieManager.Tests.csproj'"));
        });
    }

    [Test]
    public void liveログは変更せず共有読み取りでsnapshotへ固定する()
    {
        string source = GetTargetSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Test-Path -LiteralPath $LogPath -PathType Leaf"));
            Assert.That(source, Does.Contain("Resolve-Path -LiteralPath $LogPath"));
            Assert.That(source, Does.Contain("Copy-Phase0AuditLogSnapshot"));
            Assert.That(source, Does.Contain("[System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete"));
            Assert.That(source, Does.Contain("$sourceStream.CopyTo($destinationStream)"));
            Assert.That(source, Does.Contain("[System.IO.FileMode]::CreateNew"));
            Assert.That(source, Does.Not.Contain("Move-Item"));
            Assert.That(source, Does.Not.Contain("Set-Content"));
            Assert.That(source, Does.Not.Contain("Clear-Content"));
            Assert.That(source, Does.Not.Contain("Remove-Item $LogPath"));
        });
    }

    [Test]
    public void dotnet_testはRelease_x64の対象1件だけを引数配列で実行する()
    {
        string source = GetTargetSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("$dotnetArguments = @("));
            Assert.That(source, Does.Contain("'test'"));
            Assert.That(source, Does.Contain("'-c'"));
            Assert.That(source, Does.Contain("\"-p:Platform=$Platform\""));
            Assert.That(source, Does.Contain("'FullyQualifiedName~DebugRuntimeLogPhase0LiveAuditTests&Name~OptIn_live_audit'"));
            Assert.That(source, Does.Contain("$dotnetArguments += '--no-build'"));
            Assert.That(source, Does.Contain("& dotnet @dotnetArguments"));
            Assert.That(source, Does.Not.Contain("Invoke-Expression"));
        });
    }

    [Test]
    public void 監査環境変数は実行中だけ設定し元値を復元する()
    {
        string source = GetTargetSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("IMM_PHASE0_LOG_AUDIT_LIVE"));
            Assert.That(source, Does.Contain("IMM_PHASE0_LOG_AUDIT_PATH"));
            Assert.That(source, Does.Contain("IMM_PHASE0_LOG_AUDIT_SESSION_STARTED_LOCAL"));
            Assert.That(source, Does.Contain("INDIGO_DEBUG_RUNTIME_LOG_PATH"));
            Assert.That(source, Does.Contain("[Environment]::GetEnvironmentVariable($enabledEnvironmentName, 'Process')"));
            Assert.That(source, Does.Contain("[Environment]::GetEnvironmentVariable($pathEnvironmentName, 'Process')"));
            Assert.That(source, Does.Contain("$previousSessionStartedLocalValue = [Environment]::GetEnvironmentVariable($sessionStartedLocalEnvironmentName, 'Process')"));
            Assert.That(source, Does.Contain("try"));
            Assert.That(source, Does.Contain("finally"));
            Assert.That(source, Does.Contain("Set-Item -Path \"Env:$enabledEnvironmentName\" -Value '1'"));
            Assert.That(source, Does.Contain("Set-Item -Path \"Env:$pathEnvironmentName\" -Value $auditSnapshotPath"));
            Assert.That(source, Does.Contain("Set-Item -Path \"Env:$runtimeLogPathEnvironmentName\" -Value $childRuntimeLogSinkPath"));
            Assert.That(source, Does.Contain("Set-Item -Path \"Env:$sessionStartedLocalEnvironmentName\" -Value $manualReviewSessionStartedLocal"));
            Assert.That(source, Does.Contain("Remove-Item \"Env:$enabledEnvironmentName\""));
            Assert.That(source, Does.Contain("Remove-Item \"Env:$pathEnvironmentName\""));
            Assert.That(source, Does.Contain("Remove-Item \"Env:$sessionStartedLocalEnvironmentName\""));
            Assert.That(source, Does.Contain("Remove-Item \"Env:$runtimeLogPathEnvironmentName\""));
            Assert.That(source, Does.Contain("Set-Item -Path \"Env:$runtimeLogPathEnvironmentName\" -Value $previousRuntimeLogPathValue"));
        });
    }

    [Test]
    public void snapshotと子テストsinkはTEMPに作成しfinallyで削除する()
    {
        string source = GetTargetSource();
        int finallyBlock = source.IndexOf("finally", source.IndexOf("& dotnet @dotnetArguments", StringComparison.Ordinal), StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("[System.IO.Path]::GetTempPath()"));
            Assert.That(source, Does.Contain("indigo-phase0-audit-"));
            Assert.That(source, Does.Contain("indigo-phase0-child-"));
            Assert.That(source.IndexOf("Remove-Item -LiteralPath $auditSnapshotPath", finallyBlock, StringComparison.Ordinal), Is.GreaterThan(finallyBlock));
            Assert.That(source.IndexOf("Remove-Item -LiteralPath $childRuntimeLogSinkPath", finallyBlock, StringComparison.Ordinal), Is.GreaterThan(finallyBlock));
        });
    }

    [Test]
    public void build_lock時はNoBuildを案内しプロセスを停止しない()
    {
        string source = GetTargetSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("事前build後に -NoBuild を指定してください"));
            Assert.That(source, Does.Not.Contain("Stop-Process"));
            Assert.That(source, Does.Not.Contain("taskkill"));
        });
    }

    [Test]
    public void manual未指定時はambient_sessionをdotnet前に除去し終了後に元値を復元する()
    {
        string source = GetTargetSource();
        int sessionBranch = source.IndexOf("if (-not [string]::IsNullOrWhiteSpace($manualReviewSessionStartedLocal))", StringComparison.Ordinal);
        int removeBeforeDotnet = source.IndexOf("Remove-Item \"Env:$sessionStartedLocalEnvironmentName\"", sessionBranch, StringComparison.Ordinal);
        int dotnet = source.IndexOf("& dotnet @dotnetArguments", StringComparison.Ordinal);
        int finallyBlock = source.IndexOf("finally", dotnet, StringComparison.Ordinal);
        int restoreNullBranch = source.IndexOf("if ($null -eq $previousSessionStartedLocalValue)", finallyBlock, StringComparison.Ordinal);
        int restoreValue = source.IndexOf("Set-Item -Path \"Env:$sessionStartedLocalEnvironmentName\" -Value $previousSessionStartedLocalValue", finallyBlock, StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(sessionBranch, Is.GreaterThanOrEqualTo(0));
            Assert.That(removeBeforeDotnet, Is.GreaterThan(sessionBranch));
            Assert.That(removeBeforeDotnet, Is.LessThan(dotnet));
            Assert.That(finallyBlock, Is.GreaterThan(dotnet));
            Assert.That(restoreNullBranch, Is.GreaterThan(finallyBlock));
            Assert.That(restoreValue, Is.GreaterThan(restoreNullBranch));
        });
    }

    [Test]
    public void dotnetの終了コードをfinally後も保持する()
    {
        string source = GetTargetSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("$exitCode = $LASTEXITCODE"));
            Assert.That(source, Does.Contain("exit $exitCode"));
            Assert.That(source, Does.Contain("Write-Host"));
            Assert.That(source, Does.Contain("非0終了は不足evidenceを示し、監査未完なら想定内です。"));
        });
    }

    [Test]
    public void 目視確認は構造妥当性と全pass完了を分離して検証する()
    {
        string source = GetTargetSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Get-Phase0ManualReviewValidation"));
            Assert.That(source, Does.Contain("Test-Path -LiteralPath $Path -PathType Leaf"));
            Assert.That(source, Does.Contain("Resolve-Path -LiteralPath $Path"));
            Assert.That(source, Does.Contain("ConvertFrom-Json"));
            Assert.That(source, Does.Contain("phase0-manual-review-v1"));
            Assert.That(source, Does.Contain("missing-session"));
            Assert.That(source, Does.Contain("invalid-session-id"));
            Assert.That(source, Does.Contain("invalid-session-started-local"));
            Assert.That(source, Does.Contain("[Guid]::TryParse"));
            Assert.That(source, Does.Contain("[DateTime]::TryParseExact"));
            Assert.That(source, Does.Contain("'yyyy-MM-dd HH:mm:ss.fff'"));
            Assert.That(source, Does.Contain("$allowedManualReviewStatuses = @('pending', 'pass', 'fail', 'not_observed')"));
            Assert.That(source, Does.Contain("duplicate-scenarios="));
            Assert.That(source, Does.Contain("duplicate-checks="));
            Assert.That(source, Does.Contain("missing-scenarios="));
            Assert.That(source, Does.Contain("unexpected-checks="));
            Assert.That(source, Does.Contain("if ($status -ne 'pass')"));
            Assert.That(source, Does.Contain("if (-not [string]::IsNullOrWhiteSpace($ManualReviewPath))"));
            Assert.That(source, Does.Contain("IsValid             = $isValid"));
            Assert.That(source, Does.Contain("IsComplete          = $isComplete"));
            Assert.That(source, Does.Contain("$isComplete = $isValid -and $completionIssues.Count -eq 0"));
            Assert.That(source, Does.Contain("if (-not $manualReviewValidation.IsValid)"));
            Assert.That(source, Does.Contain("if ($manualReviewValidation.IsComplete)"));
            Assert.That(source, Does.Contain("$manualReviewSessionStartedLocal = $manualReviewValidation.SessionStartedLocal"));
            Assert.That(source, Does.Contain("Phase0 manual review: incomplete"));
            Assert.That(source, Does.Not.Contain("Set-Content"));
            Assert.That(source, Does.Not.Contain("Add-Content"));
        });
    }

    [Test]
    public void 無効な目視確認だけはdotnet監査を起動せず非0で終了する()
    {
        string source = GetTargetSource();
        int validate = source.IndexOf("$manualReviewValidation = Get-Phase0ManualReviewValidation", StringComparison.Ordinal);
        int reject = source.IndexOf("if (-not $manualReviewValidation.IsValid)", StringComparison.Ordinal);
        int dotnet = source.IndexOf("& dotnet @dotnetArguments", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(validate, Is.GreaterThanOrEqualTo(0));
            Assert.That(reject, Is.GreaterThan(validate));
            Assert.That(source.IndexOf("exit 1", reject, StringComparison.Ordinal), Is.GreaterThan(reject));
            Assert.That(dotnet, Is.GreaterThan(reject));
        });
    }

    [Test]
    public void 構造妥当だが未完の目視確認はsessionを渡してdotnet監査を実行し最終的に非0にする()
    {
        string source = GetTargetSource();
        int incomplete = source.IndexOf("$manualReviewIsIncomplete = $true", StringComparison.Ordinal);
        int dotnet = source.IndexOf("& dotnet @dotnetArguments", StringComparison.Ordinal);
        int forceFailure = source.IndexOf("if ($manualReviewIsIncomplete)", dotnet, StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Set-Item -Path \"Env:$sessionStartedLocalEnvironmentName\" -Value $manualReviewSessionStartedLocal"));
            Assert.That(incomplete, Is.GreaterThanOrEqualTo(0));
            Assert.That(dotnet, Is.GreaterThan(incomplete));
            Assert.That(forceFailure, Is.GreaterThan(dotnet));
            Assert.That(source, Does.Contain("$exitCode = 1"));
        });
    }

    private static string GetTargetSource()
    {
        string repoRoot = FindRepoRoot();
        string path = Path.Combine(repoRoot, "scripts", "Invoke-Phase0LiveAudit.ps1");
        Assert.That(File.Exists(path), Is.True, path);
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot([CallerFilePath] string callerFilePath = "")
    {
        // 呼び出し元から親へたどり、テスト実行場所に依存せずリポジトリを見つける。
        DirectoryInfo? current = new(Path.GetDirectoryName(callerFilePath) ?? Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IndigoMovieManager.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        Assert.Fail("repo rootを解決できませんでした。");
        return "";
    }
}
