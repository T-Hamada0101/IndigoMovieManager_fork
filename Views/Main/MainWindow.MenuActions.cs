using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IndigoMovieManager.Converter;
using IndigoMovieManager.Data;
using IndigoMovieManager.DB;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.UpperTabs.Common;
using IndigoMovieManager.ViewModels;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using Notification.Wpf;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        internal Action ReloadBookmarkTabDataForTesting { get; set; }
        internal Func<string, bool, Task> FilterAndSortAsyncForTesting { get; set; }

        private enum DeleteActionMode
        {
            UnregisterOnly = 0,
            DeleteMovieToRecycleBin = 1,
            DeleteThumbnailsOnly = 2,
            DeletePermanently = 3,
            DeleteFileWithChoice = 4,
        }

        private enum DeleteDialogAccent
        {
            Blue,
            Orange,
            Green,
            Red,
        }

        private sealed class MovieDeleteSnapshot
        {
            public MovieRecords UiRecord { get; init; }
            public long MovieId { get; init; }
            public string MoviePath { get; init; } = "";
            public string MovieBody { get; init; } = "";
            public string Hash { get; init; } = "";
        }

        private sealed class MovieDeleteBackgroundResult
        {
            public List<string> DeleteFailureMessages { get; init; } = new();
            public List<MovieRecords> DeletedRecords { get; init; } = new();
        }

        private sealed class MovieMoveSnapshot
        {
            public MovieRecords UiRecord { get; init; }
            public long MovieId { get; init; }
            public string SourcePath { get; init; } = "";
            public string DestinationPath { get; init; } = "";
            public string DestinationFolder { get; init; } = "";
        }

        private sealed class MovieMoveBackgroundResult
        {
            public List<MovieMoveSnapshot> MovedSnapshots { get; init; } = new();
            public List<string> MoveFailureMessages { get; init; } = new();
        }

        private enum MainDbCreateDialogBackgroundStatus
        {
            Created = 0,
            AlreadyExists = 1,
            Failed = 2,
        }

        private sealed class MainDbCreateDialogBackgroundResult
        {
            public MainDbCreateDialogBackgroundStatus Status { get; init; }
            public string ErrorMessage { get; init; } = "";
        }

        private static readonly Brush DeleteDialogOrangeBrush = new SolidColorBrush(
            Color.FromRgb(239, 108, 0)
        );
        private static readonly Brush DeleteDialogBlueBrush = new SolidColorBrush(
            Color.FromRgb(25, 118, 210)
        );
        private static readonly Brush DeleteDialogGreenBrush = new SolidColorBrush(
            Color.FromRgb(46, 125, 50)
        );
        private static readonly Brush DeleteDialogRedBrush = new SolidColorBrush(
            Color.FromRgb(198, 40, 40)
        );

        private readonly IMainDbMovieMutationFacade _mainDbMovieMutationFacade =
            new MainDbMovieMutationFacade();
        private int _deferredManualReloadScanRevision;

        // rescue系メニューは、救済系タブだけに絞って通常一覧での誤操作を避ける。
        internal static Visibility ResolveRescueOnlyContextMenuVisibility(
            bool isUpperTabRescueSelected,
            bool isBottomThumbnailErrorTabSelected
        )
        {
            return isUpperTabRescueSelected || isBottomThumbnailErrorTabSelected
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // 「救済タブへ送る」は通常一覧からの導線に絞り、救済一覧の上では出さない。
        internal static Visibility ResolveSendToThumbnailRescueTabMenuVisibility(
            bool isUpperTabRescueSelected,
            bool isBottomThumbnailErrorTabSelected
        )
        {
            return isUpperTabRescueSelected || isBottomThumbnailErrorTabSelected
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void MenuContext_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu contextMenu)
            {
                return;
            }

            Visibility rescueOnlyMenuVisibility = ResolveRescueOnlyContextMenuVisibility(
                IsUpperTabRescueSelected(),
                IsThumbnailErrorTabVisibleOrSelectedCached()
            );
            Visibility sendToRescueTabMenuVisibility =
                ResolveSendToThumbnailRescueTabMenuVisibility(
                    IsUpperTabRescueSelected(),
                    IsThumbnailErrorTabVisibleOrSelectedCached()
                );

            foreach (
                string rescueOnlyMenuName in new[]
                {
                    "ThumbnailRescueMenu",
                    "ThumbnailDarkHeavyBackgroundRescueMenu",
                    "ThumbnailDarkHeavyBackgroundLiteRescueMenu",
                    "ThumbnailIndexRepairMenu",
                }
            )
            {
                MenuItem rescueOnlyMenu = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(item =>
                    string.Equals(item.Name, rescueOnlyMenuName, StringComparison.Ordinal)
                );
                if (rescueOnlyMenu != null)
                {
                    rescueOnlyMenu.Visibility = rescueOnlyMenuVisibility;
                }
            }

            MenuItem sendToRescueTabMenu = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(item =>
                string.Equals(item.Name, "SendToThumbnailRescueTabMenu", StringComparison.Ordinal)
            );
            if (sendToRescueTabMenu != null)
            {
                sendToRescueTabMenu.Visibility = sendToRescueTabMenuVisibility;
            }
        }

        /// <summary>
        /// ファイルのお引越しはおまかせ！コピー＆ムーブを華麗にこなすメニューの要だ！🚚💨
        /// </summary>
        private void MenuCopyAndMove_Click(object sender, RoutedEventArgs e)
        {
            MenuItem item = sender as MenuItem;

            if (!(item.Name is "FileCopy" or "FileMove"))
            {
                return;
            }

            var dlgTitle = item.Name == "FileCopy" ? "コピー先の選択" : "移動先の選択";
            var dlg = new OpenFolderDialog
            {
                Title = dlgTitle,
                Multiselect = false,
                AddToRecent = true,
            };

            var ret = dlg.ShowDialog();

            if (ret == true)
            {
                if (Tabs.SelectedItem == null)
                {
                    return;
                }

                List<MovieRecords> mv;
                mv = GetSelectedItemsByTabIndex();
                if (mv == null)
                {
                    return;
                }

                var destFolder = dlg.FolderName;
                if (item.Name == "FileCopy")
                {
                    QueueMovieFileCopy(mv, destFolder);
                    return;
                }

                QueueMovieFileMove(mv, destFolder);
            }
        }

        // コピーは登録状態を変えないので、重いファイルI/Oだけ背景へ逃がして UI を先に返す。
        private void QueueMovieFileCopy(IReadOnlyList<MovieRecords> records, string destFolder)
        {
            if (records == null || records.Count == 0 || string.IsNullOrWhiteSpace(destFolder))
            {
                return;
            }

            var copyRequests = records
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Movie_Path))
                .Select(x => (
                    SourcePath: x.Movie_Path,
                    DestinationPath: Path.Combine(destFolder, Path.GetFileName(x.Movie_Path))
                ))
                .ToArray();
            if (copyRequests.Length == 0)
            {
                return;
            }

            FileSystemWatcher[] suppressedWatchers = SetFileWatchersEnabled(destFolder, enabled: false);
            _ = Task.Run(
                () =>
                {
                    List<string> copyFailureMessages = new();
                    try
                    {
                        foreach (var request in copyRequests)
                        {
                            try
                            {
                                File.Copy(request.SourcePath, request.DestinationPath, true);
                            }
                            catch (Exception ex)
                            {
                                AddFileCopyFailure(
                                    copyFailureMessages,
                                    request.SourcePath,
                                    request.DestinationPath,
                                    ex
                                );
                            }
                        }
                    }
                    finally
                    {
                        _ = Dispatcher.InvokeAsync(
                            () =>
                            {
                                RestoreFileWatchers(suppressedWatchers);
                                ShowFileCopyFailureSummary(copyFailureMessages);
                            }
                        );
                    }
                }
            );
        }

        private FileSystemWatcher[] SetFileWatchersEnabled(string folderPath, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return [];
            }

            FileSystemWatcher[] targets = fileWatchers
                .Where(x => string.Equals(x.Path, folderPath, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (FileSystemWatcher watcher in targets)
            {
                TrySetFileWatcherEnabled(watcher, enabled, "set-enabled");
            }

            return targets;
        }

        private static void RestoreFileWatchers(IReadOnlyList<FileSystemWatcher> watchers)
        {
            if (watchers == null)
            {
                return;
            }

            foreach (FileSystemWatcher watcher in watchers)
            {
                TrySetFileWatcherEnabled(watcher, enabled: true, "restore");
            }
        }

        private static bool TrySetFileWatcherEnabled(
            FileSystemWatcher watcher,
            bool enabled,
            string reason
        )
        {
            if (watcher == null)
            {
                return false;
            }

            try
            {
                watcher.EnableRaisingEvents = enabled;
                return true;
            }
            catch (Exception ex)
            {
                // DB切替や終了と重なって破棄済み watcher を触っても、UI操作の完了自体は壊さない。
                DebugRuntimeLog.Write(
                    "watch",
                    $"watcher enable skipped: reason={reason} enabled={enabled} err='{ex.GetType().Name}: {ex.Message}'"
                );
                return false;
            }
        }

        private static void AddFileCopyFailure(
            List<string> copyFailureMessages,
            string sourcePath,
            string destinationPath,
            Exception exception
        )
        {
            string reason = string.IsNullOrWhiteSpace(exception?.Message)
                ? exception?.GetType().Name ?? "理由不明"
                : exception.Message;
            copyFailureMessages.Add($"{sourcePath ?? ""} -> {destinationPath ?? ""} ({reason})");
            DebugRuntimeLog.Write(
                "file-copy",
                $"copy failed: source='{sourcePath ?? ""}' dest='{destinationPath ?? ""}' reason='{reason}'"
            );
        }

        private static void ShowFileCopyFailureSummary(List<string> copyFailureMessages)
        {
            if (copyFailureMessages == null || copyFailureMessages.Count == 0)
            {
                return;
            }

            const int maxVisibleFailures = 5;
            string message = string.Join(
                Environment.NewLine,
                copyFailureMessages.Take(maxVisibleFailures)
            );
            if (copyFailureMessages.Count > maxVisibleFailures)
            {
                message += $"{Environment.NewLine}...他 {copyFailureMessages.Count - maxVisibleFailures} 件";
            }

            MessageBox.Show(
                $"一部のコピーに失敗しました。{Environment.NewLine}{message}",
                Assembly.GetExecutingAssembly().GetName().Name,
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }

        // 移動は大容量・ネットワークパスで詰まりやすいので、物理MoveをUIスレッドから外す。
        private void QueueMovieFileMove(IReadOnlyList<MovieRecords> records, string destFolder)
        {
            if (records == null || records.Count == 0 || string.IsNullOrWhiteSpace(destFolder))
            {
                return;
            }

            string dbFullPathSnapshot = MainVM?.DbInfo?.DBFullPath ?? "";
            MovieMoveSnapshot[] moveRequests = records
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Movie_Path))
                .Select(x => new MovieMoveSnapshot
                {
                    UiRecord = x,
                    MovieId = x.Movie_Id,
                    SourcePath = x.Movie_Path,
                    DestinationPath = Path.Combine(destFolder, Path.GetFileName(x.Movie_Path)),
                    DestinationFolder = destFolder,
                })
                .ToArray();
            if (moveRequests.Length == 0)
            {
                return;
            }

            FileSystemWatcher[] suppressedWatchers = SetFileWatchersEnabled(destFolder, enabled: false);
            _ = Task.Run(() =>
            {
                MovieMoveBackgroundResult moveResult = MoveMovieFilesInBackground(moveRequests);
                try
                {
                    _ = Dispatcher.InvokeAsync(
                        () =>
                            _ = CompleteMovieFileMoveOnUiAsync(
                                moveResult,
                                suppressedWatchers,
                                dbFullPathSnapshot
                            ),
                        System.Windows.Threading.DispatcherPriority.Background
                    );
                }
                catch (Exception ex)
                {
                    RestoreFileWatchers(suppressedWatchers);
                    DebugRuntimeLog.Write(
                        "file-move",
                        $"move completion dispatch failed: moved={moveResult.MovedSnapshots.Count} err='{ex.GetType().Name}: {ex.Message}'"
                    );
                }
            });
        }

        private static MovieMoveBackgroundResult MoveMovieFilesInBackground(
            IReadOnlyList<MovieMoveSnapshot> moveRequests
        )
        {
            MovieMoveBackgroundResult result = new();
            foreach (MovieMoveSnapshot request in moveRequests ?? [])
            {
                try
                {
                    File.Move(request.SourcePath, request.DestinationPath, overwrite: true);
                    result.MovedSnapshots.Add(request);
                }
                catch (Exception ex)
                {
                    AddFileMoveFailure(
                        result.MoveFailureMessages,
                        request.SourcePath,
                        request.DestinationPath,
                        ex
                    );
                }
            }

            return result;
        }

        private async Task CompleteMovieFileMoveOnUiAsync(
            MovieMoveBackgroundResult moveResult,
            IReadOnlyList<FileSystemWatcher> suppressedWatchers,
            string dbFullPathSnapshot
        )
        {
            try
            {
                RestoreFileWatchers(suppressedWatchers);

                foreach (MovieMoveSnapshot movedSnapshot in moveResult?.MovedSnapshots ?? [])
                {
                    QueueMoviePathPersist(
                        dbFullPathSnapshot,
                        movedSnapshot.MovieId,
                        movedSnapshot.DestinationPath
                    );
                }

                int reflectedCount = ReflectMovedMovieRecordsOnUi(
                    moveResult?.MovedSnapshots,
                    dbFullPathSnapshot
                );
                ShowFileMoveFailureSummary(moveResult?.MoveFailureMessages);

                if (reflectedCount < 1)
                {
                    return;
                }

                string sortId = MainVM?.DbInfo?.Sort ?? "";
                if (sortId is "14" or "15")
                {
                    await SortDataAsync(sortId);
                }
                else
                {
                    NotifyUpperTabViewportSourceChanged();
                    RequestUpperTabVisibleRangeRefresh(immediate: true, reason: "movie-move");
                }

                DebugRuntimeLog.Write(
                    "file-move",
                    $"move ui reflected: moved={reflectedCount} sort={sortId}"
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "file-move",
                    $"move completion failed: err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

        private int ReflectMovedMovieRecordsOnUi(
            IReadOnlyList<MovieMoveSnapshot> movedSnapshots,
            string dbFullPathSnapshot
        )
        {
            if (movedSnapshots == null || movedSnapshots.Count == 0)
            {
                return 0;
            }

            if (!AreSameMainDbPath(dbFullPathSnapshot, MainVM?.DbInfo?.DBFullPath ?? ""))
            {
                DebugRuntimeLog.Write(
                    "file-move",
                    $"move ui reflect skipped: reason=db-changed moved={movedSnapshots.Count}"
                );
                return 0;
            }

            int reflectedCount = 0;
            foreach (MovieMoveSnapshot movedSnapshot in movedSnapshots)
            {
                MovieRecords record = movedSnapshot?.UiRecord;
                if (record == null)
                {
                    continue;
                }

                // 物理Move成功後の事実値だけを反映し、一覧の全件再読込はwatcherへ戻さない。
                record.Movie_Path = movedSnapshot.DestinationPath;
                record.Dir = movedSnapshot.DestinationFolder;
                record.Drive = Path.GetPathRoot(movedSnapshot.DestinationPath) ?? "";
                record.IsExists = true;
                reflectedCount++;
            }

            return reflectedCount;
        }

        private static void AddFileMoveFailure(
            List<string> moveFailureMessages,
            string sourcePath,
            string destinationPath,
            Exception exception
        )
        {
            string reason = string.IsNullOrWhiteSpace(exception?.Message)
                ? exception?.GetType().Name ?? "理由不明"
                : exception.Message;
            moveFailureMessages.Add($"{sourcePath ?? ""} -> {destinationPath ?? ""} ({reason})");
            DebugRuntimeLog.Write(
                "file-move",
                $"move failed: source='{sourcePath ?? ""}' dest='{destinationPath ?? ""}' reason='{reason}'"
            );
        }

        private static void ShowFileMoveFailureSummary(List<string> moveFailureMessages)
        {
            if (moveFailureMessages == null || moveFailureMessages.Count == 0)
            {
                return;
            }

            const int maxVisibleFailures = 5;
            string message = string.Join(
                Environment.NewLine,
                moveFailureMessages.Take(maxVisibleFailures)
            );
            if (moveFailureMessages.Count > maxVisibleFailures)
            {
                message += $"{Environment.NewLine}...他 {moveFailureMessages.Count - maxVisibleFailures} 件";
            }

            MessageBox.Show(
                $"一部の移動に失敗しました。{Environment.NewLine}{message}",
                Assembly.GetExecutingAssembly().GetName().Name,
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }

        private void MenuScore_Click(object sender, RoutedEventArgs e)
        {
            string keyName = "";
            if (sender is not MenuItem menuItem)
            {
                if (e is KeyEventArgs key)
                {
                    keyName = key.Key.ToString();
                }
            }
            else
            {
                keyName = menuItem.Name;
            }

            if (Tabs.SelectedItem == null)
            {
                return;
            }

            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null)
            {
                return;
            }

            if (keyName.ToLower() is "add" or "scoreplus")
            {
                mv.Score += 1;
            }
            else if (keyName.ToLower() is "subtract" or "scoreminus")
            {
                mv.Score -= 1;
            }

            QueueMovieScorePersist(MainVM?.DbInfo?.DBFullPath ?? "", mv.Movie_Id, mv.Score);
        }

        // スコア操作は表示だけ先に変え、DB保存は背景へ逃がしてクリックの詰まりを避ける。
        private void QueueMovieScorePersist(string dbFullPath, long movieId, long score)
        {
            if (string.IsNullOrWhiteSpace(dbFullPath) || movieId <= 0)
            {
                return;
            }

            PersistenceWriteRequest writeRequest = PersistenceWriteRequest.Create(
                PersistenceWriteKind.BackgroundDbWrite,
                "movie-score",
                "main-db-score",
                retryable: true
            );
            _ = Task.Run(
                () =>
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    try
                    {
                        _mainDbMovieMutationFacade.UpdateScore(dbFullPath, movieId, score);
                        PersistenceWriteResult result = PersistenceWriteResult.FromSuccess(
                            writeRequest,
                            stopwatch.Elapsed
                        );
                        DebugRuntimeLog.Write(
                            "ui-tempo",
                            $"score persist succeeded: db='{dbFullPath}' movie_id={movieId} {result.LogFields}"
                        );
                    }
                    catch (Exception ex)
                    {
                        PersistenceWriteResult result = PersistenceWriteResult.FromFailure(
                            writeRequest,
                            stopwatch.Elapsed,
                            PersistenceFailureKind.BackgroundDbWrite
                        );
                        DebugRuntimeLog.Write(
                            "ui-tempo",
                            $"score persist failed: db='{dbFullPath}' movie_id={movieId} {result.LogFields} err='{ex.GetType().Name}'"
                        );
                    }
                }
            );
        }

        // ファイル移動後の表示反映を先に返し、movie_path 保存は背景へ逃がす。
        private void QueueMoviePathPersist(string dbFullPath, long movieId, string moviePath)
        {
            if (string.IsNullOrWhiteSpace(dbFullPath) || movieId <= 0)
            {
                return;
            }

            string moviePathSnapshot = moviePath ?? "";
            PersistenceWriteRequest writeRequest = PersistenceWriteRequest.Create(
                PersistenceWriteKind.BackgroundDbWrite,
                "movie-path",
                "main-db-movie-path",
                retryable: true
            );
            _ = Task.Run(
                () =>
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    try
                    {
                        _mainDbMovieMutationFacade.UpdateMoviePath(
                            dbFullPath,
                            movieId,
                            moviePathSnapshot
                        );
                        PersistenceWriteResult result = PersistenceWriteResult.FromSuccess(
                            writeRequest,
                            stopwatch.Elapsed
                        );
                        DebugRuntimeLog.Write(
                            "ui-tempo",
                            $"movie path persist succeeded: db='{dbFullPath}' movie_id={movieId} {result.LogFields}"
                        );
                    }
                    catch (Exception ex)
                    {
                        PersistenceWriteResult result = PersistenceWriteResult.FromFailure(
                            writeRequest,
                            stopwatch.Elapsed,
                            PersistenceFailureKind.BackgroundDbWrite
                        );
                        DebugRuntimeLog.Write(
                            "ui-tempo",
                            $"movie path persist failed: db='{dbFullPath}' movie_id={movieId} {result.LogFields} err='{ex.GetType().Name}'"
                        );
                    }
                }
            );
        }

        private void OpenParentFolder_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null)
            {
                return;
            }

            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null)
            {
                return;
            }

            QueueOpenParentFolderExplorer(mv.Movie_Path, mv.Dir);
        }

        // ネットワークパス確認でクリック直後のUIを止めないよう、選択パスだけ固めて背景へ渡す。
        private void QueueOpenParentFolderExplorer(string moviePath, string dir)
        {
            if (string.IsNullOrWhiteSpace(moviePath))
            {
                return;
            }

            string moviePathSnapshot = moviePath;
            string dirSnapshot = dir ?? "";
            _ = Task.Run(
                () =>
                {
                    bool canOpen = false;
                    try
                    {
                        // 実ファイルと親フォルダの存在確認は遅い媒体ほど詰まりやすいので背景で見る。
                        canOpen = Path.Exists(moviePathSnapshot) && Path.Exists(dirSnapshot);
                    }
                    catch (Exception ex)
                    {
                        DebugRuntimeLog.Write(
                            "ui-tempo",
                            $"open parent folder check failed: path='{moviePathSnapshot}' dir='{dirSnapshot}' err='{ex.GetType().Name}'"
                        );
                    }

                    if (!canOpen)
                    {
                        return;
                    }

                    _ = Dispatcher.InvokeAsync(() =>
                    {
                        Process.Start("explorer.exe", $"/select,{moviePathSnapshot}");
                    });
                }
            );
        }

        /// <summary>
        /// 選択中の動画パスをまとめてクリップボードへ流し込む。
        /// 複数選択時は改行区切りにして、そのまま貼り付けやすくする。
        /// </summary>
        private void CopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null)
            {
                return;
            }

            List<MovieRecords> records = GetSelectedItemsByTabIndex();
            if (records == null || records.Count == 0)
            {
                return;
            }

            // 空文字や null を落として、貼り付け先で扱いやすいパス一覧だけに整える。
            List<string> paths = records
                .Select(record => record.Movie_Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct()
                .ToList();
            if (paths.Count == 0)
            {
                return;
            }

            Clipboard.SetText(string.Join(Environment.NewLine, paths));
        }

        private async void RenameFile_Click(object sender, RoutedEventArgs e)
        {
            string keyName = "";
            if (sender is not MenuItem menuItem)
            {
                if (e is KeyEventArgs keyEvent)
                {
                    keyName = keyEvent.Key.ToString();
                }
            }
            else
            {
                keyName = menuItem.Name;
            }

            if (!(keyName.ToLower() is "f2" or "renamefile"))
            {
                return;
            }

            if (Tabs.SelectedItem == null)
            {
                return;
            }
            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null)
            {
                return;
            }

            // mvをそのまま渡さず、編集に必要な項目だけをコピーする。
            var body = Path.GetFileNameWithoutExtension(mv.Movie_Path);
            MovieRecords dt = new()
            {
                Movie_Id = mv.Movie_Id,
                Movie_Body = body,
                Movie_Path = mv.Movie_Path,
                Movie_Name = mv.Movie_Name,
                Ext = mv.Ext,
            };

            var renameWindow = new RenameFile
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                DataContext = dt,
            };
            renameWindow.ShowDialog();

            if (renameWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return;
            }

            if (dt.Movie_Body == mv.Movie_Body && dt.Ext == mv.Ext)
            {
                return;
            }

            // リネーム。
            var checkFileName = mv.Movie_Body;
            var newFilePath = dt.Movie_Body;
            var checkExt = mv.Ext;
            var newExt = dt.Ext;

            // 実体ファイルのリネームと新旧ファイルパス作成。
            string oldMoviePath = mv.Movie_Path;
            var destMoveFile = mv.Movie_Path.Replace(checkFileName, newFilePath);
            var destFolder = Path.GetDirectoryName(destMoveFile);
            destMoveFile = destMoveFile.Replace(checkExt, newExt);

            FileSystemWatcher[] suppressedWatchers = SetFileWatchersEnabled(destFolder, enabled: false);
            try
            {
                string moveFailureMessage = await Task.Run(
                    () => TryMoveMovieFileInBackground(oldMoviePath, destMoveFile)
                );
                if (!string.IsNullOrWhiteSpace(moveFailureMessage))
                {
                    MessageBox.Show(
                        $"ファイルのリネームに失敗しました。\n{moveFailureMessage}",
                        Assembly.GetExecutingAssembly().GetName().Name,
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return;
                }

                // 監視時のリネーム処理の実体を呼び出し、背景DB/サムネ移送まで同じ抑止範囲で閉じる。
                await RenameThumbAsync(destMoveFile, oldMoviePath);
            }
            finally
            {
                RestoreFileWatchers(suppressedWatchers);
            }
        }

        // 実体ファイルのMoveは遅い媒体でUIを塞ぐため、結果メッセージだけをUIへ戻す。
        private static string TryMoveMovieFileInBackground(string sourcePath, string destinationPath)
        {
            try
            {
                FileInfo mvFile = new(sourcePath);
                mvFile.MoveTo(destinationPath, true);
                return "";
            }
            catch (IOException ex)
            {
                return ex.Message;
            }
            catch (Exception ex)
            {
                return string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
            }
        }

        // Delete系メニューの入口を、ショートカット共通の実処理へ寄せる。
        private void DeleteMovieRecord_Click(object sender, RoutedEventArgs e)
        {
            if (!TryResolveDeleteMenuRequest(sender, e, out DeleteActionMode actionMode))
            {
                return;
            }

            ExecuteDeleteAction(actionMode);
        }

        // Del / Shift+Del / Ctrl+Del を、それぞれ別設定と色の確認ダイアログへ流す。
        private bool TryHandleDeleteShortcut(KeyEventArgs e)
        {
            ModifierKeys modifiers = Keyboard.Modifiers;
            if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                return false;
            }

            DeleteActionMode actionMode;
            if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                actionMode = NormalizeDeleteActionMode(
                    Properties.Settings.Default.CtrlDeleteKeyActionMode,
                    DeleteActionMode.DeleteMovieToRecycleBin
                );
            }
            else if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                actionMode = NormalizeDeleteActionMode(
                    Properties.Settings.Default.ShiftDeleteKeyActionMode,
                    DeleteActionMode.DeleteThumbnailsOnly
                );
            }
            else
            {
                actionMode = NormalizeDeleteActionMode(
                    Properties.Settings.Default.DeleteKeyActionMode,
                    DeleteActionMode.UnregisterOnly
                );
            }

            ExecuteDeleteAction(actionMode);
            e.Handled = true;
            return true;
        }

        private static DeleteActionMode NormalizeDeleteActionMode(
            int configuredValue,
            DeleteActionMode fallback
        )
        {
            return configuredValue switch
            {
                0 => DeleteActionMode.UnregisterOnly,
                1 => DeleteActionMode.DeleteMovieToRecycleBin,
                2 => DeleteActionMode.DeleteThumbnailsOnly,
                3 => DeleteActionMode.DeletePermanently,
                _ => fallback,
            };
        }

        private static bool TryResolveDeleteMenuRequest(
            object sender,
            RoutedEventArgs e,
            out DeleteActionMode actionMode
        )
        {
            actionMode = DeleteActionMode.UnregisterOnly;

            if (sender is MenuItem menuItem)
            {
                switch (menuItem.Name)
                {
                    case "DeleteMovie":
                        actionMode = DeleteActionMode.UnregisterOnly;
                        return true;
                    case "DeleteFile":
                        actionMode = DeleteActionMode.DeleteFileWithChoice;
                        return true;
                    case "DeleteWithRecycle":
                        actionMode = DeleteActionMode.DeleteMovieToRecycleBin;
                        return true;
                    case "DeleteThumbnailOnly":
                        actionMode = DeleteActionMode.DeleteThumbnailsOnly;
                        return true;
                    case "DeletePermanent":
                        actionMode = DeleteActionMode.DeletePermanently;
                        return true;
                    default:
                        return false;
                }
            }

            if (e is KeyEventArgs keyEvent && keyEvent.Key == Key.Delete)
            {
                actionMode = NormalizeDeleteActionMode(
                    Properties.Settings.Default.DeleteKeyActionMode,
                    DeleteActionMode.UnregisterOnly
                );
                return true;
            }

            return false;
        }

        // 確認ダイアログを出してから、登録解除 / サムネイル削除 / 動画削除をまとめて処理する。
        private void ExecuteDeleteAction(DeleteActionMode actionMode)
        {
            if (Tabs.SelectedItem == null)
            {
                return;
            }

            List<MovieRecords> mv = GetSelectedItemsByTabIndex();
            if (mv == null || mv.Count == 0)
            {
                return;
            }

            bool isDeleteFileMode = actionMode == DeleteActionMode.DeleteFileWithChoice;
            bool isDeleteWithRecycleMode = actionMode == DeleteActionMode.DeleteMovieToRecycleBin;
            bool isDeleteThumbnailOnlyMode = actionMode == DeleteActionMode.DeleteThumbnailsOnly;
            bool isDeletePermanentMode = actionMode == DeleteActionMode.DeletePermanently;

            string msg = $"登録からデータを削除します\n（監視対象の場合、再監視で復活します）";
            string title = "登録から削除します";
            string headline = "";
            string radio1Content = "";
            string radio2Content = "";
            bool useRadio = false;
            bool useCheckBox = true;
            bool checkBoxIsChecked = true;
            string checkBoxContent = "サムネイルも削除する";
            MaterialDesignThemes.Wpf.PackIconKind dialogIconKind =
                MaterialDesignThemes.Wpf.PackIconKind.ExclamationBold;
            DeleteDialogAccent dialogAccent = DeleteDialogAccent.Blue;
            MaterialDesignThemes.Wpf.PackIconKind? radio1IconKind = null;
            MaterialDesignThemes.Wpf.PackIconKind? radio2IconKind = null;
            DeleteDialogAccent? radio1Accent = null;
            DeleteDialogAccent? radio2Accent = null;

            if (isDeleteFileMode)
            {
                msg = "登録元のファイルを削除します。";
                title = "ファイル削除";
                useRadio = true;
                radio1Content = "ゴミ箱に移動して削除";
                radio2Content = "ディスクから完全に削除";
                dialogIconKind = MaterialDesignThemes.Wpf.PackIconKind.DeleteRestore;
                dialogAccent = DeleteDialogAccent.Orange;
                radio1IconKind = MaterialDesignThemes.Wpf.PackIconKind.DeleteRestore;
                radio2IconKind = MaterialDesignThemes.Wpf.PackIconKind.DeleteForever;
                radio1Accent = DeleteDialogAccent.Orange;
                radio2Accent = DeleteDialogAccent.Red;
            }
            else if (isDeleteWithRecycleMode)
            {
                // Delキー設定で選ばれた時は、登録解除＋ゴミ箱移動を固定で実行する。
                headline = BuildDeleteDialogHeadline(mv);
                msg = "ゴミ箱に移動します。\nゴミ箱に入らない大きさの場合は削除されます。";
                title = "動画をゴミ箱へ移動";
                dialogIconKind = MaterialDesignThemes.Wpf.PackIconKind.DeleteRestore;
                dialogAccent = DeleteDialogAccent.Orange;
            }
            else if (isDeleteThumbnailOnlyMode)
            {
                msg = "選択した動画のサムネイルを削除します。";
                title = "サムネイル削除";
                useCheckBox = false;
                checkBoxIsChecked = false;
                checkBoxContent = "";
                dialogIconKind = MaterialDesignThemes.Wpf.PackIconKind.ImageRemove;
                dialogAccent = DeleteDialogAccent.Green;
            }
            else if (isDeletePermanentMode)
            {
                headline = BuildDeleteDialogHeadline(mv);
                msg = "元に戻せません。";
                title = "削除";
                dialogIconKind = MaterialDesignThemes.Wpf.PackIconKind.DeleteForever;
                dialogAccent = DeleteDialogAccent.Red;
            }

            var dialogWindow = new MessageBoxEx(this)
            {
                CheckBoxContent = checkBoxContent,
                UseRadioButton = useRadio,
                UseCheckBox = useCheckBox,
                CheckBoxIsChecked = checkBoxIsChecked,
                DlogHeadline = headline,
                DlogMessage = msg,
                DlogTitle = title,
                Radio1Content = radio1Content,
                Radio2Content = radio2Content,
                PackIconKind = dialogIconKind,
                Radio1PackIconKind = radio1IconKind,
                Radio2PackIconKind = radio2IconKind,
            };
            ApplyDeleteDialogAccent(dialogWindow, dialogAccent);
            ApplyDeleteDialogAccent(dialogWindow, radio1Accent, isRadio1: true);
            ApplyDeleteDialogAccent(dialogWindow, radio2Accent, isRadio1: false);

            dialogWindow.ShowDialog();
            if (dialogWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return;
            }

            if (isDeleteThumbnailOnlyMode)
            {
                QueueThumbnailOnlyDelete(mv);
                return;
            }

            string dbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            string thumbFolder = ResolveCurrentThumbnailRoot();
            string thumbOutPath = ResolveCurrentThumbnailOutPath(GetCurrentThumbnailActionTabIndex());
            bool shouldDeleteThumbnail = dialogWindow.checkBox.IsChecked == true;
            bool sendThumbnailToRecycleBin = isDeleteWithRecycleMode;
            bool shouldDeletePhysicalFile =
                isDeleteFileMode || isDeleteWithRecycleMode || isDeletePermanentMode;
            bool sendPhysicalToRecycleBin = isDeleteFileMode
                ? dialogWindow.radioButton1.IsChecked == true
                : isDeleteWithRecycleMode;

            QueueConfirmedMovieDelete(
                mv,
                dbFullPath,
                thumbFolder,
                thumbOutPath,
                shouldDeleteThumbnail,
                sendThumbnailToRecycleBin,
                shouldDeletePhysicalFile,
                sendPhysicalToRecycleBin
            );
        }

        // 確認後のDB/ファイルI/Oはsnapshotだけを背景へ渡し、クリック導線をUIへ早く返す。
        private void QueueConfirmedMovieDelete(
            IReadOnlyList<MovieRecords> records,
            string dbFullPath,
            string thumbFolder,
            string thumbOutPath,
            bool shouldDeleteThumbnail,
            bool sendThumbnailToRecycleBin,
            bool shouldDeletePhysicalFile,
            bool sendPhysicalToRecycleBin
        )
        {
            MovieDeleteSnapshot[] deleteSnapshots = records
                ?.Where(record => record != null)
                .Select(CreateMovieDeleteSnapshot)
                .ToArray() ?? [];
            if (deleteSnapshots.Length == 0 || string.IsNullOrWhiteSpace(dbFullPath))
            {
                return;
            }

            _ = Task.Run(
                () =>
                {
                    MovieDeleteBackgroundResult result;
                    try
                    {
                        result = DeleteMoviesInBackground(
                            deleteSnapshots,
                            dbFullPath,
                            thumbFolder,
                            thumbOutPath,
                            shouldDeleteThumbnail,
                            sendThumbnailToRecycleBin,
                            shouldDeletePhysicalFile,
                            sendPhysicalToRecycleBin
                        );
                    }
                    catch (Exception ex)
                    {
                        result = new MovieDeleteBackgroundResult();
                        string reason = string.IsNullOrWhiteSpace(ex.Message)
                            ? ex.GetType().Name
                            : ex.Message;
                        AddDeleteFailure(result.DeleteFailureMessages, "削除処理", dbFullPath, reason);
                    }

                    _ = Dispatcher.InvokeAsync(
                        () =>
                        {
                            ShowDeleteFailureSummary(result.DeleteFailureMessages);
                            if (!AreSameMainDbPath(dbFullPath, MainVM?.DbInfo?.DBFullPath ?? ""))
                            {
                                return;
                            }

                            RefreshVisibleMovieUiAfterMovieDelete(result.DeletedRecords);
                        }
                    );
                }
            );
        }

        private MovieDeleteBackgroundResult DeleteMoviesInBackground(
            IReadOnlyList<MovieDeleteSnapshot> deleteSnapshots,
            string dbFullPath,
            string thumbFolder,
            string thumbOutPath,
            bool shouldDeleteThumbnail,
            bool sendThumbnailToRecycleBin,
            bool shouldDeletePhysicalFile,
            bool sendPhysicalToRecycleBin
        )
        {
            MovieDeleteBackgroundResult result = new();
            foreach (MovieDeleteSnapshot snapshot in deleteSnapshots)
            {
                if (snapshot == null)
                {
                    continue;
                }

                if (shouldDeleteThumbnail)
                {
                    try
                    {
                        DeleteThumbnailsForMovieCore(
                            snapshot,
                            thumbFolder,
                            thumbOutPath,
                            sendThumbnailToRecycleBin,
                            result.DeleteFailureMessages
                        );
                    }
                    catch (Exception ex)
                    {
                        string reason = string.IsNullOrWhiteSpace(ex.Message)
                            ? ex.GetType().Name
                            : ex.Message;
                        AddDeleteFailure(result.DeleteFailureMessages, "サムネイル", snapshot.MoviePath, reason);
                    }
                }

                int deletedCount = TryDeleteMovieTableInBackground(
                    dbFullPath,
                    snapshot,
                    result.DeleteFailureMessages
                );
                TryAdjustRegisteredMovieCount(dbFullPath, -deletedCount);
                if (deletedCount > 0)
                {
                    result.DeletedRecords.Add(snapshot.UiRecord);
                }

                if (shouldDeletePhysicalFile)
                {
                    DeletePhysicalMovieFileInBackground(
                        snapshot,
                        sendPhysicalToRecycleBin,
                        result.DeleteFailureMessages
                    );
                }
            }

            return result;
        }

        private int TryDeleteMovieTableInBackground(
            string dbFullPath,
            MovieDeleteSnapshot snapshot,
            List<string> deleteFailureMessages
        )
        {
            try
            {
                return DeleteMovieTable(dbFullPath, snapshot.MovieId);
            }
            catch (Exception ex)
            {
                string reason = string.IsNullOrWhiteSpace(ex.Message)
                    ? ex.GetType().Name
                    : ex.Message;
                AddDeleteFailure(deleteFailureMessages, "DB", snapshot.MoviePath, reason);
                return 0;
            }
        }

        private static void DeletePhysicalMovieFileInBackground(
            MovieDeleteSnapshot snapshot,
            bool sendToRecycleBin,
            List<string> deleteFailureMessages
        )
        {
            if (
                !TryDeletePhysicalFile(
                    snapshot.MoviePath,
                    sendToRecycleBin,
                    out string failureReason
                )
            )
            {
                AddDeleteFailure(deleteFailureMessages, "動画", snapshot.MoviePath, failureReason);
            }
        }

        // DBから消えた行だけを手元の表示モデルから抜き、動画削除後の全件DB再読込を避ける。
        private void RefreshVisibleMovieUiAfterMovieDelete(IReadOnlyList<MovieRecords> records)
        {
            HashSet<long> deletedMovieIds = records
                ?.Where(record => record != null && record.Movie_Id > 0)
                .Select(record => record.Movie_Id)
                .ToHashSet() ?? [];
            if (deletedMovieIds.Count == 0 || MainVM?.MovieRecs == null || MainVM.FilteredMovieRecs == null)
            {
                return;
            }

            int sourceRemovedCount = RemoveDeletedMovieRecordsById(MainVM.MovieRecs, deletedMovieIds);
            MovieRecords[] nextFilteredMovies = MainVM.FilteredMovieRecs
                .Where(record => record != null && !deletedMovieIds.Contains(record.Movie_Id))
                .ToArray();

            int currentTabIndex = TryGetCurrentUpperTabFixedIndex(out int resolvedTabIndex)
                ? resolvedTabIndex
                : UpperTabGridFixedIndex;
            FilteredMovieRecsUpdateMode updateMode =
                UpperTabCollectionUpdatePolicy.ResolveUpdateMode(
                    currentTabIndex,
                    isSortOnly: false
                );
            MovieRecords selectedBeforeCollectionApply = GetSelectedItemByTabIndex();
            FilteredMovieRecsUpdateResult applyResult = MainVM.ReplaceFilteredMovieRecs(
                nextFilteredMovies,
                updateMode: updateMode
            );

            filterList = nextFilteredMovies;
            MainVM.DbInfo.SearchCount = nextFilteredMovies.Length;
            UpdateExtensionDetailVisibilityBySearchCount();

            bool hasChanges = sourceRemovedCount > 0 || applyResult.HasChanges;
            bool shouldRefresh = RefreshSelectionDetailAfterCollectionApplyIfNeeded(
                selectedBeforeCollectionApply,
                applyResult,
                currentTabIndex,
                updateMode
            );

            if (hasChanges)
            {
                NotifyUpperTabViewportSourceChanged();
                InvalidateThumbnailErrorRecords(refreshIfVisible: true);
                RequestUpperTabVisibleRangeRefresh(immediate: true, reason: "movie-delete");
                RefreshUpperTabPreferredMoviePathKeysRevision();
                RequestThumbnailErrorSnapshotRefresh();
                RequestThumbnailProgressSnapshotRefresh();
            }

            if (string.Equals(MainVM.DbInfo.Sort, "28", StringComparison.Ordinal))
            {
                RefreshThumbnailErrorRecords(force: true);
            }

            DebugRuntimeLog.Write(
                "ui-tempo",
                $"movie delete local refresh: source_removed={sourceRemovedCount} filtered_removed={applyResult.RemovedCount} update_mode={updateMode} refresh_applied={shouldRefresh} count={nextFilteredMovies.Length}"
            );
        }

        internal static int RemoveDeletedMovieRecordsById(
            IList<MovieRecords> records,
            ISet<long> deletedMovieIds
        )
        {
            if (records == null || deletedMovieIds == null || deletedMovieIds.Count == 0)
            {
                return 0;
            }

            int removedCount = 0;
            for (int index = records.Count - 1; index >= 0; index--)
            {
                MovieRecords record = records[index];
                if (record == null || !deletedMovieIds.Contains(record.Movie_Id))
                {
                    continue;
                }

                // 後ろから抜くことで、削除中のインデックスずれを避ける。
                records.RemoveAt(index);
                removedCount++;
            }

            return removedCount;
        }

        // サムネイルのみ削除は DB を触らないため、ファイル削除を背景へ逃がして UI 操作を先に返す。
        private void QueueThumbnailOnlyDelete(IReadOnlyList<MovieRecords> records)
        {
            MovieRecords[] recordSnapshot = records?.Where(x => x != null).ToArray() ?? [];
            if (recordSnapshot.Length == 0)
            {
                return;
            }

            string thumbFolder = ResolveCurrentThumbnailRoot();
            string thumbOutPath = ResolveCurrentThumbnailOutPath(GetCurrentThumbnailActionTabIndex());
            string dbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            int actionTabIndex = GetCurrentThumbnailActionTabIndex();

            _ = Task.Run(
                () =>
                {
                    List<string> deleteFailureMessages = new();
                    List<MovieRecords> deletedRecords = new();
                    foreach (MovieRecords rec in recordSnapshot)
                    {
                        int failureCountBeforeDelete = deleteFailureMessages.Count;
                        DeleteThumbnailsForMovieCore(
                            CreateMovieDeleteSnapshot(rec),
                            thumbFolder,
                            thumbOutPath,
                            sendToRecycleBin: false,
                            deleteFailureMessages
                        );
                        if (deleteFailureMessages.Count == failureCountBeforeDelete)
                        {
                            deletedRecords.Add(rec);
                        }
                    }

                    _ = Dispatcher.InvokeAsync(
                        () =>
                        {
                            ShowDeleteFailureSummary(deleteFailureMessages);
                            if (!AreSameMainDbPath(dbFullPath, MainVM?.DbInfo?.DBFullPath ?? ""))
                            {
                                return;
                            }

                            RefreshVisibleThumbnailUiAfterThumbnailOnlyDelete(
                                deletedRecords,
                                actionTabIndex
                            );
                        }
                    );
                }
            );
        }

        // サムネイルのみ削除はDBに影響しないため、表示モデルと既存の軽量更新だけを進める。
        private void RefreshVisibleThumbnailUiAfterThumbnailOnlyDelete(
            IReadOnlyList<MovieRecords> records,
            int actionTabIndex
        )
        {
            if (records == null || records.Count == 0)
            {
                return;
            }

            foreach (MovieRecords record in records)
            {
                if (!ClearThumbnailPathsForThumbnailOnlyDelete(record))
                {
                    continue;
                }

                TryQueueExternalSkinThumbnailUpdated(
                    record,
                    actionTabIndex,
                    "thumbnail-only-delete"
                );
            }

            InvalidateThumbnailErrorRecords(refreshIfVisible: true);
            RequestUpperTabVisibleRangeRefresh(immediate: true, reason: "thumbnail-only-delete");
            RefreshUpperTabPreferredMoviePathKeysRevision();
            RequestThumbnailErrorSnapshotRefresh();
            RequestThumbnailProgressSnapshotRefresh();
        }

        // 削除済みjpgを再参照しないよう、保持しているサムネ表示パスを空にしてPropertyChangedへ流す。
        internal static bool ClearThumbnailPathsForThumbnailOnlyDelete(MovieRecords record)
        {
            if (record == null)
            {
                return false;
            }

            bool changed = false;
            ClearThumbnailPathForThumbnailOnlyDelete(
                record.ThumbPathSmall,
                value => record.ThumbPathSmall = value,
                ref changed
            );
            ClearThumbnailPathForThumbnailOnlyDelete(
                record.ThumbPathBig,
                value => record.ThumbPathBig = value,
                ref changed
            );
            ClearThumbnailPathForThumbnailOnlyDelete(
                record.ThumbPathGrid,
                value => record.ThumbPathGrid = value,
                ref changed
            );
            ClearThumbnailPathForThumbnailOnlyDelete(
                record.ThumbPathList,
                value => record.ThumbPathList = value,
                ref changed
            );
            ClearThumbnailPathForThumbnailOnlyDelete(
                record.ThumbPathBig10,
                value => record.ThumbPathBig10 = value,
                ref changed
            );
            ClearThumbnailPathForThumbnailOnlyDelete(
                record.ThumbDetail,
                value => record.ThumbDetail = value,
                ref changed
            );

            if (record.ThumbnailErrorMarkerCount != 0)
            {
                record.ThumbnailErrorMarkerCount = 0;
                changed = true;
            }

            return changed;
        }

        private static void ClearThumbnailPathForThumbnailOnlyDelete(
            string currentPath,
            Action<string> applyPath,
            ref bool changed
        )
        {
            if (applyPath == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                NoLockImageConverter.InvalidateFilePath(currentPath);
                applyPath(string.Empty);
                changed = true;
            }
        }

        // 選択動画に紐づくサムネイル本体と ERROR マーカーをまとめて片付ける。
        private void DeleteThumbnailsForMovie(
            MovieRecords rec,
            bool sendToRecycleBin,
            List<string> deleteFailureMessages
        )
        {
            if (rec == null)
            {
                return;
            }

            DeleteThumbnailsForMovieCore(
                CreateMovieDeleteSnapshot(rec),
                ResolveCurrentThumbnailRoot(),
                ResolveCurrentThumbnailOutPath(GetCurrentThumbnailActionTabIndex()),
                sendToRecycleBin,
                deleteFailureMessages
            );
        }

        private static MovieDeleteSnapshot CreateMovieDeleteSnapshot(MovieRecords record)
        {
            return new MovieDeleteSnapshot
            {
                UiRecord = record,
                MovieId = record.Movie_Id,
                MoviePath = record.Movie_Path ?? "",
                MovieBody = record.Movie_Body ?? "",
                Hash = record.Hash ?? "",
            };
        }

        private void DeleteThumbnailsForMovieCore(
            MovieDeleteSnapshot snapshot,
            string thumbFolder,
            string thumbOutPath,
            bool sendToRecycleBin,
            List<string> deleteFailureMessages
        )
        {
            if (snapshot == null)
            {
                return;
            }

            if (Path.Exists(thumbFolder))
            {
                DirectoryInfo di = new(thumbFolder);
                EnumerationOptions enumOption = new() { RecurseSubdirectories = true };

                // 生成時と同じ命名規則を優先し、旧命名は互換フォールバックで拾う。
                string primaryFileName = ThumbnailPathResolver.BuildThumbnailFileName(
                    snapshot.MoviePath,
                    snapshot.Hash
                );
                IEnumerable<FileInfo> primaryFiles = di.EnumerateFiles(primaryFileName, enumOption);

                string legacyPattern = $"*{snapshot.MovieBody}.#{snapshot.Hash}*.jpg";
                IEnumerable<FileInfo> legacyFiles = di.EnumerateFiles(legacyPattern, enumOption);

                foreach (
                    FileInfo item in primaryFiles
                        .Concat(legacyFiles)
                        .GroupBy(x => x.FullName, StringComparer.OrdinalIgnoreCase)
                        .Select(x => x.First())
                )
                {
                    if (
                        !TryDeleteThumbnailFile(
                            item.FullName,
                            sendToRecycleBin,
                            out string failureReason
                        )
                    )
                    {
                        AddDeleteFailure(
                            deleteFailureMessages,
                            "サムネイル",
                            item.FullName,
                            failureReason
                        );
                    }
                }
            }

            TryDeleteThumbnailErrorMarker(thumbOutPath, snapshot.MoviePath);
        }

        // サムネ削除前に画像キャッシュを外し、自前参照で消せない事故を減らす。
        internal static bool TryDeleteThumbnailFile(
            string filePath,
            bool sendToRecycleBin,
            out string failureReason
        )
        {
            failureReason = "";
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return true;
            }

            NoLockImageConverter.InvalidateFilePath(filePath);
            return TryDeletePhysicalFile(filePath, sendToRecycleBin, out failureReason);
        }

        // 削除失敗は UI クラッシュへ繋げず、呼び出し元で集約表示できるよう bool で返す。
        internal static bool TryDeletePhysicalFile(
            string filePath,
            bool sendToRecycleBin,
            out string failureReason
        )
        {
            failureReason = "";
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return true;
            }

            try
            {
                if (sendToRecycleBin)
                {
                    FileSystem.DeleteFile(
                        filePath,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin
                    );
                }
                else
                {
                    File.Delete(filePath);
                }

                return true;
            }
            catch (Exception ex)
            {
                failureReason = string.IsNullOrWhiteSpace(ex.Message)
                    ? ex.GetType().Name
                    : ex.Message;
                return false;
            }
        }

        // 失敗内容はログへ残しつつ、最後の警告ダイアログでまとめて見せる。
        private static void AddDeleteFailure(
            List<string> deleteFailureMessages,
            string targetLabel,
            string targetPath,
            string failureReason
        )
        {
            string safePath = targetPath ?? "";
            string safeReason = string.IsNullOrWhiteSpace(failureReason) ? "理由不明" : failureReason;
            deleteFailureMessages.Add($"{targetLabel}: {safePath} ({safeReason})");
            DebugRuntimeLog.Write(
                "delete-action",
                $"{targetLabel} delete failed: path='{safePath}' reason='{safeReason}'"
            );
        }

        private static void ShowDeleteFailureSummary(List<string> deleteFailureMessages)
        {
            if (deleteFailureMessages.Count == 0)
            {
                return;
            }

            const int maxVisibleFailures = 5;
            string message = string.Join(
                Environment.NewLine,
                deleteFailureMessages.Take(maxVisibleFailures)
            );
            if (deleteFailureMessages.Count > maxVisibleFailures)
            {
                message +=
                    $"{Environment.NewLine}...他 {deleteFailureMessages.Count - maxVisibleFailures} 件";
            }

            MessageBox.Show(
                $"一部の削除に失敗しました。ファイルが使用中の可能性があります。{Environment.NewLine}{message}",
                Assembly.GetExecutingAssembly().GetName().Name,
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }

        private static void ApplyDeleteDialogAccent(
            MessageBoxEx dialogWindow,
            DeleteDialogAccent dialogAccent
        )
        {
            ApplyDeleteDialogAccentCore(
                dialogWindow,
                dialogAccent,
                assignBaseAccent: true,
                isRadio1: false
            );
        }

        private static void ApplyDeleteDialogAccent(
            MessageBoxEx dialogWindow,
            DeleteDialogAccent? dialogAccent,
            bool isRadio1
        )
        {
            if (!dialogAccent.HasValue)
            {
                return;
            }

            ApplyDeleteDialogAccentCore(
                dialogWindow,
                dialogAccent.Value,
                assignBaseAccent: false,
                isRadio1: isRadio1
            );
        }

        private static void ApplyDeleteDialogAccentCore(
            MessageBoxEx dialogWindow,
            DeleteDialogAccent dialogAccent,
            bool assignBaseAccent,
            bool isRadio1
        )
        {
            Brush accentBrush;
            Brush foregroundBrush = Brushes.White;
            switch (dialogAccent)
            {
                case DeleteDialogAccent.Blue:
                    accentBrush = DeleteDialogBlueBrush;
                    break;
                case DeleteDialogAccent.Orange:
                    accentBrush = DeleteDialogOrangeBrush;
                    break;
                case DeleteDialogAccent.Green:
                    accentBrush = DeleteDialogGreenBrush;
                    break;
                case DeleteDialogAccent.Red:
                    accentBrush = DeleteDialogRedBrush;
                    break;
                default:
                    accentBrush = null;
                    foregroundBrush = Brushes.White;
                    break;
            }

            if (assignBaseAccent)
            {
                dialogWindow.DialogAccentBrush = accentBrush;
                dialogWindow.DialogAccentForegroundBrush = foregroundBrush;
                return;
            }

            if (isRadio1)
            {
                dialogWindow.Radio1AccentBrush = accentBrush;
                dialogWindow.Radio1AccentForegroundBrush = foregroundBrush;
            }
            else
            {
                dialogWindow.Radio2AccentBrush = accentBrush;
                dialogWindow.Radio2AccentForegroundBrush = foregroundBrush;
            }
        }

        // 単体は動画名をそのまま出し、複数選択時は件数付きで見出しへ圧縮する。
        private static string BuildDeleteDialogHeadline(IReadOnlyList<MovieRecords> records)
        {
            if (records == null || records.Count == 0)
            {
                return "動画を削除します";
            }

            string movieLabel = BuildDeleteDialogMovieLabel(records[0]);
            if (records.Count == 1)
            {
                return $"{movieLabel}を削除します";
            }

            return $"{movieLabel} ほか{records.Count}件を削除します";
        }

        private static string BuildDeleteDialogMovieLabel(MovieRecords record)
        {
            string movieName = record?.Movie_Body;
            if (string.IsNullOrWhiteSpace(movieName))
            {
                movieName = Path.GetFileNameWithoutExtension(record?.Movie_Path ?? "");
            }
            if (string.IsNullOrWhiteSpace(movieName))
            {
                movieName = "動画";
            }

            return $"「{movieName}」";
        }

        private void BtnReCreateThumbnail_Click(object sender, RoutedEventArgs e)
        {
            QueueRecreateAllThumbnailsFromCurrentTab(closeMenu: true);
        }

        // 現在タブの全動画をサムネイル再作成キューへ積む共通入口。
        private bool QueueRecreateAllThumbnailsFromCurrentTab(bool closeMenu)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
            {
                MessageBox.Show(
                    "管理ファイルが選択されていません。",
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Exclamation
                );
                return false;
            }

            if (Tabs.SelectedItem == null)
            {
                return false;
            }

            var dialogWindow = new MessageBoxEx(this)
            {
                DlogTitle = "サムネイルの再作成",
                DlogMessage = "サムネイルを再作成します。よろしいですか？",
                PackIconKind = MaterialDesignThemes.Wpf.PackIconKind.EventQuestion,
            };

            dialogWindow.ShowDialog();
            if (dialogWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return false;
            }

            if (closeMenu)
            {
                MenuToggleButton.IsChecked = false;
            }

            foreach (var item in MainVM.MovieRecs)
            {
                int currentTabIndex = GetCurrentThumbnailActionTabIndex();
                QueueObj tempObj = new()
                {
                    MovieId = item.Movie_Id,
                    MovieFullPath = item.Movie_Path,
                    Hash = item.Hash,
                    Tabindex = currentTabIndex,
                    Priority = ThumbnailQueuePriority.Normal,
                };
                _ = TryEnqueueThumbnailJob(tempObj);
            }

            return true;
        }

        // 右クリック明示救済の入口を一本化し、mode 指定だけ差し替えて再利用する。
        private void RunThumbnailRescueMenuAction(
            object sender,
            string rescueMode,
            string upperReason,
            string normalReason,
            string toastTitle
        )
        {
            int currentTabIndex = GetCurrentUpperTabFixedIndex();
            int targetTabIndex = GetCurrentThumbnailActionTabIndex();
            DebugRuntimeLog.Write(
                "thumbnail-rescue",
                $"context rescue clicked: tab={targetTabIndex} mode={rescueMode ?? ""}"
            );

            if (Tabs.SelectedItem == null)
            {
                ShowThumbnailUserActionPopup(
                    toastTitle,
                    "対象タブを選択してから実行してください。",
                    MessageBoxImage.Warning
                );
                return;
            }

            bool isBottomErrorContext = IsThumbnailErrorBottomContextMenuInvocation(sender);
            if (!isBottomErrorContext && currentTabIndex == ThumbnailErrorTabIndex)
            {
                List<MovieRecords> rescueRecords = GetSelectedUpperTabRescueMovieRecords();
                MovieRecords firstRescueRecord = NormalizeThumbnailUserActionMovieRecords(
                    rescueRecords
                ).FirstOrDefault();
                if (firstRescueRecord == null)
                {
                    ShowThumbnailUserActionPopup(
                        toastTitle,
                        "対象動画が選択されていません。",
                        MessageBoxImage.Warning
                    );
                    return;
                }

                RememberManualThumbnailRescueMoviePath(firstRescueRecord.Movie_Path);
                ReportManualThumbnailRescueProgress(
                    BuildManualThumbnailRescueModeProgressMessage(rescueMode),
                    true
                );
                ThumbnailRescueUserActionDispatchResult upperDispatchResult =
                    DispatchThumbnailRescueUserAction(
                        rescueRecords,
                        new ThumbnailRescueUserActionRequest(
                            TargetTabIndex: targetTabIndex,
                            Priority: ThumbnailQueuePriority.Normal,
                            Reason: upperReason,
                            UseDedicatedManualWorkerSlot: true,
                            SkipWhenSuccessExists: false,
                            RescueMode: rescueMode,
                            DeleteErrorMarkerFirst: true
                        )
                    );

                DebugRuntimeLog.Write(
                    "thumbnail-rescue",
                    $"context rescue enqueue end: tab={targetTabIndex} selected={upperDispatchResult.SelectedCount} queued={upperDispatchResult.AcceptedCount}"
                );
                ShowThumbnailUserActionPopup(
                    toastTitle,
                    BuildThumbnailRescueUserActionPopupMessage(
                        toastTitle,
                        upperDispatchResult.SelectedCount,
                        upperDispatchResult.AcceptedCount,
                        upperDispatchResult.DuplicateRequestCount,
                        upperDispatchResult.ExistingSuccessCount
                    ),
                    ResolveThumbnailRescueUserActionPopupImage(
                        upperDispatchResult.AcceptedCount,
                        upperDispatchResult.DuplicateRequestCount,
                        upperDispatchResult.ExistingSuccessCount
                    )
                );
                RefreshThumbnailManualUserActionUiIfAccepted(
                    upperDispatchResult.AcceptedCount,
                    upperReason
                );
                return;
            }

            if (!IsUpperThumbnailTabIndex(targetTabIndex))
            {
                ShowThumbnailUserActionPopup(
                    toastTitle,
                    "処理先のサムネイルタブを特定できませんでした。",
                    MessageBoxImage.Warning
                );
                return;
            }

            List<MovieRecords> records = ResolveSelectedMovieRecordsForThumbnailUserAction(sender);
            MovieRecords firstRecord = NormalizeThumbnailUserActionMovieRecords(records).FirstOrDefault();
            if (firstRecord == null)
            {
                ShowThumbnailUserActionPopup(
                    toastTitle,
                    "対象動画が選択されていません。",
                    MessageBoxImage.Warning
                );
                return;
            }

            RememberManualThumbnailRescueMoviePath(firstRecord.Movie_Path);
            ReportManualThumbnailRescueProgress(
                BuildManualThumbnailRescueModeProgressMessage(rescueMode),
                true
            );
            ThumbnailRescueUserActionDispatchResult normalDispatchResult =
                DispatchThumbnailRescueUserAction(
                    records,
                    new ThumbnailRescueUserActionRequest(
                        TargetTabIndex: targetTabIndex,
                        Priority: ThumbnailQueuePriority.Normal,
                        Reason: normalReason,
                        UseDedicatedManualWorkerSlot: true,
                        SkipWhenSuccessExists: false,
                        RescueMode: rescueMode,
                        DeleteErrorMarkerFirst: true
                    )
                );

            DebugRuntimeLog.Write(
                "thumbnail-rescue",
                $"context rescue enqueue end: tab={targetTabIndex} selected={normalDispatchResult.SelectedCount} queued={normalDispatchResult.AcceptedCount}"
            );
            ShowThumbnailUserActionPopup(
                toastTitle,
                BuildThumbnailRescueUserActionPopupMessage(
                    toastTitle,
                    normalDispatchResult.SelectedCount,
                    normalDispatchResult.AcceptedCount,
                    normalDispatchResult.DuplicateRequestCount,
                    normalDispatchResult.ExistingSuccessCount
                ),
                ResolveThumbnailRescueUserActionPopupImage(
                    normalDispatchResult.AcceptedCount,
                    normalDispatchResult.DuplicateRequestCount,
                    normalDispatchResult.ExistingSuccessCount
                )
            );
            RefreshThumbnailManualUserActionUiIfAccepted(
                normalDispatchResult.AcceptedCount,
                normalReason
            );
        }

        // 救済の受付が実際に増えた時だけ、一覧全体ではなく関連する表示面へ軽く知らせる。
        private void RefreshThumbnailManualUserActionUiIfAccepted(
            int acceptedOrStartedCount,
            string reason
        )
        {
            if (acceptedOrStartedCount <= 0)
            {
                return;
            }

            InvalidateThumbnailErrorRecords(refreshIfVisible: true);
            RequestUpperTabVisibleRangeRefresh(immediate: true, reason: reason);
            RefreshUpperTabPreferredMoviePathKeysRevision();
            RequestThumbnailErrorSnapshotRefresh();
            RequestThumbnailProgressSnapshotRefresh();
        }

        // 右クリックからも rescue レーンへ送れるようにし、難動画を通常キューへ戻さない。
        private void ThumbnailRescueMenu_Click(object sender, RoutedEventArgs e)
        {
            RunThumbnailRescueMenuAction(
                sender,
                rescueMode: "",
                upperReason: "context-upper-rescue-tab",
                normalReason: "context-manual-rescue",
                toastTitle: "手動救済"
            );
        }

        // 通常一覧から救済タブへ送る時は、いまは rescue 要求を積まずに上側タブだけ開く。
        private async void SendToThumbnailRescueTabMenu_Click(object sender, RoutedEventArgs e)
        {
            const string actionLabel = "サムネ救済タブへ送る";

            if (Tabs.SelectedItem == null)
            {
                ShowThumbnailUserActionPopup(
                    actionLabel,
                    "対象タブを選択してから実行してください。",
                    MessageBoxImage.Warning
                );
                return;
            }

            int targetTabIndex = GetCurrentThumbnailActionTabIndex();
            if (!IsUpperThumbnailTabIndex(targetTabIndex))
            {
                ShowThumbnailUserActionPopup(
                    actionLabel,
                    "処理先のサムネイルタブを特定できませんでした。",
                    MessageBoxImage.Warning
                );
                return;
            }

            List<MovieRecords> records = ResolveSelectedMovieRecordsForThumbnailUserAction(sender);
            MovieRecords firstRecord = NormalizeThumbnailUserActionMovieRecords(records).FirstOrDefault();
            if (firstRecord == null)
            {
                ShowThumbnailUserActionPopup(
                    actionLabel,
                    "対象動画が選択されていません。",
                    MessageBoxImage.Warning
                );
                return;
            }

            RegisterUpperTabRescueManualMoviePaths(records, targetTabIndex);

            // TODO: 必要になったらここで rescue 要求を積めるよう、既存コードは残しておく。
            // ThumbnailRescueUserActionDispatchResult dispatchResult =
            //     DispatchThumbnailRescueUserAction(
            //         records,
            //         new ThumbnailRescueUserActionRequest(
            //             TargetTabIndex: targetTabIndex,
            //             Priority: ThumbnailQueuePriority.Normal,
            //             Reason: "context-send-to-rescue-tab",
            //             UseDedicatedManualWorkerSlot: false,
            //             SkipWhenSuccessExists: false,
            //             RescueMode: "",
            //             DeleteErrorMarkerFirst: true
            //         )
            //     );

            bool openedRescueTab = false;
            try
            {
                await OpenUpperTabRescueForMovieAsync(targetTabIndex, firstRecord.Movie_Path);
                openedRescueTab = true;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "upper-tab-rescue",
                    $"send to rescue tab failed: {ex.GetType().Name}: {ex.Message}"
                );
            }

            ShowThumbnailUserActionPopup(
                actionLabel,
                openedRescueTab
                    ? "サムネ救済タブのリストへ追加しました。"
                    : "サムネ救済タブを開けませんでした。",
                openedRescueTab ? MessageBoxImage.Information : MessageBoxImage.Warning
            );
        }

        // 黒多め背景専用の手動救済は、通常 route に混ぜず明示指定時だけ mode を載せる。
        private void ThumbnailDarkHeavyBackgroundRescueMenu_Click(object sender, RoutedEventArgs e)
        {
            RunThumbnailRescueMenuAction(
                sender,
                rescueMode: "dark-heavy-background",
                upperReason: "context-upper-rescue-tab-dark-heavy-background",
                normalReason: "context-manual-rescue-dark-heavy-background",
                toastTitle: "黒多め背景救済"
            );
        }

        // Lite は near-black 候補を落とし過ぎず、とにかく1枚返す寄りで走らせる。
        private void ThumbnailDarkHeavyBackgroundLiteRescueMenu_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            RunThumbnailRescueMenuAction(
                sender,
                rescueMode: "dark-heavy-background-lite",
                upperReason: "context-upper-rescue-tab-dark-heavy-background-lite",
                normalReason: "context-manual-rescue-dark-heavy-background-lite",
                toastTitle: "黒多め背景救済Lite"
            );
        }

        // 右クリックからも強制 repair 救済へ送れるようにし、救済タブと同じ確認ダイアログを使う。
        private void ThumbnailIndexRepairMenu_Click(object sender, RoutedEventArgs e)
        {
            RunThumbnailIndexRepairMenuAction(
                sender,
                upperReason: "context-upper-rescue-tab-index-rebuild",
                normalReason: "context-manual-rescue-index-rebuild",
                toastTitle: "インデックス再構築"
            );
        }

        // インデックス再構築は重い処理なので、確認後に対象だけを manual slot へ流す。
        private void RunThumbnailIndexRepairMenuAction(
            object sender,
            string upperReason,
            string normalReason,
            string toastTitle
        )
        {
            if (!ConfirmThumbnailIndexRepair())
            {
                return;
            }

            int currentTabIndex = GetCurrentUpperTabFixedIndex();
            int targetTabIndex = GetCurrentThumbnailActionTabIndex();
            DebugRuntimeLog.Write(
                "thumbnail-rescue",
                $"context index repair clicked: tab={targetTabIndex}"
            );

            if (Tabs.SelectedItem == null)
            {
                ShowThumbnailUserActionPopup(
                    toastTitle,
                    "対象タブを選択してから実行してください。",
                    MessageBoxImage.Warning
                );
                return;
            }

            bool isBottomErrorContext = IsThumbnailErrorBottomContextMenuInvocation(sender);
            if (!isBottomErrorContext && currentTabIndex == ThumbnailErrorTabIndex)
            {
                List<MovieRecords> rescueRecords = GetSelectedUpperTabRescueMovieRecords();
                RunThumbnailIndexRepairMenuActionCore(
                    rescueRecords,
                    targetTabIndex,
                    upperReason,
                    toastTitle
                );
                return;
            }

            if (!IsUpperThumbnailTabIndex(targetTabIndex))
            {
                ShowThumbnailUserActionPopup(
                    toastTitle,
                    "処理先のサムネイルタブを特定できませんでした。",
                    MessageBoxImage.Warning
                );
                return;
            }

            List<MovieRecords> records = ResolveSelectedMovieRecordsForThumbnailUserAction(sender);
            RunThumbnailIndexRepairMenuActionCore(
                records,
                targetTabIndex,
                normalReason,
                toastTitle
            );
        }

        // 選択動画を絞り込んで強制 repair 救済へ送り、受付結果だけ UI へ返す。
        private void RunThumbnailIndexRepairMenuActionCore(
            List<MovieRecords> records,
            int targetTabIndex,
            string reason,
            string toastTitle
        )
        {
            List<MovieRecords> normalizedRecords = NormalizeThumbnailUserActionMovieRecords(records);
            if (normalizedRecords.Count == 0)
            {
                ShowThumbnailUserActionPopup(
                    toastTitle,
                    "対象動画が選択されていません。",
                    MessageBoxImage.Warning
                );
                return;
            }

            MovieRecords firstEligibleRecord = normalizedRecords.FirstOrDefault(record =>
                record != null && CanTryThumbnailIndexRepair(record.Movie_Path)
            );
            if (firstEligibleRecord == null)
            {
                ShowThumbnailUserActionPopup(
                    toastTitle,
                    "インデックス再構築対象の動画がありません。",
                    MessageBoxImage.Warning
                );
                return;
            }

            RememberManualThumbnailRescueMoviePath(firstEligibleRecord.Movie_Path);
            ReportManualThumbnailRescueProgress(
                BuildManualThumbnailRescueModeProgressMessage("force-index-repair"),
                true
            );
            ThumbnailDirectIndexRepairDispatchResult dispatchResult =
                DispatchThumbnailDirectIndexRepairUserAction(
                    normalizedRecords,
                    targetTabIndex,
                    reason
                );

            DebugRuntimeLog.Write(
                "thumbnail-rescue",
                $"context index repair direct end: tab={targetTabIndex} selected={dispatchResult.SelectedCount} started={dispatchResult.StartedCount} busy={dispatchResult.BusyCount} unsupported={dispatchResult.UnsupportedCount}"
            );

            ShowThumbnailUserActionPopup(
                toastTitle,
                BuildThumbnailIndexRepairUserActionPopupMessage(
                    dispatchResult.SelectedCount,
                    dispatchResult.StartedCount,
                    dispatchResult.BusyCount,
                    dispatchResult.UnsupportedCount
                ),
                dispatchResult.StartedCount > 0
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Warning
            );

            RefreshThumbnailManualUserActionUiIfAccepted(
                dispatchResult.StartedCount,
                reason
            );
        }

        // 進捗表示だけは mode 名を短い文へ変換し、手動操作の意図を UI に返す。
        private static string BuildManualThumbnailRescueModeProgressMessage(string rescueMode)
        {
            return string.Equals(
                rescueMode,
                "dark-heavy-background",
                StringComparison.OrdinalIgnoreCase
            )
                ? "黒多め背景救済を登録中です。"
                : string.Equals(
                    rescueMode,
                "dark-heavy-background-lite",
                StringComparison.OrdinalIgnoreCase
            )
                    ? "黒多め背景救済Liteを登録中です。"
                : string.Equals(
                    rescueMode,
                    "force-index-repair",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? "インデックス再構築を開始中です。"
                : "救済要求を登録中です。";
        }

        // 救済タブと右クリックで同じ確認文言を使い、意図の差分をなくす。
        private static bool ConfirmThumbnailIndexRepair()
        {
            MessageBoxResult confirmResult = MessageBox.Show(
                "動画を別名でコピーしてインデックスを再生します。　シークが出来ない動画を復旧できる可能性が有ります",
                "インデックス再構築",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information
            );
            return confirmResult == MessageBoxResult.OK;
        }

        // duplicate / 既存成功を1本の短い案内へまとめ、手動救済の反応を必ず返す。
        private static string BuildManualThumbnailRescueSkipMessage(
            int duplicateRequestCount,
            int existingSuccessCount
        )
        {
            if (duplicateRequestCount > 0)
            {
                return duplicateRequestCount == 1
                    ? "同じ動画は既に救済中、または救済待ちです。"
                    : $"{duplicateRequestCount}件は既に救済中、または救済待ちです。";
            }

            if (existingSuccessCount > 0)
            {
                return existingSuccessCount == 1
                    ? "既に正常サムネイルがあります。"
                    : $"{existingSuccessCount}件は既に正常サムネイルがあります。";
            }

            return "救済要求は受け付けられませんでした。";
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void BtnNew_Click(object sender, RoutedEventArgs e)
        {
            await TryCreateMainDbFromDialogAsync();
        }

        // .wb 新規作成ダイアログを共通化し、ドロップ導線からも同じ処理を再利用する。
        private async Task<bool> TryCreateMainDbFromDialogAsync()
        {
            var sfd = new SaveFileDialog
            {
                InitialDirectory = GetNewMainDbDialogInitialDirectory(),
                RestoreDirectory = true,
                Filter = "設定ファイル(*.wb)|*.wb|すべてのファイル(*.*)|*.*",
                FilterIndex = 1,
                Title = "設定ファイル(.wb）の選択",
                OverwritePrompt = false,
            };

            var result = sfd.ShowDialog();
            if (result == true)
            {
                string dbFullPathBeforeCreate = MainVM?.DbInfo?.DBFullPath ?? "";
                string dbFullPathSnapshot = sfd.FileName;
                RememberMainDbDialogDirectory(dbFullPathSnapshot);

                MainDbCreateDialogBackgroundResult createResult =
                    await CreateMainDbFromDialogInBackgroundAsync(dbFullPathSnapshot);

                if (createResult.Status == MainDbCreateDialogBackgroundStatus.AlreadyExists)
                {
                    MessageBox.Show(
                        $"{dbFullPathSnapshot}は既に存在します。",
                        "新規作成",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                    return false;
                }

                if (createResult.Status == MainDbCreateDialogBackgroundStatus.Failed)
                {
                    MessageBox.Show(
                        this,
                        $"新規DBを作成できませんでした。\n{createResult.ErrorMessage}",
                        Assembly.GetExecutingAssembly().GetName().Name,
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return false;
                }

                if (
                    !AreSameMainDbPath(
                        dbFullPathBeforeCreate,
                        MainVM?.DbInfo?.DBFullPath ?? ""
                    )
                )
                {
                    DebugRuntimeLog.Write(
                        "ui-tempo",
                        $"new main db switch skipped: reason=db-changed created='{dbFullPathSnapshot}'"
                    );
                    return false;
                }

                return await TrySwitchMainDb(dbFullPathSnapshot, MainDbSwitchSource.New);
            }

            return false;
        }

        // 新規DB作成のファイル存在確認とSQLite初期化は、遅い媒体でUIを塞がないよう背景へ逃がす。
        private static Task<MainDbCreateDialogBackgroundResult> CreateMainDbFromDialogInBackgroundAsync(
            string dbFullPath
        )
        {
            string dbFullPathSnapshot = dbFullPath ?? "";
            return Task.Run(() =>
            {
                if (Path.Exists(dbFullPathSnapshot))
                {
                    return new MainDbCreateDialogBackgroundResult
                    {
                        Status = MainDbCreateDialogBackgroundStatus.AlreadyExists,
                    };
                }

                if (!TryCreateDatabase(dbFullPathSnapshot, out string createError))
                {
                    return new MainDbCreateDialogBackgroundResult
                    {
                        Status = MainDbCreateDialogBackgroundStatus.Failed,
                        ErrorMessage = createError,
                    };
                }

                return new MainDbCreateDialogBackgroundResult
                {
                    Status = MainDbCreateDialogBackgroundStatus.Created,
                };
            });
        }

        /// <summary>
        /// 最近使ったファイル履歴（Recent）を先頭優先で再構築する！新しい順に並べて使いやすさをグッと押し上げるぜ！🔄
        /// </summary>
        private void ReStackRecentTree(string newItem)
        {
            var rootItem = MainVM.RecentTreeRoot[0];
            Stack<string> temp = new();

            foreach (var item in recentFiles.Reverse())
            {
                if (item != newItem)
                {
                    temp.Push(item);
                }
            }
            recentFiles.Clear();
            recentFiles = temp;

            while (recentFiles.Count + 1 > Properties.Settings.Default.RecentFilesCount)
            {
                recentFiles = new Stack<string>(recentFiles.Reverse().Skip(1));
            }

            recentFiles.Push(newItem);
            rootItem.Children?.Clear();

            foreach (var item in recentFiles)
            {
                var childItem = new TreeSource { Text = item, IsExpanded = false };
                rootItem.Add(childItem);
            }
        }

        private async void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            await TryOpenMainDbFromDialogAsync();
        }

        // .wb 選択ダイアログを共通化し、ドロップ導線からも同じ処理を再利用する。
        private async Task<bool> TryOpenMainDbFromDialogAsync()
        {
            var ofd = new OpenFileDialog
            {
                InitialDirectory = GetMainDbDialogInitialDirectory(),
                RestoreDirectory = true,
                Filter = "設定ファイル(*.wb)|*.wb|すべてのファイル(*.*)|*.*",
                FilterIndex = 1,
                Multiselect = false,
                Title = "設定ファイル(.wb）の選択",
            };

            var result = ofd.ShowDialog();

            if (result == true)
            {
                RememberMainDbDialogDirectory(ofd.FileName);
                return await TrySwitchMainDb(ofd.FileName, MainDbSwitchSource.OpenDialog);
            }

            return false;
        }

        /// <summary>
        /// 開発者用テストボタン！各表示部材を手動で強制リロードする禁断の力だ！🔧
        /// </summary>
        private async void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteHeaderReloadAsync(MainVM.DbInfo.Sort, "Header.ReloadButton");
        }

        // 再読込は full filter と manual scan を直列化し、その間の watch 差し込みを抑えて過積載を避ける。
        internal async Task ExecuteHeaderReloadAsync(string sortId, string trigger)
        {
            Stopwatch reloadStopwatch = Stopwatch.StartNew();
            const string fullReloadReason = "header-explicit";
            string reloadId = CreateHeaderReloadLogCorrelationId();
            bool externalSkinRefreshQueued = false;
            bool deferredScanScheduled = false;
            bool watchUiSuppressionStarted = false;

            DebugRuntimeLog.Write(
                "ui-tempo",
                $"header reload begin: reload_id={reloadId} trigger={trigger} sort={sortId} full_reload_reason={fullReloadReason}"
            );

            try
            {
                Action reloadBookmarkHook = ReloadBookmarkTabDataForTesting;
                if (reloadBookmarkHook != null)
                {
                    reloadBookmarkHook();
                }
                else
                {
                    ReloadBookmarkTabData();
                }

                BeginWatchUiSuppression("manual-reload");
                watchUiSuppressionStarted = true;

                Func<string, bool, Task> filterHook = FilterAndSortAsyncForTesting;
                if (filterHook != null)
                {
                    await filterHook(sortId, true);
                }
                else
                {
                    await FilterAndSortAsync(sortId, true);
                }

                if (GetCurrentExternalSkinDefinition() != null)
                {
                    // 共通ヘッダーの再読込でも外部 skin host を明示的に積み直し、旧専用ヘッダー依存を残さない。
                    await ClearExternalSkinHostBeforeRefreshAsync("header-reload");
                    externalSkinRefreshQueued = QueueExternalSkinHostRefresh("header-reload");
                }

                // 再読込完了を先に返し、重い全域scanはUIが一息ついてから背後へ回す。
                ScheduleDeferredManualReloadScan(trigger, reloadId);
                deferredScanScheduled = true;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"header reload failed: reload_id={reloadId} trigger={trigger} sort={sortId} full_reload_reason={fullReloadReason} external_skin_refresh_queued={FormatRuntimeLogBool(externalSkinRefreshQueued)} deferred_scan_scheduled={FormatRuntimeLogBool(deferredScanScheduled)} elapsed_ms={reloadStopwatch.ElapsedMilliseconds} type={ex.GetType().Name} reason='{ex.Message}'"
                );
                throw;
            }
            finally
            {
                if (watchUiSuppressionStarted)
                {
                    EndWatchUiSuppression("manual-reload");
                }
            }

            DebugRuntimeLog.Write(
                "ui-tempo",
                $"header reload end: reload_id={reloadId} trigger={trigger} sort={sortId} full_reload_reason={fullReloadReason} external_skin_refresh_queued={FormatRuntimeLogBool(externalSkinRefreshQueued)} deferred_scan_scheduled={FormatRuntimeLogBool(deferredScanScheduled)} elapsed_ms={reloadStopwatch.ElapsedMilliseconds}"
            );
        }

        // 1回のHeader再読込から後続scanまでを、短いIDで同じ流れとして追えるようにする。
        internal static string CreateHeaderReloadLogCorrelationId()
        {
            return $"hr{System.Guid.NewGuid():N}".Substring(0, 10);
        }

        private static string FormatRuntimeLogBool(bool value)
        {
            return value ? "true" : "false";
        }

        // Header再読込の直後だけは一覧更新の体感を優先し、全域scanは1拍後ろへ逃がす。
        private void ScheduleDeferredManualReloadScan(string trigger, string reloadId)
        {
            int scanRevision = System.Threading.Interlocked.Increment(
                ref _deferredManualReloadScanRevision
            );
            _ = RunDeferredManualReloadScanAsync(trigger, reloadId, scanRevision);
        }

        private async Task RunDeferredManualReloadScanAsync(
            string trigger,
            string reloadId,
            int scanRevision
        )
        {
            try
            {
                if (IsDeferredManualReloadScanSuperseded(scanRevision))
                {
                    LogDeferredManualReloadScanSkipped(trigger, reloadId, "superseded");
                    return;
                }

                if (TryGetDeferredManualReloadScanSkipReason(out string skipReason))
                {
                    LogDeferredManualReloadScanSkipped(trigger, reloadId, skipReason);
                    return;
                }

                DebugRuntimeLog.Write(
                    "watch-check",
                    $"manual reload deferred scan scheduled: reload_id={reloadId} trigger={trigger}"
                );
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                await Task.Delay(250);

                if (IsDeferredManualReloadScanSuperseded(scanRevision))
                {
                    LogDeferredManualReloadScanSkipped(trigger, reloadId, "superseded");
                    return;
                }

                if (TryGetDeferredManualReloadScanSkipReason(out skipReason))
                {
                    LogDeferredManualReloadScanSkipped(trigger, reloadId, skipReason);
                    return;
                }

                if (IsDeferredManualReloadScanSuperseded(scanRevision))
                {
                    LogDeferredManualReloadScanSkipped(trigger, reloadId, "superseded");
                    return;
                }

                await QueueCheckFolderAsync(CheckMode.Manual, $"{trigger}:deferred");
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"manual reload deferred scan failed: reload_id={reloadId} trigger={trigger} type={ex.GetType().Name} origin={GetDeferredManualReloadScanFailureOrigin(ex)} reason='{ex.Message}'"
                );
            }
        }

        private bool IsDeferredManualReloadScanSuperseded(int scanRevision)
        {
            return IsDeferredManualReloadScanSuperseded(
                scanRevision,
                System.Threading.Volatile.Read(ref _deferredManualReloadScanRevision)
            );
        }

        internal static bool IsDeferredManualReloadScanSuperseded(
            int scanRevision,
            int latestScanRevision
        )
        {
            return scanRevision != latestScanRevision;
        }

        private bool TryGetDeferredManualReloadScanSkipReason(out string reason)
        {
            return TryGetDeferredManualReloadScanSkipReason(
                Dispatcher,
                MainVM,
                _checkFolderRequestSync,
                out reason
            );
        }

        internal static bool TryGetDeferredManualReloadScanSkipReason(
            System.Windows.Threading.Dispatcher dispatcher,
            MainWindowViewModel mainVM,
            object checkFolderRequestSync,
            out string reason
        )
        {
            if (dispatcher == null)
            {
                reason = "dispatcher-null";
                return true;
            }

            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                reason = "dispatcher-shutdown";
                return true;
            }

            if (mainVM == null)
            {
                reason = "main-vm-null";
                return true;
            }

            if (mainVM.DbInfo == null)
            {
                reason = "db-info-null";
                return true;
            }

            if (string.IsNullOrWhiteSpace(mainVM.DbInfo.DBFullPath))
            {
                reason = "db-path-empty";
                return true;
            }

            if (checkFolderRequestSync == null)
            {
                // Queue入口のロックが無い状態では、背後scanを積まずに原因だけ残す。
                reason = "queue-not-initialized";
                return true;
            }

            reason = "";
            return false;
        }

        private static void LogDeferredManualReloadScanSkipped(
            string trigger,
            string reloadId,
            string reason
        )
        {
            DebugRuntimeLog.Write(
                "watch-check",
                $"manual reload deferred scan skipped: reload_id={reloadId} trigger={trigger} reason={reason}"
            );
        }

        private static string GetDeferredManualReloadScanFailureOrigin(Exception ex)
        {
            StackFrame frame = new StackTrace(ex, fNeedFileInfo: false).GetFrame(0);
            string methodName = frame?.GetMethod()?.Name ?? "unknown";
            return methodName;
        }

        private void MenuBtnSettings_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button item)
            {
                if (!string.IsNullOrEmpty(item.Tag.ToString()))
                {
                    var tag = item.Tag.ToString();
                    if (tag != "設定")
                    {
                        switch (tag)
                        {
                            case "共通設定":
                                MenuToggleButton.IsChecked = false;
                                var commonSettingsWindow = new CommonSettingsWindow
                                {
                                    Owner = this,
                                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                                };
                                commonSettingsWindow.ShowDialog();
                                ApplyThumbnailGpuDecodeSetting();
                                break;
                            case "個別設定":
                                if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
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
                                var sysData = new DbSettings(MainVM.DbInfo.DBFullPath);
                                var settingsWindow = new SettingsWindow
                                {
                                    Owner = this,
                                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                                    DataContext = sysData,
                                };
                                settingsWindow.ShowDialog();
                                int persistedSettingsCount = PersistDbSettingsValues(
                                    MainVM.DbInfo.DBFullPath,
                                    settingsWindow.ThumbFolder.Text,
                                    settingsWindow.BookmarkFolder.Text,
                                    settingsWindow.KeepHistory.Text,
                                    settingsWindow.PlayerPrg.Text,
                                    settingsWindow.PlayerParam.Text?.ToString() ?? ""
                                );
                                if (persistedSettingsCount != 5)
                                {
                                    DebugRuntimeLog.Write(
                                        "skin-db",
                                        $"settings persist partial: success={persistedSettingsCount}/5 db='{MainVM.DbInfo.DBFullPath}'"
                                    );
                                }
                                break;
                            default:
                                break;
                        }
                    }
                    else
                    {
                        if (MenuConfig.Items.Count > 0)
                        {
                            if (MenuConfig.Items[0] is TreeSource topNode)
                            {
                                topNode.IsExpanded = !topNode.IsExpanded;
                            }
                        }
                    }
                }
            }
        }

        private int PersistDbSettingsValues(
            string dbFullPath,
            string thumbFolder,
            string bookmarkFolder,
            string keepHistory,
            string playerPrg,
            string playerParam
        )
        {
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return 0;
            }

            // 個別設定画面の各入力を、UI からはまとめて保存要求するだけに寄せる。
            int persistedCount = 0;

            persistedCount += TryPersistSystemValue(dbFullPath, "thum", thumbFolder ?? "") ? 1 : 0;
            persistedCount += TryPersistSystemValue(dbFullPath, "bookmark", bookmarkFolder ?? "") ? 1 : 0;
            persistedCount += TryPersistSystemValue(dbFullPath, "keepHistory", keepHistory ?? "") ? 1 : 0;
            persistedCount += TryPersistSystemValue(dbFullPath, "playerPrg", playerPrg ?? "") ? 1 : 0;
            persistedCount += TryPersistSystemValue(dbFullPath, "playerParam", playerParam ?? "") ? 1 : 0;

            return persistedCount;
        }

        private void MenuBtnTool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button item)
            {
                if (!string.IsNullOrEmpty(item.Tag.ToString()))
                {
                    var tag = item.Tag.ToString();
                    if (tag != "ツール")
                    {
                        if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
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

                        switch (tag)
                        {
                            case "監視フォルダ編集":
                                OpenWatchFolderEditorDialog();
                                break;

                            case "監視フォルダ更新チェック":
                                _ = QueueCheckFolderAsync(
                                    CheckMode.Manual,
                                    "Menu.ManualWatchCheck"
                                );
                                break;

                            case "全ファイルサムネイル再作成":
                                _ = QueueRecreateAllThumbnailsFromCurrentTab(closeMenu: false);
                                break;
                            default:
                                break;
                        }
                    }
                    else
                    {
                        if (MenuTool.Items.Count > 0)
                        {
                            if (MenuTool.Items[0] is TreeSource topNode)
                            {
                                topNode.IsExpanded = !topNode.IsExpanded;
                            }
                        }
                    }
                }
            }
        }

        private async void MenuRecentTree_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button item)
            {
                if (!string.IsNullOrEmpty(item.Tag.ToString()))
                {
                    var tag = item.Tag.ToString();
                    if (tag != RECENT_OPEN_FILE_LABEL)
                    {
                        await TrySwitchMainDb(tag, MainDbSwitchSource.RecentMenu);
                    }
                    else
                    {
                        if (MenuRecent.Items.Count > 0)
                        {
                            if (MenuRecent.Items[0] is TreeSource topNode)
                            {
                                topNode.IsExpanded = !topNode.IsExpanded;
                            }
                        }
                    }
                }
            }
        }
    }
}
