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
        Assert.That(
            persistenceSource,
            Does.Contain(
                "PersistenceFailureNotificationPolicy.BuildLogFields(PersistenceFailureKind.ApplicationSettings)"
            )
        );
        Assert.That(playerSource, Does.Contain("App.IsDiagnosticNoPersistEnabled()"));
        Assert.That(
            playerSource,
            Does.Contain(
                "PersistenceFailureNotificationPolicy.BuildLogFields(PersistenceFailureKind.ApplicationSettings)"
            )
        );
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

    [Test]
    public void UI操作hot_pathは同期保存や直接DB更新へ戻さない()
    {
        string inputRoutingSource = GetRepoText("Views", "Main", "MainWindow.InputRouting.cs");
        string playerTabSource = GetRepoText(
            "UpperTabs",
            "Player",
            "MainWindow.UpperTabs.PlayerTab.cs"
        );
        string playerSource = GetRepoText("Views", "Main", "MainWindow.Player.cs");
        string menuSource = GetRepoText("Views", "Main", "MainWindow.MenuActions.cs");
        string tagSource = GetRepoText("Views", "Main", "MainWindow.Tag.cs");
        string detailThumbnailSource = GetRepoText(
            "BottomTabs",
            "Extension",
            "MainWindow.BottomTab.Extension.DetailThumbnail.cs"
        );

        Assert.That(inputRoutingSource, Does.Not.Contain("Properties.Settings.Default.Save();"));
        Assert.That(playerTabSource, Does.Not.Contain("Properties.Settings.Default.Save();"));
        Assert.That(detailThumbnailSource, Does.Not.Contain("Properties.Settings.Default.Save();"));

        string scoreClickMethod = ExtractMethod(menuSource, "private void MenuScore_Click(");
        string scorePersistMethod = ExtractMethod(menuSource, "private void QueueMovieScorePersist(");
        string tagPasteMethod = ExtractMethod(tagSource, "private void TagPaste_Click(");
        string tagAddMethod = ExtractMethod(tagSource, "private void ApplyTagsToRecords(");
        string tagPersistMethod = ExtractMethod(tagSource, "internal void QueueMovieTagPersist(");
        string fileMoveCompleteMethod = ExtractMethod(
            menuSource,
            "private async Task CompleteMovieFileMoveOnUiAsync("
        );
        string reflectMovedMovieMethod = ExtractMethod(
            menuSource,
            "private int ReflectMovedMovieRecordsOnUi("
        );
        string moviePathPersistMethod = ExtractMethod(
            menuSource,
            "private void QueueMoviePathPersist("
        );
        string playMovieMethod = ExtractMethod(playerSource, "public async void PlayMovie_Click(");
        string playbackStatsPersistMethod = ExtractMethod(
            playerSource,
            "private void QueueMoviePlaybackStatsPersist("
        );

        Assert.That(scoreClickMethod, Does.Contain("QueueMovieScorePersist("));
        Assert.That(scoreClickMethod, Does.Not.Contain("_mainDbMovieMutationFacade.UpdateScore("));
        Assert.That(scoreClickMethod, Does.Not.Contain("ExecuteNonQuery("));
        Assert.That(scorePersistMethod, Does.Contain("_mainDbMovieMutationFacade.UpdateScore("));
        Assert.That(
            scorePersistMethod,
            Does.Contain(
                "PersistenceFailureNotificationPolicy.BuildLogFields(PersistenceFailureKind.BackgroundDbWrite)"
            )
        );

        Assert.That(tagPasteMethod, Does.Contain("QueueMovieTagPersist("));
        Assert.That(tagPasteMethod, Does.Not.Contain("_mainDbMovieMutationFacade.UpdateTag("));
        Assert.That(tagPasteMethod, Does.Not.Contain("ExecuteNonQuery("));
        Assert.That(tagAddMethod, Does.Contain("QueueMovieTagPersist("));
        Assert.That(tagAddMethod, Does.Not.Contain("_mainDbMovieMutationFacade.UpdateTag("));
        Assert.That(tagAddMethod, Does.Not.Contain("ExecuteNonQuery("));
        Assert.That(tagPersistMethod, Does.Contain("_mainDbMovieMutationFacade.UpdateTag("));
        Assert.That(
            tagPersistMethod,
            Does.Contain(
                "PersistenceFailureNotificationPolicy.BuildLogFields(PersistenceFailureKind.BackgroundDbWrite)"
            )
        );

        Assert.That(fileMoveCompleteMethod, Does.Contain("QueueMoviePathPersist("));
        Assert.That(fileMoveCompleteMethod, Does.Contain("ReflectMovedMovieRecordsOnUi("));
        Assert.That(fileMoveCompleteMethod, Does.Not.Contain("_mainDbMovieMutationFacade.UpdateMoviePath("));
        Assert.That(fileMoveCompleteMethod, Does.Not.Contain("ExecuteNonQuery("));
        Assert.That(reflectMovedMovieMethod, Does.Contain("record.Movie_Path = movedSnapshot.DestinationPath;"));
        Assert.That(reflectMovedMovieMethod, Does.Not.Contain("_mainDbMovieMutationFacade.UpdateMoviePath("));
        Assert.That(moviePathPersistMethod, Does.Contain("Task.Run("));
        Assert.That(moviePathPersistMethod, Does.Contain("_mainDbMovieMutationFacade.UpdateMoviePath("));
        Assert.That(
            moviePathPersistMethod,
            Does.Contain(
                "PersistenceFailureNotificationPolicy.BuildLogFields(PersistenceFailureKind.BackgroundDbWrite)"
            )
        );

        int viewCountDisplayIndex = playMovieMethod.IndexOf(
            "mv.View_Count += 1;",
            StringComparison.Ordinal
        );
        int viewCountPersistIndex = playMovieMethod.IndexOf(
            "QueueMoviePlaybackStatsPersist(",
            StringComparison.Ordinal
        );

        Assert.That(playMovieMethod, Does.Contain("mv.View_Count += 1;"));
        Assert.That(playMovieMethod, Does.Contain("mv.Last_Date = result.ToString("));
        Assert.That(viewCountPersistIndex, Is.GreaterThan(viewCountDisplayIndex));
        Assert.That(playMovieMethod, Does.Not.Contain("_mainDbMovieMutationFacade.UpdateViewCount("));
        Assert.That(playMovieMethod, Does.Not.Contain("_mainDbMovieMutationFacade.UpdateLastDate("));
        Assert.That(playMovieMethod, Does.Not.Contain("ExecuteNonQuery("));
        Assert.That(playbackStatsPersistMethod, Does.Contain("Task.Run("));
        Assert.That(
            playbackStatsPersistMethod,
            Does.Contain("_mainDbMovieMutationFacade.UpdateViewCount(")
        );
        Assert.That(
            playbackStatsPersistMethod,
            Does.Contain("_mainDbMovieMutationFacade.UpdateLastDate(")
        );
        Assert.That(
            playbackStatsPersistMethod,
            Does.Contain(
                "PersistenceFailureNotificationPolicy.BuildLogFields(PersistenceFailureKind.BackgroundDbWrite)"
            )
        );
    }

    [Test]
    public void PersistenceFailureNotificationPolicy_保存失敗の軽量状態と通知条件を共通化する()
    {
        PersistenceFailureNotificationState retryableState =
            PersistenceFailureNotificationPolicy.BuildFailureState(
                PersistenceFailureKind.BackgroundDbWrite
            );
        PersistenceFailureNotificationState systemState =
            PersistenceFailureNotificationPolicy.BuildFailureState(
                PersistenceFailureKind.SkinSystem
            );

        Assert.Multiple(() =>
        {
            Assert.That(retryableState.Dirty, Is.True);
            Assert.That(retryableState.Failed, Is.True);
            Assert.That(retryableState.Retryable, Is.True);
            Assert.That(retryableState.NotifyUi, Is.False);
            Assert.That(
                PersistenceFailureNotificationPolicy.BuildLogFields(retryableState),
                Is.EqualTo("dirty=true failed=true retryable=true notify_ui=false")
            );

            Assert.That(systemState.Dirty, Is.False);
            Assert.That(systemState.Failed, Is.True);
            Assert.That(systemState.Retryable, Is.False);
            Assert.That(systemState.NotifyUi, Is.True);
            Assert.That(
                PersistenceFailureNotificationPolicy.BuildLogFields(systemState),
                Is.EqualTo("dirty=false failed=true retryable=false notify_ui=true")
            );
        });
    }

    [Test]
    public void 外部SkinProfileWrite_hot_pathはenqueueだけに保つ()
    {
        string apiSource = GetRepoText("Views", "Main", "MainWindow.WebViewSkin.Api.cs");
        string persistenceSource = GetRepoText("WhiteBrowserSkin", "MainWindow.SkinPersistence.cs");
        string writeMethod = ExtractMethod(
            apiSource,
            "private async Task<bool> WriteExternalSkinProfileValueAsync("
        );
        string enqueueMethod = ExtractMethod(
            persistenceSource,
            "private bool TryEnqueueExternalSkinProfileWrite("
        );
        string queueMethod = ExtractMethod(
            persistenceSource,
            "private bool TryEnqueueWhiteBrowserSkinStatePersistRequest("
        );

        Assert.Multiple(() =>
        {
            Assert.That(writeMethod, Does.Contain("TryEnqueueExternalSkinProfileWrite("));
            Assert.That(writeMethod, Does.Not.Contain("UpsertProfileTable("));
            Assert.That(writeMethod, Does.Not.Contain("TryUpsertProfileTable("));
            Assert.That(writeMethod, Does.Not.Contain("Task.Run("));

            Assert.That(enqueueMethod, Does.Contain("WhiteBrowserSkinStatePersistRequest.CreateProfile("));
            Assert.That(enqueueMethod, Does.Not.Contain("UpsertProfileTable("));
            Assert.That(enqueueMethod, Does.Not.Contain("TryUpsertProfileTable("));

            Assert.That(queueMethod, Does.Contain("WhiteBrowserSkinProfileValueCache.RecordPending("));
            Assert.That(queueMethod, Does.Contain("RecordProfilePersistFaultForCache(request);"));
            Assert.That(queueMethod, Does.Contain("BuildFailureStateLogFields()"));
        });
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

    private static string ExtractMethod(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{signature} が見つかりません。");

        int bodyStart = source.IndexOf('{', start);
        Assert.That(bodyStart, Is.GreaterThanOrEqualTo(0), $"{signature} の本文開始が見つかりません。");

        int depth = 0;
        for (int i = bodyStart; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(start, i - start + 1);
                }
            }
        }

        Assert.Fail($"{signature} の本文終端が見つかりません。");
        return "";
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
