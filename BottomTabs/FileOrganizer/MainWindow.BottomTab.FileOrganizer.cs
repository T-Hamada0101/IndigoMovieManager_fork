using System.Collections.Specialized;
using System.Data;
using System.Windows;
using System.Windows.Input;
using IndigoMovieManager.BottomTabs.FileOrganizer;
using IndigoMovieManager.DB;
using Microsoft.Win32;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int FileOrganizerDestinationCount = 9;
        private FileOrganizerDestinationItem[] _fileOrganizerDestinations = [];
        private MovieRecords _fileOrganizerDisplayedMovie;

        private void InitializeFileOrganizerTabSupport()
        {
            _fileOrganizerDestinations = Enumerable
                .Range(1, FileOrganizerDestinationCount)
                .Select(number => new FileOrganizerDestinationItem(number))
                .ToArray();

            StringCollection savedDestinations = Properties.Settings.Default.FileOrganizerDestinations;
            for (int index = 0; index < _fileOrganizerDestinations.Length; index++)
            {
                _fileOrganizerDestinations[index].FolderPath =
                    savedDestinations != null && index < savedDestinations.Count
                        ? savedDestinations[index] ?? ""
                        : "";
            }

            FileOrganizerTabViewHost.SetItems(_fileOrganizerDestinations);
            FileOrganizerTabViewHost.RegisterRequested += FileOrganizerRegisterRequested;
            FileOrganizerTabViewHost.ClearRequested += FileOrganizerClearRequested;
            FileOrganizerTabViewHost.MoveAllRequested += FileOrganizerMoveAllRequested;
            FileOrganizerTabViewHost.DetailActionRequested += FileOrganizerDetailActionRequested;
            FileOrganizerTabViewHost.SetSelectedMovie(null);
        }

        // メインタブの現在選択1件を、ショートカットの移動対象として左側へ同期する。
        private void RefreshFileOrganizerDisplayedMovie()
        {
            _fileOrganizerDisplayedMovie = GetSelectedItemByTabIndex();
            FileOrganizerTabViewHost?.SetSelectedMovie(_fileOrganizerDisplayedMovie);
        }

        private async void FileOrganizerRegisterRequested(
            object sender,
            FileOrganizerSlotEventArgs e
        )
        {
            FileOrganizerDestinationItem item = ResolveFileOrganizerDestination(
                e?.ShortcutNumber ?? 0
            );
            if (item == null)
            {
                return;
            }

            OpenFolderDialog dialog = new()
            {
                Title = $"Ctrl+{item.ShortcutNumber} の移動先を登録",
                Multiselect = false,
                AddToRecent = true,
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            item.FolderPath = dialog.FolderName;
            PersistFileOrganizerDestinations("file-organizer-register");
            FileOrganizerTabViewHost.SetStatus(
                $"Ctrl+{item.ShortcutNumber} の移動先を登録しました。"
            );

            await PromptToAddFileOrganizerWatchFolderIfNeededAsync(item.FolderPath);
        }

        private void FileOrganizerClearRequested(object sender, FileOrganizerSlotEventArgs e)
        {
            FileOrganizerDestinationItem item = ResolveFileOrganizerDestination(
                e?.ShortcutNumber ?? 0
            );
            if (item == null)
            {
                return;
            }

            item.IsShortcutEnabled = false;
            item.FolderPath = "";
            PersistFileOrganizerDestinations("file-organizer-clear");
            FileOrganizerTabViewHost.SetStatus(
                $"Ctrl+{item.ShortcutNumber} の移動先を解除しました。"
            );
        }

        private void FileOrganizerMoveAllRequested(object sender, FileOrganizerSlotEventArgs e)
        {
            FileOrganizerDestinationItem destination = ResolveFileOrganizerDestination(
                e?.ShortcutNumber ?? 0
            );
            if (destination == null || string.IsNullOrWhiteSpace(destination.FolderPath))
            {
                return;
            }

            List<MovieRecords> displayedMovies = (MainVM?.FilteredMovieRecs ?? [])
                .Where(movie => movie != null && !string.IsNullOrWhiteSpace(movie.Movie_Path))
                .ToList();
            if (displayedMovies.Count == 0)
            {
                FileOrganizerTabViewHost.SetStatus("現在のメインタブに移動対象がありません。");
                return;
            }

            _fileOrganizerDisplayedMovie = displayedMovies[0];
            FileOrganizerTabViewHost.SetSelectedMovie(
                _fileOrganizerDisplayedMovie,
                displayedMovies.Count
            );
            if (!ConfirmAndQueueFileOrganizerMove(
                displayedMovies,
                destination,
                operationLabel: "全移動"
            ))
            {
                RefreshFileOrganizerDisplayedMovie();
            }
        }

        private void FileOrganizerDetailActionRequested(
            object sender,
            FileOrganizerDetailActionEventArgs e
        )
        {
            MovieRecords movie = _fileOrganizerDisplayedMovie;
            if (movie == null)
            {
                FileOrganizerTabViewHost.SetStatus("メインタブで動画を選択してください。");
                return;
            }

            switch (e?.Action)
            {
                case FileOrganizerDetailAction.CopyMoviePath:
                    CopyFileOrganizerText(movie.Movie_Path, "ファイルパスをコピーしました。");
                    break;
                case FileOrganizerDetailAction.CopyFolderPath:
                    CopyFileOrganizerText(movie.Dir, "フォルダパスをコピーしました。");
                    break;
                case FileOrganizerDetailAction.OpenFolder:
                    QueueOpenParentFolderExplorer(movie.Movie_Path, movie.Dir);
                    FileOrganizerTabViewHost.SetStatus("親フォルダを開きます。");
                    break;
            }
        }

        private void CopyFileOrganizerText(string text, string successMessage)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            Clipboard.SetText(text);
            FileOrganizerTabViewHost.SetStatus(successMessage);
        }

        private void PersistFileOrganizerDestinations(string reason)
        {
            StringCollection destinations = new();
            foreach (FileOrganizerDestinationItem item in _fileOrganizerDestinations)
            {
                destinations.Add(item.FolderPath ?? "");
            }

            Properties.Settings.Default.FileOrganizerDestinations = destinations;
            QueueApplicationSettingsSave(reason);
        }

        private FileOrganizerDestinationItem ResolveFileOrganizerDestination(int shortcutNumber)
        {
            return _fileOrganizerDestinations.FirstOrDefault(item =>
                item.ShortcutNumber == shortcutNumber
            );
        }

        // 対象行ONの時だけCtrl+1～9を受理し、左側の詳細1件を移動する。
        private bool TryHandleFileOrganizerShortcut(KeyEventArgs e)
        {
            if (e == null)
            {
                return false;
            }

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            int shortcutNumber = FileOrganizerShortcutPolicy.ResolveShortcutNumber(
                key,
                Keyboard.Modifiers,
                isFileOrganizerActive: true
            );
            if (shortcutNumber == 0)
            {
                return false;
            }

            FileOrganizerDestinationItem destination = ResolveFileOrganizerDestination(
                shortcutNumber
            );
            if (destination == null || !destination.IsShortcutActive)
            {
                return false;
            }

            e.Handled = true;
            RefreshFileOrganizerDisplayedMovie();
            if (_fileOrganizerDisplayedMovie == null)
            {
                FileOrganizerTabViewHost.SetStatus("メインタブで移動する動画を選択してください。");
                return true;
            }

            ConfirmAndQueueFileOrganizerMove(
                [_fileOrganizerDisplayedMovie],
                destination,
                operationLabel: destination.ShortcutLabel
            );
            return true;
        }

        private bool ConfirmAndQueueFileOrganizerMove(
            IReadOnlyList<MovieRecords> movies,
            FileOrganizerDestinationItem destination,
            string operationLabel
        )
        {
            MovieRecords[] targets = (movies ?? [])
                .Where(movie => movie != null && !string.IsNullOrWhiteSpace(movie.Movie_Path))
                .ToArray();
            if (targets.Length == 0 || destination == null)
            {
                return false;
            }

            MessageBoxResult result = MessageBox.Show(
                FileOrganizerMoveConfirmationPolicy.BuildMessage(
                    targets[0].Movie_Path,
                    targets.Length,
                    destination.FolderPath
                ),
                $"ファイル整理 - {operationLabel}",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No
            );
            if (result != MessageBoxResult.Yes)
            {
                FileOrganizerTabViewHost.SetStatus("移動をキャンセルしました。");
                return false;
            }

            QueueMovieFileMove(targets, destination.FolderPath);
            FileOrganizerTabViewHost.SetStatus(
                $"{targets.Length} 件を {destination.ShortcutLabel} の登録先へ移動開始しました。"
            );
            return true;
        }

        private async Task PromptToAddFileOrganizerWatchFolderIfNeededAsync(string folderPath)
        {
            string dbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(dbFullPath) || string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            bool isCovered;
            try
            {
                isCovered = await Task.Run(() => IsFileOrganizerFolderCoveredByWatch(dbFullPath, folderPath));
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "file-organizer",
                    $"watch coverage check failed: db='{dbFullPath}' folder='{folderPath}' err='{ex.GetType().Name}: {ex.Message}'"
                );
                return;
            }

            if (
                isCovered
                || !AreSameMainDbPath(dbFullPath, MainVM?.DbInfo?.DBFullPath ?? "")
                || Dispatcher.HasShutdownStarted
                || Dispatcher.HasShutdownFinished
            )
            {
                return;
            }

            MessageBoxResult addResult = MessageBox.Show(
                $"登録した移動先は現在の監視対象外です。\n\n{folderPath}\n\n監視フォルダへ追加しますか？",
                "ファイル整理 - 監視フォルダ確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.Yes
            );
            if (addResult != MessageBoxResult.Yes)
            {
                FileOrganizerTabViewHost.SetStatus("移動先を登録しました。監視フォルダには追加していません。");
                return;
            }

            await QueueDroppedWatchFoldersAsync(dbFullPath, [folderPath]);
            FileOrganizerTabViewHost.SetStatus("移動先を登録し、監視フォルダへ追加しました。");
        }

        private static bool IsFileOrganizerFolderCoveredByWatch(
            string dbFullPath,
            string folderPath
        )
        {
            DataTable currentWatchData = SQLite.GetData(dbFullPath, "SELECT * FROM watch");
            WatchTableRowNormalizer.Normalize(currentWatchData);
            FileOrganizerWatchFolder[] watchFolders = currentWatchData
                .AsEnumerable()
                .Select(row =>
                    new FileOrganizerWatchFolder(
                        row["dir"]?.ToString() ?? "",
                        Convert.ToInt64(row["watch"]) == 1,
                        Convert.ToInt64(row["sub"]) == 1
                    )
                )
                .ToArray();
            return FileOrganizerWatchCoveragePolicy.IsCovered(folderPath, watchFolders);
        }
    }
}
