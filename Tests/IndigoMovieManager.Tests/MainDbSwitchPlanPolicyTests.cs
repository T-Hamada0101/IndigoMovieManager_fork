namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MainDbSwitchPlanPolicyTests
{
    [Test]
    public void BuildSideEffectPlan_UI起点の別DB切替では必要な後処理を有効にする()
    {
        MainDbSwitchSideEffectPlan plan = MainDbSwitchPlanPolicy.BuildSideEffectPlan(
            MainWindow.MainDbSwitchSource.RecentMenu,
            hasCurrentDb: true,
            hasTargetDb: true,
            isDifferentDb: true
        );

        Assert.That(plan.ShouldCloseMainMenu, Is.True);
        Assert.That(plan.ShouldPersistCurrentDbViewState, Is.True);
        Assert.That(plan.ShouldUpdateRecentFiles, Is.True);
        Assert.That(plan.ShouldRememberLastDoc, Is.True);
        Assert.That(plan.ShouldDiscardPreviousDbPendingThumbnailQueueItems, Is.True);
    }

    [Test]
    public void BuildSideEffectPlan_起動自動オープンではUI保存とRecent更新を抑える()
    {
        MainDbSwitchSideEffectPlan plan = MainDbSwitchPlanPolicy.BuildSideEffectPlan(
            MainWindow.MainDbSwitchSource.StartupAutoOpen,
            hasCurrentDb: true,
            hasTargetDb: true,
            isDifferentDb: true
        );

        Assert.That(plan.ShouldCloseMainMenu, Is.False);
        Assert.That(plan.ShouldPersistCurrentDbViewState, Is.False);
        Assert.That(plan.ShouldUpdateRecentFiles, Is.False);
        Assert.That(plan.ShouldRememberLastDoc, Is.False);
        Assert.That(plan.ShouldDiscardPreviousDbPendingThumbnailQueueItems, Is.True);
    }

    [Test]
    public void BuildSideEffectPlan_同一DBまたは空DBでは保存と旧Queue掃除を行わない()
    {
        MainDbSwitchSideEffectPlan sameDbPlan = MainDbSwitchPlanPolicy.BuildSideEffectPlan(
            MainWindow.MainDbSwitchSource.OpenDialog,
            hasCurrentDb: true,
            hasTargetDb: true,
            isDifferentDb: false
        );
        MainDbSwitchSideEffectPlan noCurrentPlan = MainDbSwitchPlanPolicy.BuildSideEffectPlan(
            MainWindow.MainDbSwitchSource.OpenDialog,
            hasCurrentDb: false,
            hasTargetDb: true,
            isDifferentDb: true
        );

        Assert.That(sameDbPlan.ShouldPersistCurrentDbViewState, Is.False);
        Assert.That(sameDbPlan.ShouldDiscardPreviousDbPendingThumbnailQueueItems, Is.False);
        Assert.That(noCurrentPlan.ShouldPersistCurrentDbViewState, Is.False);
        Assert.That(noCurrentPlan.ShouldDiscardPreviousDbPendingThumbnailQueueItems, Is.False);
    }

    [Test]
    public void MainDbSwitchPlanPolicy_WPFやDB読込へ依存しない()
    {
        string source = File.ReadAllText(
            Path.Combine(GetRepoRoot().FullName, "Views", "Main", "MainDbSwitchPlanPolicy.cs")
        );

        Assert.That(source, Does.Not.Contain("System.Windows"));
        Assert.That(source, Does.Not.Contain("Dispatcher"));
        Assert.That(source, Does.Not.Contain("DataTable"));
        Assert.That(source, Does.Not.Contain("File."));
        Assert.That(source, Does.Not.Contain("Directory."));
        Assert.That(source, Does.Not.Contain("LoadSystemTable"));
        Assert.That(source, Does.Not.Contain("OpenDatafile"));
        Assert.That(source, Does.Not.Contain("QueueDbService"));
        Assert.That(source, Does.Not.Contain("Properties.Settings"));
    }

    private static DirectoryInfo GetRepoRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string sourcePath = ""
    )
    {
        DirectoryInfo? directory = new(Path.GetDirectoryName(sourcePath)!);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
