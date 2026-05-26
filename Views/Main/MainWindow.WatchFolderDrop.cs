using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using IndigoMovieManager.DB;
using Notification.Wpf;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const string MainWindowDropToastAreaName = "ProgressArea";
        private readonly NotificationManager _watchFolderDropNotificationManager = new();

        internal enum DroppedMainDbSwitchToastKind
        {
            Switched = 0,
            AlreadyOpen = 1,
            Failed = 2,
        }

        // DragOverでは存在確認を避け、フォルダらしいパスと .wb 候補だけで受け付けを決める。
        internal static bool CanAcceptWatchFolderDrop(
            string dbFullPath,
            IEnumerable<string> droppedPaths
        )
        {
            string[] paths = [.. droppedPaths ?? []];
            return HasPotentialWatchFolderDropPath(paths)
                || !string.IsNullOrWhiteSpace(ResolveDroppedMainDbPath(paths));
        }

        private void MainWindow_PreviewDragOver(object sender, DragEventArgs e)
        {
            // 画面へ直接フォルダを落とした時だけ、監視フォルダ追加導線を有効にする。
            string[] droppedPaths = GetWatchFolderDroppedPaths(e.Data);
            e.Effects = CanAcceptWatchFolderDrop(MainVM?.DbInfo?.DBFullPath, droppedPaths)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private async void MainWindow_Drop(object sender, DragEventArgs e)
        {
            // .wb はDB切替、フォルダは watch テーブルへ直接追加として扱い、混在時は先にDBを切り替える。
            string[] droppedPaths = GetWatchFolderDroppedPaths(e.Data);
            if (!CanAcceptWatchFolderDrop(MainVM?.DbInfo?.DBFullPath, droppedPaths))
            {
                e.Handled = true;
                return;
            }

            e.Handled = true;
            string dbFullPathAtDrop = MainVM?.DbInfo?.DBFullPath ?? "";
            string droppedMainDbCandidatePath = ResolveDroppedMainDbPath(droppedPaths);
            if (!string.IsNullOrWhiteSpace(droppedMainDbCandidatePath))
            {
                string droppedMainDbPath = await ResolveExistingDroppedMainDbPathAsync(droppedPaths);
                if (!CanContinueDroppedMainDbSwitch(dbFullPathAtDrop))
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(droppedMainDbPath))
                {
                    if (!WatchFolderDropRegistrationPolicy.CanAccept(droppedPaths))
                    {
                        return;
                    }
                }
                else
                {
                    if (!HandleDroppedMainDbSwitch(droppedMainDbPath))
                    {
                        return;
                    }
                }
            }

            if (!WatchFolderDropRegistrationPolicy.CanAccept(droppedPaths))
            {
                return;
            }

            if (!EnsureMainDbReadyForWatchFolderDrop())
            {
                return;
            }

            QueueDroppedWatchFolders(droppedPaths);
        }

        // 新規開始では、最初のフォルダドロップからそのままDB作成へ進める。
        private bool EnsureMainDbReadyForWatchFolderDrop()
        {
            if (!string.IsNullOrWhiteSpace(MainVM?.DbInfo?.DBFullPath))
            {
                return true;
            }

            return TryCreateMainDbFromDialog();
        }

        // 監視フォルダ編集ダイアログを開く入口を1か所へ寄せ、メニュー起動とドロップ起動を揃える。
        private void OpenWatchFolderEditorDialog(IEnumerable<string> initialDroppedPaths = null)
        {
            if (string.IsNullOrWhiteSpace(MainVM?.DbInfo?.DBFullPath))
            {
                MessageBox.Show(
                    "管理ファイルが選択されていません。",
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Exclamation
                );
                return;
            }

            MenuToggleButton.IsChecked = false;
            var watchWindow = new WatchWindow(MainVM.DbInfo.DBFullPath, initialDroppedPaths)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            watchWindow.ShowDialog();

            // 監視フォルダ編集を閉じたら、次回 poll で watch 一覧を取り直す。
            InvalidateEverythingWatchPollWatchFolderSnapshot();
        }

        // Explorer からの file drop を安全に取り出し、判定ロジック側へ渡す。
        private static string[] GetWatchFolderDroppedPaths(IDataObject dataObject)
        {
            if (dataObject == null || !dataObject.GetDataPresent(DataFormats.FileDrop))
            {
                return [];
            }

            return dataObject.GetData(DataFormats.FileDrop) as string[] ?? [];
        }

        // DragOverではI/Oを掘らず、文字列として .wb 候補かどうかだけを拾う。
        internal static string ResolveDroppedMainDbPath(IEnumerable<string> droppedPaths)
        {
            foreach (string normalizedPath in EnumerateDroppedMainDbCandidatePaths(droppedPaths))
            {
                return normalizedPath;
            }

            return "";
        }

        // Drop確定後だけ、存在確認を背景で行い、UIスレッドをネットワークパス待ちに巻き込まない。
        internal static Task<string> ResolveExistingDroppedMainDbPathAsync(
            IEnumerable<string> droppedPaths
        )
        {
            string[] droppedPathSnapshot = [.. droppedPaths ?? []];
            return Task.Run(() =>
            {
                foreach (string normalizedPath in EnumerateDroppedMainDbCandidatePaths(droppedPathSnapshot))
                {
                    if (File.Exists(normalizedPath))
                    {
                        return normalizedPath;
                    }
                }

                return "";
            });
        }

        private static IEnumerable<string> EnumerateDroppedMainDbCandidatePaths(
            IEnumerable<string> droppedPaths
        )
        {
            foreach (string droppedPath in droppedPaths ?? [])
            {
                string normalizedPath = NormalizeDroppedMainDbPath(droppedPath);
                if (string.IsNullOrWhiteSpace(normalizedPath))
                {
                    continue;
                }

                if (
                    !string.Equals(
                        Path.GetExtension(normalizedPath),
                        ".wb",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    continue;
                }

                yield return normalizedPath;
            }
        }

        // 監視フォルダのDragOverは、拡張子つきファイルらしいものだけ弾く軽量判定に留める。
        private static bool HasPotentialWatchFolderDropPath(IEnumerable<string> droppedPaths)
        {
            foreach (string droppedPath in droppedPaths ?? [])
            {
                string normalizedPath = NormalizeDroppedMainDbPath(droppedPath);
                if (string.IsNullOrWhiteSpace(normalizedPath))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(Path.GetExtension(normalizedPath)))
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanContinueDroppedMainDbSwitch(string dbFullPathAtDrop)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return false;
            }

            string currentDbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (
                string.IsNullOrWhiteSpace(dbFullPathAtDrop)
                && string.IsNullOrWhiteSpace(currentDbFullPath)
            )
            {
                return true;
            }

            return AreSameMainDbPath(dbFullPathAtDrop, currentDbFullPath);
        }

        // .wb ドロップ時は同一DBの再オープンを避け、結果をトーストで返す。
        private bool HandleDroppedMainDbSwitch(string droppedMainDbPath)
        {
            if (string.IsNullOrWhiteSpace(droppedMainDbPath))
            {
                return true;
            }

            if (AreSameMainDbPath(MainVM?.DbInfo?.DBFullPath, droppedMainDbPath))
            {
                ShowDroppedMainDbSwitchToast(droppedMainDbPath, DroppedMainDbSwitchToastKind.AlreadyOpen);
                return true;
            }

            bool switched = TrySwitchMainDb(droppedMainDbPath, MainDbSwitchSource.DragDrop);
            ShowDroppedMainDbSwitchToast(
                droppedMainDbPath,
                switched
                    ? DroppedMainDbSwitchToastKind.Switched
                    : DroppedMainDbSwitchToastKind.Failed
            );
            return switched;
        }

        // DBドロップ結果を短いトースト文言へ変換する。
        internal static (string Title, string Message, NotificationType Type) BuildDroppedMainDbSwitchToast(
            string droppedMainDbPath,
            DroppedMainDbSwitchToastKind kind
        )
        {
            string fileName = Path.GetFileName(droppedMainDbPath);
            string displayName = string.IsNullOrWhiteSpace(fileName) ? droppedMainDbPath : fileName;

            return kind switch
            {
                DroppedMainDbSwitchToastKind.AlreadyOpen => (
                    "DB切替",
                    $"既に開いています: {displayName}",
                    NotificationType.Information
                ),
                DroppedMainDbSwitchToastKind.Failed => (
                    "DB切替",
                    $"DBを開けませんでした: {displayName}",
                    NotificationType.Error
                ),
                _ => (
                    "DB切替",
                    $"DBを切り替えました: {displayName}",
                    NotificationType.Success
                ),
            };
        }

        private void ShowDroppedMainDbSwitchToast(
            string droppedMainDbPath,
            DroppedMainDbSwitchToastKind kind
        )
        {
            try
            {
                (string title, string message, NotificationType type) =
                    BuildDroppedMainDbSwitchToast(droppedMainDbPath, kind);
                _watchFolderDropNotificationManager.Show(
                    title,
                    message,
                    type,
                    MainWindowDropToastAreaName,
                    TimeSpan.FromSeconds(4)
                );
            }
            catch
            {
                // トースト失敗でDB切替結果は変えない。
            }
        }

        // メイン画面のフォルダドロップは、DB I/Oを背景化してUIには結果反映だけ戻す。
        private void QueueDroppedWatchFolders(IEnumerable<string> droppedPaths)
        {
            string dbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            string[] droppedPathSnapshot = [.. droppedPaths ?? []];
            if (string.IsNullOrWhiteSpace(dbFullPath) || droppedPathSnapshot.Length < 1)
            {
                return;
            }

            _ = QueueDroppedWatchFoldersAsync(dbFullPath, droppedPathSnapshot);
        }

        private async Task QueueDroppedWatchFoldersAsync(
            string dbFullPath,
            string[] droppedPathSnapshot
        )
        {
            DroppedWatchFolderApplyResult applyResult;
            try
            {
                applyResult = await Task.Run(
                        () => ApplyDroppedWatchFoldersInBackground(dbFullPath, droppedPathSnapshot)
                    )
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch-drop",
                    $"watch folder drop failed: db='{dbFullPath}' err='{ex.GetBaseException().Message}'"
                );
                return;
            }

            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            try
            {
                await Dispatcher.InvokeAsync(
                    () => ApplyDroppedWatchFoldersOnUi(dbFullPath, applyResult),
                    DispatcherPriority.Background
                )
                .Task;
            }
            catch (InvalidOperationException) when (
                Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished
            )
            {
            }
            catch (TaskCanceledException) when (
                Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished
            )
            {
            }
        }

        private void ApplyDroppedWatchFoldersOnUi(
            string dbFullPath,
            DroppedWatchFolderApplyResult applyResult
        )
        {
            if (
                applyResult == null
                || !AreSameMainDbPath(dbFullPath, MainVM?.DbInfo?.DBFullPath ?? "")
            )
            {
                return;
            }

            watchData = applyResult.RefreshedWatchData;
            if (applyResult.Result.DirectoriesToAdd.Count > 0)
            {
                // 直接追加した監視フォルダを次回pollへ反映するため、キャッシュを捨てる。
                InvalidateEverythingWatchPollWatchFolderSnapshot();
            }

            ShowDroppedWatchFolderToast(applyResult.Result);
        }

        private static DroppedWatchFolderApplyResult ApplyDroppedWatchFoldersInBackground(
            string dbFullPath,
            IEnumerable<string> droppedPaths
        )
        {
            const string watchTableSql = "SELECT * FROM watch";
            DataTable currentWatchData = SQLite.GetData(dbFullPath, watchTableSql);
            WatchTableRowNormalizer.Normalize(currentWatchData);
            WatchFolderDropResult result = WatchFolderDropRegistrationPolicy.Build(
                droppedPaths,
                EnumerateWatchDirectories(currentWatchData)
            );

            foreach (string directoryPath in result.DirectoriesToAdd)
            {
                SQLite.InsertWatchTable(
                    dbFullPath,
                    new WatchRecords
                    {
                        Auto = true,
                        Watch = true,
                        Sub = true,
                        Dir = directoryPath,
                    }
                );
            }

            DataTable refreshedWatchData = SQLite.GetData(dbFullPath, watchTableSql);
            WatchTableRowNormalizer.Normalize(refreshedWatchData);
            return new DroppedWatchFolderApplyResult(result, refreshedWatchData);
        }

        private static IEnumerable<string> EnumerateWatchDirectories(DataTable sourceWatchData)
        {
            if (sourceWatchData == null || sourceWatchData.Rows.Count == 0)
            {
                return [];
            }

            List<string> directories = [];
            foreach (DataRow row in sourceWatchData.Rows)
            {
                string directoryPath = row["dir"]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(directoryPath))
                {
                    directories.Add(directoryPath);
                }
            }

            return directories;
        }

        private sealed record DroppedWatchFolderApplyResult(
            WatchFolderDropResult Result,
            DataTable RefreshedWatchData
        );

        private void ShowDroppedWatchFolderToast(WatchFolderDropResult result)
        {
            try
            {
                (string title, string message, NotificationType type) =
                    WatchWindow.BuildDropSummaryToast(result);
                if (string.IsNullOrWhiteSpace(message))
                {
                    return;
                }

                _watchFolderDropNotificationManager.Show(
                    title,
                    message,
                    type,
                    MainWindowDropToastAreaName,
                    TimeSpan.FromSeconds(4)
                );
            }
            catch
            {
                // トースト失敗で watch 追加結果は変えない。
            }
        }

        // Explorer から来るパスの表記揺れだけ吸収し、不正文字は空扱いにする。
        private static string NormalizeDroppedMainDbPath(string droppedPath)
        {
            if (string.IsNullOrWhiteSpace(droppedPath))
            {
                return "";
            }

            try
            {
                return Path.GetFullPath(droppedPath.Trim());
            }
            catch
            {
                return "";
            }
        }
    }
}
