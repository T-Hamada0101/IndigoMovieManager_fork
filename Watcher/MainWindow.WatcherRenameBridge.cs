using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using IndigoMovieManager.Thumbnail;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    internal static class ThumbnailRenameAssetTransferHelper
    {
        internal sealed class ThumbnailPathSnapshot
        {
            public string ThumbPathSmall { get; init; } = "";
            public string ThumbPathBig { get; init; } = "";
            public string ThumbPathGrid { get; init; } = "";
            public string ThumbPathList { get; init; } = "";
            public string ThumbPathBig10 { get; init; } = "";
            public string ThumbDetail { get; init; } = "";
        }

        internal sealed class ThumbnailRenameFileOperation
        {
            public string SourcePath { get; init; } = "";
            public string DestinationPath { get; init; } = "";
        }

        internal static ThumbnailPathSnapshot CreatePathSnapshot(MovieRecords movie)
        {
            return new ThumbnailPathSnapshot
            {
                ThumbPathSmall = movie?.ThumbPathSmall ?? "",
                ThumbPathBig = movie?.ThumbPathBig ?? "",
                ThumbPathGrid = movie?.ThumbPathGrid ?? "",
                ThumbPathList = movie?.ThumbPathList ?? "",
                ThumbPathBig10 = movie?.ThumbPathBig10 ?? "",
                ThumbDetail = movie?.ThumbDetail ?? "",
            };
        }

        // 現在表示中の実サムネと、旧命名で残っているjpgをまとめて新名へ寄せる。
        internal static void RenameThumbnailFiles(
            MovieRecords movie,
            string thumbnailRoot,
            string oldFullPath,
            string newFullPath
        )
        {
            IReadOnlyList<ThumbnailRenameFileOperation> operations = RenameThumbnailFiles(
                CreatePathSnapshot(movie),
                thumbnailRoot,
                oldFullPath,
                newFullPath
            );
            ApplyRenamedThumbnailPaths(movie, operations);
        }

        // ファイル確認と再帰列挙は背景側で行えるよう、UIオブジェクトではなくパスsnapshotだけを見る。
        internal static IReadOnlyList<ThumbnailRenameFileOperation> RenameThumbnailFiles(
            ThumbnailPathSnapshot snapshot,
            string thumbnailRoot,
            string oldFullPath,
            string newFullPath
        )
        {
            List<ThumbnailRenameFileOperation> operations = [];
            if (snapshot == null || string.IsNullOrWhiteSpace(thumbnailRoot) || !Directory.Exists(thumbnailRoot))
            {
                return operations;
            }

            foreach (
                string sourcePath in EnumerateThumbnailSourcePaths(
                    snapshot,
                    thumbnailRoot,
                    oldFullPath
                )
            )
            {
                string destinationPath = TryBuildRenamedThumbnailPath(
                    sourcePath,
                    oldFullPath,
                    newFullPath
                );
                if (string.IsNullOrWhiteSpace(destinationPath))
                {
                    continue;
                }

                if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
                {
                    operations.Add(
                        new ThumbnailRenameFileOperation
                        {
                            SourcePath = sourcePath,
                            DestinationPath = destinationPath,
                        }
                    );
                    continue;
                }

                FileInfo thumbnailFile = new(sourcePath);
                if (!thumbnailFile.Exists)
                {
                    continue;
                }

                thumbnailFile.MoveTo(destinationPath, true);
                if (!ThumbnailPathResolver.IsErrorMarker(destinationPath))
                {
                    ThumbnailPathResolver.RememberSuccessThumbnailPath(destinationPath);
                }

                operations.Add(
                    new ThumbnailRenameFileOperation
                    {
                        SourcePath = sourcePath,
                        DestinationPath = destinationPath,
                    }
                );
            }

            return operations;
        }

        internal static string TryBuildRenamedThumbnailPath(
            string sourcePath,
            string oldFullPath,
            string newFullPath
        )
        {
            if (
                string.IsNullOrWhiteSpace(sourcePath)
                || string.IsNullOrWhiteSpace(oldFullPath)
                || string.IsNullOrWhiteSpace(newFullPath)
            )
            {
                return "";
            }

            string oldBody = Path.GetFileNameWithoutExtension(oldFullPath) ?? "";
            string newBody = Path.GetFileNameWithoutExtension(newFullPath) ?? "";
            if (string.IsNullOrWhiteSpace(oldBody) || string.IsNullOrWhiteSpace(newBody))
            {
                return "";
            }

            string directoryPath = Path.GetDirectoryName(sourcePath) ?? "";
            string extension = Path.GetExtension(sourcePath) ?? "";
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(sourcePath) ?? "";
            if (string.IsNullOrWhiteSpace(directoryPath) || string.IsNullOrWhiteSpace(extension))
            {
                return "";
            }

            string renamedFileNameWithoutExtension;
            if (string.Equals(fileNameWithoutExtension, oldBody, StringComparison.OrdinalIgnoreCase))
            {
                renamedFileNameWithoutExtension = newBody;
            }
            else if (
                fileNameWithoutExtension.StartsWith(
                    oldBody + ".#",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                renamedFileNameWithoutExtension =
                    newBody + fileNameWithoutExtension[oldBody.Length..];
            }
            else
            {
                return "";
            }

            return Path.Combine(directoryPath, renamedFileNameWithoutExtension + extension);
        }

        // まず表示中のパスを尊重し、その後で旧命名の取りこぼしだけを追加で拾う。
        private static IEnumerable<string> EnumerateThumbnailSourcePaths(
            ThumbnailPathSnapshot snapshot,
            string thumbnailRoot,
            string oldFullPath
        )
        {
            HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
            string oldBody = Path.GetFileNameWithoutExtension(oldFullPath) ?? "";

            TryAddThumbnailPath(paths, snapshot.ThumbPathSmall, thumbnailRoot, oldBody);
            TryAddThumbnailPath(paths, snapshot.ThumbPathBig, thumbnailRoot, oldBody);
            TryAddThumbnailPath(paths, snapshot.ThumbPathGrid, thumbnailRoot, oldBody);
            TryAddThumbnailPath(paths, snapshot.ThumbPathList, thumbnailRoot, oldBody);
            TryAddThumbnailPath(paths, snapshot.ThumbPathBig10, thumbnailRoot, oldBody);
            TryAddThumbnailPath(paths, snapshot.ThumbDetail, thumbnailRoot, oldBody);

            if (string.IsNullOrWhiteSpace(oldBody))
            {
                return paths;
            }

            DirectoryInfo thumbnailRootDirectory = new(thumbnailRoot);
            EnumerationOptions enumerationOptions = new() { RecurseSubdirectories = true };
            foreach (string searchPattern in EnumerateLegacySearchPatterns(oldBody))
            {
                foreach (
                    FileInfo thumbnailFile in thumbnailRootDirectory.EnumerateFiles(
                        searchPattern,
                        enumerationOptions
                    )
                )
                {
                    paths.Add(thumbnailFile.FullName);
                }
            }

            return paths;
        }

        private static IEnumerable<string> EnumerateLegacySearchPatterns(string oldBody)
        {
            yield return oldBody + ".jpg";
            yield return oldBody + ".#*.jpg";
        }

        private static void TryAddThumbnailPath(
            ISet<string> target,
            string thumbnailPath,
            string thumbnailRoot,
            string oldBody
        )
        {
            if (
                string.IsNullOrWhiteSpace(thumbnailPath)
                || string.IsNullOrWhiteSpace(thumbnailRoot)
                || string.IsNullOrWhiteSpace(oldBody)
                || !File.Exists(thumbnailPath)
            )
            {
                return;
            }

            if (!IsPathUnderRoot(thumbnailPath, thumbnailRoot))
            {
                return;
            }

            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(thumbnailPath) ?? "";
            if (
                !string.Equals(fileNameWithoutExtension, oldBody, StringComparison.OrdinalIgnoreCase)
                && !fileNameWithoutExtension.StartsWith(
                    oldBody + ".#",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return;
            }

            target.Add(thumbnailPath);
        }

        private static bool IsPathUnderRoot(string path, string root)
        {
            string fullPath = Path.GetFullPath(path);
            string fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }

        // UIが握っている各表示先パスも同時に差し替え、リロード前の見た目崩れを防ぐ。
        internal static void ApplyRenamedThumbnailPaths(
            MovieRecords movie,
            IReadOnlyList<ThumbnailRenameFileOperation> operations
        )
        {
            if (movie == null || operations == null)
            {
                return;
            }

            foreach (ThumbnailRenameFileOperation operation in operations)
            {
                UpdateMovieThumbnailPath(
                    movie,
                    operation.SourcePath,
                    operation.DestinationPath
                );
            }
        }

        private static void UpdateMovieThumbnailPath(
            MovieRecords movie,
            string sourcePath,
            string destinationPath
        )
        {
            if (string.Equals(movie.ThumbPathSmall, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                movie.ThumbPathSmall = destinationPath;
            }
            if (string.Equals(movie.ThumbPathBig, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                movie.ThumbPathBig = destinationPath;
            }
            if (string.Equals(movie.ThumbPathGrid, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                movie.ThumbPathGrid = destinationPath;
            }
            if (string.Equals(movie.ThumbPathList, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                movie.ThumbPathList = destinationPath;
            }
            if (string.Equals(movie.ThumbPathBig10, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                movie.ThumbPathBig10 = destinationPath;
            }
            if (string.Equals(movie.ThumbDetail, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                movie.ThumbDetail = destinationPath;
            }
        }
    }

    public partial class MainWindow
    {
        private int _renameBridgeRevision;

        private sealed class RenameBridgeUiSnapshot
        {
            public int Revision { get; init; }
            public string DbFullPath { get; init; } = "";
            public string CurrentSort { get; init; } = "";
            public string ThumbnailRoot { get; init; } = "";
            public string BookmarkFolder { get; init; } = "";
            public RenameBridgeMovieSnapshot[] Movies { get; init; } = [];
            public WatchChangedMovie[] ChangedMovies { get; init; } = [];
        }

        private sealed class RenameBridgeMovieSnapshot
        {
            public MovieRecords UiRecord { get; init; }
            public long MovieId { get; init; }
            public string MoviePath { get; init; } = "";
            public string MovieName { get; init; } = "";
            public string Kana { get; init; } = "";
            public string Roma { get; init; } = "";
            public ThumbnailRenameAssetTransferHelper.ThumbnailPathSnapshot ThumbnailSnapshot { get; init; }
        }

        private sealed class RenameBridgeBackgroundResult
        {
            public List<RenameBridgeThumbnailApplyItem> ThumbnailApplyItems { get; } = [];
        }

        private sealed class RenameBridgeThumbnailApplyItem
        {
            public MovieRecords UiRecord { get; init; }
            public IReadOnlyList<ThumbnailRenameAssetTransferHelper.ThumbnailRenameFileOperation> Operations { get; init; } = [];
        }

        // テスト/既存呼び出し側向けに、rename 入口の薄い橋渡しを維持する。
        internal static void ProcessRenamedWatchEventDirect(
            string eFullPath,
            string oldFullPath,
            Action<string, string> onRenamedWatch
        )
        {
            Action<string, string, Func<bool>, Action<string>> renameAction =
                (_, _, _, _) => onRenamedWatch?.Invoke(eFullPath, oldFullPath);
            ProcessRenamedWatchEventDirect(
                eFullPath,
                oldFullPath,
                renameAction,
                () => true,
                null
            );
        }

        // rename 本体直前でのガード付き入口を、既存シーケンスを崩さず再現する。
        internal static void ProcessRenamedWatchEventDirect(
            string eFullPath,
            string oldFullPath,
            Action<string, string, Func<bool>, Action<string>> renameAction,
            Func<bool> canStartRenameBridge,
            Action<string> logWatchMessage
        )
        {
            Func<bool> canStartRenameBridgeOrDefault = canStartRenameBridge ?? (() => true);
            renameAction?.Invoke(eFullPath, oldFullPath, canStartRenameBridgeOrDefault, logWatchMessage);
        }

        // callback を先に受けてから rename 本体を呼ぶ古い順序を維持する。
        internal static void ProcessRenamedWatchEventDirect(
            string eFullPath,
            string oldFullPath,
            Action<string, string> onRenamedWatch,
            Action<string, string, Func<bool>, Action<string>> renameAction,
            Func<bool> canStartRenameBridge,
            Action<string> logWatchMessage
        )
        {
            onRenamedWatch?.Invoke(eFullPath, oldFullPath);
            ProcessRenamedWatchEventDirect(
                eFullPath,
                oldFullPath,
                renameAction,
                canStartRenameBridge,
                logWatchMessage
            );
        }

        // 既存UI操作との互換のため、従来名は fire-and-forget の薄い入口として残す。
        private void RenameThumb(string eFullPath, string oldFullPath)
        {
            _ = RenameThumbAsync(eFullPath, oldFullPath);
        }

        /// <summary>
        /// リネームイベントを検知！DB・サムネ・ブックマークの全方位に「名前変わったぞ！」と号令をかけて回る怒涛の追従処理！🏃‍♂️💨
        /// </summary>
        private async Task RenameThumbAsync(string eFullPath, string oldFullPath)
        {
            try
            {
                RenameBridgeUiSnapshot snapshot = await CreateRenameBridgeUiSnapshotAsync(
                    eFullPath,
                    oldFullPath
                );

                // Created 直後に rename されて旧パスが未登録だった場合は、
                // rename だけでは取り込めないため watch scan へ再合流して最終整合を回収する。
                if (snapshot.Movies.Length < 1)
                {
                    await TryQueueWatchScanForUntrackedRenameAsync(eFullPath, oldFullPath);
                    return;
                }

                RenameBridgeBackgroundResult result = await Task.Run(
                    () => RunRenameBridgeBackgroundWork(snapshot, eFullPath, oldFullPath)
                );

                if (!CanApplyRenameBridgeResult(snapshot))
                {
                    DebugRuntimeLog.Write(
                        "watch",
                        $"rename bridge result skipped: reason=stale-or-db-changed revision={snapshot.Revision} db='{snapshot.DbFullPath}'"
                    );
                    return;
                }

                await Dispatcher.InvokeAsync(
                    () =>
                    {
                        if (!CanApplyRenameBridgeResult(snapshot))
                        {
                            return;
                        }

                        ApplyRenameBridgeBackgroundResultOnUiThread(result);
                        ReloadBookmarkTabData();
                    },
                    System.Windows.Threading.DispatcherPriority.Background
                );

                if (!CanApplyRenameBridgeResult(snapshot))
                {
                    return;
                }

                await RefreshMovieViewAfterRenameAsync(snapshot.CurrentSort, snapshot.ChangedMovies);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch",
                    $"rename bridge failed: old='{oldFullPath}' new='{eFullPath}' err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

        private Task<RenameBridgeUiSnapshot> CreateRenameBridgeUiSnapshotAsync(
            string eFullPath,
            string oldFullPath
        )
        {
            if (Dispatcher == null || Dispatcher.CheckAccess())
            {
                return Task.FromResult(CreateRenameBridgeUiSnapshotOnUiThread(eFullPath, oldFullPath));
            }

            return Dispatcher
                .InvokeAsync(
                    () => CreateRenameBridgeUiSnapshotOnUiThread(eFullPath, oldFullPath),
                    System.Windows.Threading.DispatcherPriority.Background
                )
                .Task;
        }

        private RenameBridgeUiSnapshot CreateRenameBridgeUiSnapshotOnUiThread(
            string eFullPath,
            string oldFullPath
        )
        {
            int revision = Interlocked.Increment(ref _renameBridgeRevision);
            string dbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            string movieName = Path.GetFileNameWithoutExtension(eFullPath).ToLower();
            string thumbnailRoot = ResolveCurrentThumbnailRoot();
            string bookmarkFolder = ResolveBookmarkFolderPath();
            List<RenameBridgeMovieSnapshot> movies = [];
            List<WatchChangedMovie> changedMovies = [];

            foreach (
                MovieRecords item in MainVM?.MovieRecs?.Where(x =>
                    IsMoviePathMatchForRename(x?.Movie_Path, oldFullPath)
                ) ?? []
            )
            {
                item.Movie_Path = eFullPath;
                item.Movie_Name = movieName;
                string persistedKana = JapaneseKanaProvider.GetKanaForPersistence(
                    item.Movie_Name,
                    item.Movie_Path
                );
                string persistedRoma = JapaneseKanaProvider.GetRomaFromKanaForPersistence(
                    persistedKana
                );
                item.Kana = persistedKana;
                item.Roma = persistedRoma;

                movies.Add(
                    new RenameBridgeMovieSnapshot
                    {
                        UiRecord = item,
                        MovieId = item.Movie_Id,
                        MoviePath = item.Movie_Path,
                        MovieName = item.Movie_Name,
                        Kana = persistedKana,
                        Roma = persistedRoma,
                        ThumbnailSnapshot = ThumbnailRenameAssetTransferHelper.CreatePathSnapshot(item),
                    }
                );
                changedMovies.Add(
                    new WatchChangedMovie(
                        item.Movie_Path,
                        WatchMovieChangeKind.None,
                        WatchMovieDirtyFields.MovieName
                            | WatchMovieDirtyFields.MoviePath
                            | WatchMovieDirtyFields.Kana
                    )
                );
            }

            return new RenameBridgeUiSnapshot
            {
                Revision = revision,
                DbFullPath = dbFullPath,
                CurrentSort = MainVM?.DbInfo?.Sort ?? "",
                ThumbnailRoot = thumbnailRoot,
                BookmarkFolder = bookmarkFolder,
                Movies = movies.ToArray(),
                ChangedMovies = changedMovies.ToArray(),
            };
        }

        private RenameBridgeBackgroundResult RunRenameBridgeBackgroundWork(
            RenameBridgeUiSnapshot snapshot,
            string eFullPath,
            string oldFullPath
        )
        {
            RenameBridgeBackgroundResult result = new();
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.DbFullPath))
            {
                return result;
            }

            string oldFileName = Path.GetFileNameWithoutExtension(oldFullPath) ?? "";
            foreach (RenameBridgeMovieSnapshot movie in snapshot.Movies)
            {
                if (!IsRenameBridgeRevisionCurrent(snapshot.Revision))
                {
                    break;
                }

                PersistRenameBridgeMovieSnapshot(snapshot.DbFullPath, movie);
                IReadOnlyList<ThumbnailRenameAssetTransferHelper.ThumbnailRenameFileOperation> operations =
                    ThumbnailRenameAssetTransferHelper.RenameThumbnailFiles(
                        movie.ThumbnailSnapshot,
                        snapshot.ThumbnailRoot,
                        oldFullPath,
                        eFullPath
                    );
                result.ThumbnailApplyItems.Add(
                    new RenameBridgeThumbnailApplyItem
                    {
                        UiRecord = movie.UiRecord,
                        Operations = operations,
                    }
                );
            }

            if (
                IsRenameBridgeRevisionCurrent(snapshot.Revision)
                && snapshot.Movies.Length > 0
                && !string.IsNullOrWhiteSpace(oldFileName)
                && Directory.Exists(snapshot.BookmarkFolder)
            )
            {
                RenameBookmarkAssetsInBackground(
                    snapshot.DbFullPath,
                    snapshot.BookmarkFolder,
                    oldFileName,
                    snapshot.Movies[0].MovieName
                );
            }

            return result;
        }

        private void PersistRenameBridgeMovieSnapshot(
            string dbFullPath,
            RenameBridgeMovieSnapshot movie
        )
        {
            try
            {
                // DB更新は背景側でまとめて流し、UIに残すのは表示モデル更新だけにする。
                _mainDbMovieMutationFacade.UpdateMoviePath(dbFullPath, movie.MovieId, movie.MoviePath);
                _mainDbMovieMutationFacade.UpdateMovieName(dbFullPath, movie.MovieId, movie.MovieName);
                _mainDbMovieMutationFacade.UpdateKana(dbFullPath, movie.MovieId, movie.Kana);
                _mainDbMovieMutationFacade.UpdateRoma(dbFullPath, movie.MovieId, movie.Roma);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch",
                    $"rename bridge movie persist failed: db='{dbFullPath}' movie_id={movie.MovieId} err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

        private static void RenameBookmarkAssetsInBackground(
            string dbFullPath,
            string bookmarkFolder,
            string oldFileName,
            string newMovieName
        )
        {
            try
            {
                DirectoryInfo directory = new(bookmarkFolder);
                EnumerationOptions enumOption = new() { RecurseSubdirectories = true };
                foreach (FileInfo bookMarkJpg in directory.EnumerateFiles($"*{oldFileName}*.jpg", enumOption))
                {
                    string destinationPath = BuildBookmarkRenameDestinationPath(
                        bookMarkJpg.FullName,
                        oldFileName,
                        newMovieName
                    );
                    if (
                        !string.IsNullOrWhiteSpace(destinationPath)
                        && !string.Equals(
                            bookMarkJpg.FullName,
                            destinationPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        File.Move(bookMarkJpg.FullName, destinationPath, true);
                    }
                }

                UpdateBookmarkRename(dbFullPath, oldFileName, newMovieName);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch",
                    $"rename bridge bookmark move failed: db='{dbFullPath}' folder='{bookmarkFolder}' err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

        private void ApplyRenameBridgeBackgroundResultOnUiThread(RenameBridgeBackgroundResult result)
        {
            if (result == null)
            {
                return;
            }

            foreach (RenameBridgeThumbnailApplyItem item in result.ThumbnailApplyItems)
            {
                ThumbnailRenameAssetTransferHelper.ApplyRenamedThumbnailPaths(
                    item.UiRecord,
                    item.Operations
                );
            }
        }

        private bool CanApplyRenameBridgeResult(RenameBridgeUiSnapshot snapshot)
        {
            return snapshot != null
                && IsRenameBridgeRevisionCurrent(snapshot.Revision)
                && Dispatcher != null
                && !Dispatcher.HasShutdownStarted
                && !Dispatcher.HasShutdownFinished
                && AreSameMainDbPath(snapshot.DbFullPath, MainVM?.DbInfo?.DBFullPath ?? "");
        }

        private bool IsRenameBridgeRevisionCurrent(int revision)
        {
            return revision == Volatile.Read(ref _renameBridgeRevision) && !IsWatchEventShutdownRequested();
        }

        // 旧パス未登録の rename は scan 本流へ戻し、Created -> Renamed 連鎖の取りこぼしを防ぐ。
        private async Task TryQueueWatchScanForUntrackedRenameAsync(
            string newFullPath,
            string oldFullPath
        )
        {
            bool shouldQueue = await Task.Run(
                () => ShouldQueueWatchScanForUntrackedRename(newFullPath, oldFullPath)
            );
            if (!shouldQueue)
            {
                return;
            }

            DebugRuntimeLog.Write(
                "watch",
                $"rename without tracked movie rerouted to queued watch scan: old='{oldFullPath}' new='{newFullPath}'"
            );
            _ = QueueCheckFolderAsync(CheckMode.Watch, $"renamed-untracked:{newFullPath}");
        }

        private void TryQueueWatchScanForUntrackedRename(string newFullPath, string oldFullPath)
        {
            if (!ShouldQueueWatchScanForUntrackedRename(newFullPath, oldFullPath))
            {
                return;
            }

            DebugRuntimeLog.Write(
                "watch",
                $"rename without tracked movie rerouted to queued watch scan: old='{oldFullPath}' new='{newFullPath}'"
            );
            _ = QueueCheckFolderAsync(CheckMode.Watch, $"renamed-untracked:{newFullPath}");
        }

        internal static bool ShouldQueueWatchScanForUntrackedRename(
            string newFullPath,
            string oldFullPath
        )
        {
            if (
                string.IsNullOrWhiteSpace(newFullPath)
                || string.IsNullOrWhiteSpace(oldFullPath)
                || string.Equals(newFullPath, oldFullPath, StringComparison.OrdinalIgnoreCase)
            )
            {
                return false;
            }

            return File.Exists(newFullPath);
        }

        // Windows の rename は大文字小文字違いだけでも飛んでくるため、比較は大文字小文字を無視する。
        internal static bool IsMoviePathMatchForRename(string currentMoviePath, string oldFullPath)
        {
            if (string.IsNullOrWhiteSpace(currentMoviePath) || string.IsNullOrWhiteSpace(oldFullPath))
            {
                return false;
            }

            return string.Equals(
                currentMoviePath,
                oldFullPath,
                StringComparison.OrdinalIgnoreCase
            );
        }

        // bookmark の rename はファイル名部分だけを差し替え、親フォルダまで巻き込まない。
        internal static string BuildBookmarkRenameDestinationPath(
            string bookmarkFilePath,
            string oldFileName,
            string newMovieName
        )
        {
            if (
                string.IsNullOrWhiteSpace(bookmarkFilePath)
                || string.IsNullOrWhiteSpace(oldFileName)
                || string.IsNullOrWhiteSpace(newMovieName)
            )
            {
                return bookmarkFilePath ?? "";
            }

            string directoryPath = Path.GetDirectoryName(bookmarkFilePath) ?? "";
            string fileName = Path.GetFileName(bookmarkFilePath) ?? "";
            string renamedFileName = fileName.Replace(
                oldFileName,
                newMovieName,
                StringComparison.OrdinalIgnoreCase
            );

            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return renamedFileName;
            }

            return Path.Combine(directoryPath, renamedFileName);
        }
    }
}
