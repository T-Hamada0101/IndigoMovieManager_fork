using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MainWindowSettingsPersistencePolicyTests
{
    [Test]
    public void MainWindow設定保存は背景直列キューへ寄せる()
    {
        string persistenceSource = GetRepoText("Views", "Main", "MainWindow.SettingsPersistence.cs");
        string dbSwitchSource = GetRepoText("Views", "Main", "MainWindow.DbSwitch.cs");
        string mainWindowSource = GetRepoText("Views", "Main", "MainWindow.xaml.cs");
        string fullscreenSource = GetRepoText(
            "UpperTabs",
            "Player",
            "MainWindow.UpperTabs.PlayerFullscreenWindow.cs"
        );

        Assert.That(persistenceSource, Does.Contain("private Task _applicationSettingsSaveTask = Task.CompletedTask;"));
        Assert.That(persistenceSource, Does.Contain("private void QueueApplicationSettingsSave(string reason)"));
        Assert.That(persistenceSource, Does.Contain("WaitForApplicationSettingsSaveForShutdown("));
        Assert.That(persistenceSource, Does.Contain("TaskScheduler.Default"));
        Assert.That(persistenceSource, Does.Contain("Properties.Settings.Default.Save();"));

        Assert.That(dbSwitchSource, Does.Contain("QueueApplicationSettingsSave(\"main-db-dialog-directory\")"));
        Assert.That(dbSwitchSource, Does.Contain("QueueApplicationSettingsSave(\"main-db-last-doc\")"));
        Assert.That(dbSwitchSource, Does.Not.Contain("Properties.Settings.Default.Save();"));

        Assert.That(mainWindowSource, Does.Contain("QueueApplicationSettingsSave(\"main-window-closing\")"));
        Assert.That(mainWindowSource, Does.Contain("WaitForPlayerVolumeSettingSaveForShutdown();"));
        Assert.That(mainWindowSource, Does.Contain("WaitForApplicationSettingsSaveForShutdown(\"main-window-closing\")"));
        Assert.That(fullscreenSource, Does.Contain("QueueApplicationSettingsSave(\"player-fullscreen-debug-enable\")"));
        Assert.That(fullscreenSource, Does.Contain("QueueApplicationSettingsSave(\"player-fullscreen-debug-restore\")"));
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
}
