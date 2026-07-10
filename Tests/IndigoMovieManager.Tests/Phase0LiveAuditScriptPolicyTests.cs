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
            Assert.That(source, Does.Contain("Join-Path $env:LOCALAPPDATA 'IndigoMovieManager\\logs\\debug-runtime.log'"));
            Assert.That(source, Does.Contain("Join-Path $repoRoot 'Tests/IndigoMovieManager.Tests/IndigoMovieManager.Tests.csproj'"));
        });
    }

    [Test]
    public void ログは存在確認と解決だけを行い変更しない()
    {
        string source = GetTargetSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Test-Path -LiteralPath $LogPath -PathType Leaf"));
            Assert.That(source, Does.Contain("Resolve-Path -LiteralPath $LogPath"));
            Assert.That(source, Does.Not.Contain("Copy-Item"));
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
            Assert.That(source, Does.Contain("[Environment]::GetEnvironmentVariable($enabledEnvironmentName, 'Process')"));
            Assert.That(source, Does.Contain("[Environment]::GetEnvironmentVariable($pathEnvironmentName, 'Process')"));
            Assert.That(source, Does.Contain("try"));
            Assert.That(source, Does.Contain("finally"));
            Assert.That(source, Does.Contain("Set-Item -Path \"Env:$enabledEnvironmentName\" -Value '1'"));
            Assert.That(source, Does.Contain("Set-Item -Path \"Env:$pathEnvironmentName\" -Value $resolvedLogPath"));
            Assert.That(source, Does.Contain("Remove-Item \"Env:$enabledEnvironmentName\""));
            Assert.That(source, Does.Contain("Remove-Item \"Env:$pathEnvironmentName\""));
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
