using System.Runtime.CompilerServices;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class Phase0DiagnosticRunScriptPolicyTests
{
    [Test]
    public void launcherはコピー済みDBの明示承認を必須にする()
    {
        string source = GetTargetSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("[Parameter(Mandatory = $true)]"));
            Assert.That(source, Does.Contain("[string]$CopiedDbPath"));
            Assert.That(source, Does.Contain("[switch]$AcknowledgeCopiedDb"));
            Assert.That(source, Does.Contain("if (-not $AcknowledgeCopiedDb)"));
            Assert.That(source, Does.Contain("コピー済みDBだけを指定する"));
            Assert.That(source, Does.Contain("throw "));
        });
    }

    [Test]
    public void DBとRelease実行ファイルの存在形式検証を行う()
    {
        string source = GetTargetSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Test-Path -LiteralPath $CopiedDbPath -PathType Leaf"));
            Assert.That(source, Does.Contain("Resolve-Path -LiteralPath $CopiedDbPath"));
            Assert.That(source, Does.Contain("GetExtension($resolvedDbPath) -ine '.wb'"));
            Assert.That(source, Does.Contain("bin\\x64\\Release\\net8.0-windows10.0.19041.0\\IndigoMovieManager.exe"));
            Assert.That(source, Does.Contain("Test-Path -LiteralPath $requestedExecutablePath -PathType Leaf"));
            Assert.That(source, Does.Contain("Resolve-Path -LiteralPath $requestedExecutablePath"));
            Assert.That(source, Does.Contain("-ine 'IndigoMovieManager.exe'"));
        });
    }

    [Test]
    public void DB操作は読み取り検証に限定し自動コピーや削除をしない()
    {
        string source = GetTargetSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Not.Contain("Copy-Item"));
            Assert.That(source, Does.Not.Contain("Move-Item"));
            Assert.That(source, Does.Not.Contain("Remove-Item"));
            Assert.That(source, Does.Not.Contain("File.Copy"));
            Assert.That(source, Does.Not.Contain("File.Move"));
            Assert.That(source, Does.Not.Contain("File.Delete"));
            Assert.That(source, Does.Not.Contain("WriteAllText"));
        });
    }

    [Test]
    public void 診断環境変数はStartProcessの子プロセスへだけ渡す()
    {
        string source = GetTargetSource();
        int environmentStart = source.IndexOf("$childEnvironment", StringComparison.Ordinal);
        string environmentBlock = environmentStart >= 0
            ? source[environmentStart..]
            : string.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("INDIGO_DIAGNOSTIC_NO_PERSIST = '1'"));
            Assert.That(source, Does.Contain("INDIGO_DIAGNOSTIC_STARTUP_DB = $resolvedDbPath"));
            Assert.That(source, Does.Contain("INDIGO_RELEASE_LOG_MODE      = '1'"));
            Assert.That(source, Does.Contain("-Environment $childEnvironment"));
            Assert.That(source, Does.Not.Contain("$env:INDIGO_DIAGNOSTIC_NO_PERSIST"));
            Assert.That(source, Does.Not.Contain("$env:INDIGO_DIAGNOSTIC_STARTUP_DB"));
            Assert.That(environmentBlock, Does.Contain("$resolvedDbPath"));
        });
    }

    [Test]
    public void 対話用の可視起動とPID返却Wait選択を維持する()
    {
        string source = GetTargetSource();
        int startProcess = source.IndexOf("Start-Process", StringComparison.Ordinal);
        string startBlock = startProcess >= 0 ? source[startProcess..] : string.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("[switch]$Wait"));
            Assert.That(startBlock, Does.Contain("-PassThru"));
            Assert.That(startBlock, Does.Not.Contain("-WindowStyle Hidden"));
            Assert.That(source, Does.Contain("if ($Wait)"));
            Assert.That(source, Does.Contain("Wait-Process -Id $process.Id"));
            Assert.That(source, Does.Not.Contain("Stop-Process"));
            Assert.That(source, Does.Contain("Write-Output $process"));
        });
    }

    [Test]
    public void 起動後に主要8シナリオとlive監査案内を表示する()
    {
        string source = GetTargetSource();

        // 長期ロードマップの操作束を崩さず、同一runで採取する代表語彙を固定する。
        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("主要8シナリオ:"));
            Assert.That(
                source,
                Does.Contain(
                    "1. cold startから first-page shown、input ready、heavy services started まで。"
                )
            );
            Assert.That(
                source,
                Does.Contain("2. 検索入力中にsortを変更し、直後にscroll / PageUp / PageDownする。")
            );
            Assert.That(
                source,
                Does.Contain("3. 上側タブ、Logタブ、選択、ページを連続で切り替える。")
            );
            Assert.That(
                source,
                Does.Contain("4. 検索中にwatch 1件追加とrenameを発生させる。")
            );
            Assert.That(source, Does.Contain("5. Playerを開始、停止し、音量を変更する。"));
            Assert.That(
                source,
                Does.Contain("6. visible thumbnail、進捗、ERROR一覧、詳細画像を表示する。")
            );
            Assert.That(
                source,
                Does.Contain(
                    "7. コピーDB + no-persistでskin通常切り替えとHeader Reloadを行う。"
                )
            );
            Assert.That(
                source,
                Does.Contain("8. 設定変更、bookmark / score / tag保存後に終了する。")
            );
            Assert.That(source, Does.Contain("scripts/Invoke-Phase0LiveAudit.ps1"));
            Assert.That(source, Does.Contain("$process.Id"));
            Assert.That(source, Does.Contain("$resolvedDbPath"));
        });
    }

    [Test]
    public void ソース既定値にローカル絶対パスやユーザー名を埋め込まない()
    {
        string source = GetTargetSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Not.Contain("C:\\Users\\"));
            Assert.That(source, Does.Not.Contain("C:/Users/"));
            Assert.That(source, Does.Not.Contain("$env:USERNAME"));
            Assert.That(source, Does.Not.Contain("$env:USERPROFILE"));
        });
    }

    private static string GetTargetSource()
    {
        return File.ReadAllText(Path.Combine(FindRepoRoot(), "scripts", "Start-Phase0DiagnosticRun.ps1"));
    }

    private static string FindRepoRoot([CallerFilePath] string callerFilePath = "")
    {
        // 呼び出し元から親へたどり、テスト実行場所に依存しないrepo rootを探す。
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
