using System.Runtime.CompilerServices;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ReleaseVersionConsistencyPolicyTests
{
    [Test]
    public void 正式ラベルとproject三種と配布EXEを同じ版数へ固定する()
    {
        string source = ReadRepoFile("scripts", "assert_release_version_consistency.ps1");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("^v(.+)$"));
            Assert.That(source, Does.Contain("Version,FileVersion,AssemblyVersion"));
            Assert.That(source, Does.Contain("project の Version / FileVersion / AssemblyVersion が一致していません"));
            Assert.That(source, Does.Contain("project と配布EXEの版数が一致していません"));
            Assert.That(source, Does.Contain("正式リリースラベルと実体の版数が一致していません"));
        });
    }

    [Test]
    public void ZIP作成時はprojectと配布EXEと成果物ラベルを照合する()
    {
        string source = ReadRepoFile("scripts", "create_github_release_package.ps1");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("assert_release_version_consistency.ps1"));
            Assert.That(source, Does.Contain("-ProjectPath $projectFullPath"));
            Assert.That(source, Does.Contain("-MainExePath $mainExePath"));
            Assert.That(source, Does.Contain("-VersionLabel $versionLabelNormalized"));
        });
    }

    [Test]
    public void Installer作成時は包むEXEとSetup名の版数を照合する()
    {
        string source = ReadRepoFile("scripts", "create_wix_installer_from_release_package.ps1");
        int consistencyCheck = source.IndexOf("assert_release_version_consistency.ps1", StringComparison.Ordinal);
        int runtimeMetadata = source.LastIndexOf("Get-DotNetDesktopRuntimeMetadata -MajorVersion", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("-MainExePath $mainExePath"));
            Assert.That(source, Does.Contain("-VersionLabel $VersionLabel"));
            Assert.That(consistencyCheck, Is.GreaterThanOrEqualTo(0));
            Assert.That(runtimeMetadata, Is.GreaterThan(consistencyCheck));
        });
    }

    [Test]
    public void Tag実行はPrivate成果物同期より前にproject版数を照合する()
    {
        string source = ReadRepoFile(".github", "workflows", "github-release-package.yml");
        int validation = source.IndexOf("Validate release version consistency", StringComparison.Ordinal);
        int privateSync = source.IndexOf("Try sync private engine publish source", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("if: github.ref_type == 'tag'"));
            Assert.That(source, Does.Contain("-ProjectPath ./IndigoMovieManager.csproj"));
            Assert.That(source, Does.Contain("-VersionLabel $env:GITHUB_REF_NAME_VALUE"));
            Assert.That(validation, Is.GreaterThanOrEqualTo(0));
            Assert.That(privateSync, Is.GreaterThan(validation));
        });
    }

    [Test]
    public void 公開ミラー同期ではPrivateTokenを送らない()
    {
        string source = ReadRepoFile(".github", "workflows", "github-release-package.yml");
        string workerSync = ReadRepoFile("scripts", "sync_private_engine_worker_artifact.ps1");
        string packageSync = ReadRepoFile("scripts", "sync_private_engine_packages.ps1");
        int guardedTokenCount = source.Split(
            "if ($needsToken)",
            StringSplitOptions.None
        ).Length - 1;

        Assert.Multiple(() =>
        {
            Assert.That(guardedTokenCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(source, Does.Contain("公開ミラーへ期限切れPrivate tokenを送ると"));
            Assert.That(source, Does.Contain("公開ミラーは匿名API"));
            Assert.That(
                source.Split("-AnonymousGitHub", StringSplitOptions.None).Length - 1,
                Is.GreaterThanOrEqualTo(2)
            );
            Assert.That(workerSync, Does.Contain("[switch]$AnonymousGitHub"));
            Assert.That(
                workerSync.IndexOf("if ($AnonymousGitHub)", StringComparison.Ordinal),
                Is.LessThan(
                    workerSync.IndexOf("$env:IMM_PRIVATE_ENGINE_TOKEN", StringComparison.Ordinal)
                )
            );
            Assert.That(packageSync, Does.Contain("[switch]$AnonymousGitHub"));
            Assert.That(
                packageSync.IndexOf("if ($AnonymousGitHub)", StringComparison.Ordinal),
                Is.LessThan(
                    packageSync.IndexOf("$env:IMM_PRIVATE_ENGINE_TOKEN", StringComparison.Ordinal)
                )
            );
        });
    }

    private static string ReadRepoFile(params string[] parts)
    {
        return File.ReadAllText(Path.Combine([FindRepoRoot(), .. parts]));
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
