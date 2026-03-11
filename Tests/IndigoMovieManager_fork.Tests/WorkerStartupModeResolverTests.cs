using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class WorkerStartupModeResolverTests
{
    [Test]
    public void ShouldRunDropUi_引数なしではtrueを返す()
    {
        Assert.That(WorkerStartupModeResolver.ShouldRunDropUi([]), Is.True);
    }

    [Test]
    public void ShouldRunDropUi_drop_manifestだけならtrueを返す()
    {
        string[] args = ["--drop-manifest", "sample.json"];

        Assert.That(WorkerStartupModeResolver.ShouldRunDropUi(args), Is.True);
    }

    [Test]
    public void ShouldRunDropUi_worker本線引数があればfalseを返す()
    {
        string[] args =
        [
            "--drop-manifest",
            "sample.json",
            "--role",
            "normal",
            "--main-db",
            "main.wb",
            "--owner",
            "owner-a",
            "--settings-snapshot",
            "settings.json",
        ];

        Assert.That(WorkerStartupModeResolver.ShouldRunDropUi(args), Is.False);
    }

    [Test]
    public void HasWorkerRuntimeArguments_大文字小文字差を吸収する()
    {
        string[] args = ["--ROLE", "idle", "--MAIN-DB", "main.wb"];

        Assert.That(WorkerStartupModeResolver.HasWorkerRuntimeArguments(args), Is.True);
    }
}
