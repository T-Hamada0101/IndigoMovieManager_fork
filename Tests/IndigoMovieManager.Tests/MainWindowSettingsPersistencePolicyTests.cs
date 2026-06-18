using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MainWindowSettingsPersistencePolicyTests
{
    [Test]
    public void MainWindow設定保存は背景直列キューへ寄せる()
    {
        string persistenceSource = GetRepoText("Views", "Main", "MainWindow.SettingsPersistence.cs");
        string dbSwitchSource = GetRepoText("Views", "Main", "MainWindow.DbSwitch.cs");
        string lifecycleSource = GetRepoText("Views", "Main", "MainWindow.Lifecycle.cs");
        string playerSource = GetRepoText("Views", "Main", "MainWindow.Player.cs");
        string settingsWindowSource = GetRepoText(
            "Views",
            "Settings",
            "CommonSettingsWindow.xaml.cs"
        );
        string appSource = GetRepoText("App.xaml.cs");
        string fullscreenSource = GetRepoText(
            "UpperTabs",
            "Player",
            "MainWindow.UpperTabs.PlayerFullscreenWindow.cs"
        );
        string detailThumbnailSource = GetRepoText(
            "BottomTabs",
            "Extension",
            "MainWindow.BottomTab.Extension.DetailThumbnail.cs"
        );
        string logTabSource = GetRepoText(
            "BottomTabs",
            "LogTab",
            "MainWindow.BottomTab.Log.cs"
        );

        Assert.That(persistenceSource, Does.Contain("private Task _applicationSettingsSaveTask = Task.CompletedTask;"));
        Assert.That(persistenceSource, Does.Contain("private void QueueApplicationSettingsSave(string reason)"));
        Assert.That(persistenceSource, Does.Contain("WaitForApplicationSettingsSaveForShutdown("));
        Assert.That(persistenceSource, Does.Contain("TaskScheduler.Default"));
        Assert.That(persistenceSource, Does.Contain("Properties.Settings.Default.Save();"));
        Assert.That(persistenceSource, Does.Contain("App.IsDiagnosticNoPersistEnabled()"));
        Assert.That(playerSource, Does.Contain("App.IsDiagnosticNoPersistEnabled()"));
        Assert.That(settingsWindowSource, Does.Contain("App.IsDiagnosticNoPersistEnabled()"));
        Assert.That(appSource, Does.Contain("internal const string DiagnosticNoPersistEnvironmentVariable"));
        Assert.That(appSource, Does.Contain("internal static bool IsDiagnosticNoPersistEnabled()"));

        Assert.That(dbSwitchSource, Does.Contain("QueueApplicationSettingsSave(\"main-db-dialog-directory\")"));
        Assert.That(dbSwitchSource, Does.Contain("QueueApplicationSettingsSave(\"main-db-last-doc\")"));
        Assert.That(dbSwitchSource, Does.Not.Contain("Properties.Settings.Default.Save();"));

        Assert.That(lifecycleSource, Does.Contain("QueueApplicationSettingsSave(\"main-window-closing\")"));
        Assert.That(lifecycleSource, Does.Contain("WaitForPlayerVolumeSettingSaveForShutdown();"));
        Assert.That(lifecycleSource, Does.Contain("WaitForApplicationSettingsSaveForShutdown(\"main-window-closing\")"));
        Assert.That(fullscreenSource, Does.Contain("QueueApplicationSettingsSave(\"player-fullscreen-debug-enable\")"));
        Assert.That(fullscreenSource, Does.Contain("QueueApplicationSettingsSave(\"player-fullscreen-debug-restore\")"));
        Assert.That(detailThumbnailSource, Does.Contain("QueueApplicationSettingsSave(\"extension-detail-thumbnail-mode\")"));
        Assert.That(detailThumbnailSource, Does.Not.Contain("Properties.Settings.Default.Save();"));
        Assert.That(logTabSource, Does.Contain("QueueApplicationSettingsSave(\"log-tab-debug-switch\")"));
        Assert.That(logTabSource, Does.Not.Contain("Properties.Settings.Default.Save();"));
    }

    private static string GetRepoText(params string[] relativePathParts)
    {
        foreach (DirectoryInfo searchRoot in EnumerateRepoSearchRoots())
        {
            DirectoryInfo? current = searchRoot;
            while (current != null)
            {
                string candidate = Path.Combine([current.FullName, .. relativePathParts]);
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }

                current = current.Parent;
            }
        }

        Assert.Fail($"{Path.Combine(relativePathParts)} の位置を repo root から解決できませんでした。");
        return string.Empty;
    }

    private static IEnumerable<DirectoryInfo> EnumerateRepoSearchRoots(
        [CallerFilePath] string callerFilePath = ""
    )
    {
        string? callerDirectory = Path.GetDirectoryName(callerFilePath);
        if (!string.IsNullOrWhiteSpace(callerDirectory))
        {
            yield return new DirectoryInfo(callerDirectory);
        }

        yield return new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        yield return new DirectoryInfo(TestContext.CurrentContext.WorkDirectory);
        yield return new DirectoryInfo(Directory.GetCurrentDirectory());
    }
}
