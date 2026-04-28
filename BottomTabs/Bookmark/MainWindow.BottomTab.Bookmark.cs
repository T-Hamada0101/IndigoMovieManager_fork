using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IndigoMovieManager.BottomTabs.Bookmark;
using IndigoMovieManager.Thumbnail;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private BookmarkTabPresenter _bookmarkTabPresenter;
        private int _bookmarkReloadRevision;

        private void InitializeBookmarkTabSupport()
        {
            if (_bookmarkTabPresenter == null && exBookMark != null && BookmarkTabViewHost != null)
            {
                _bookmarkTabPresenter = new BookmarkTabPresenter(
                    exBookMark,
                    BookmarkTabViewHost,
                    ReloadBookmarkTabDataCore
                );
            }

            _bookmarkTabPresenter?.Initialize();
        }

        // Bookmarkタブ側で使うフォルダ解決を1か所へ寄せる。
        private string ResolveBookmarkFolderPath()
        {
            string bookmarkFolder = MainVM?.DbInfo?.BookmarkFolder ?? "";
            string dbName = MainVM?.DbInfo?.DBName ?? "";
            if (string.IsNullOrWhiteSpace(dbName))
            {
                return bookmarkFolder;
            }

            string defaultBookmarkFolder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "bookmark",
                dbName
            );
            return string.IsNullOrWhiteSpace(bookmarkFolder)
                ? defaultBookmarkFolder
                : bookmarkFolder;
        }

        // Bookmark一覧の見た目更新は、この窓口を通す。
        private void RefreshBookmarkTabView()
        {
            _bookmarkTabPresenter?.RefreshView();
        }

        // DB再読込と一覧更新をセットで呼びたい場所が多いため、ここへ集約する。
        private void ReloadBookmarkTabData()
        {
            _bookmarkTabPresenter?.ReloadOrMarkDirty();
        }

        // 非表示中は遅延し、表示中だけDB再読込と一覧更新をまとめて流す。
        private void ReloadBookmarkTabDataCore()
        {
            _ = ReloadBookmarkTabDataCoreAsync();
        }

        // Bookmark 一覧の反映は UI に残し、DB read と item 生成だけを背景へ逃がす。
        private async Task ReloadBookmarkTabDataCoreAsync()
        {
            if (string.IsNullOrWhiteSpace(MainVM?.DbInfo?.DBFullPath))
            {
                bookmarkData?.Clear();
                MainVM?.BookmarkRecs.Clear();
                _bookmarkTabPresenter?.OnReloadCompleted();
                return;
            }

            string dbFullPath = MainVM.DbInfo.DBFullPath;
            string bookmarkFolder = ResolveBookmarkFolderPath();
            int requestRevision = Interlocked.Increment(ref _bookmarkReloadRevision);

            BookmarkReloadSnapshot snapshot = await Task.Run(
                () => LoadBookmarkReloadSnapshot(dbFullPath, bookmarkFolder)
            );

            if (requestRevision != Volatile.Read(ref _bookmarkReloadRevision))
            {
                return;
            }

            if (!AreSameMainDbPath(dbFullPath, MainVM?.DbInfo?.DBFullPath ?? ""))
            {
                return;
            }

            bookmarkData = snapshot.BookmarkData;
            MainVM.BookmarkRecs.Clear();
            if (bookmarkData == null)
            {
                _bookmarkTabPresenter?.OnReloadCompleted();
                return;
            }

            foreach (MovieRecords item in snapshot.Items)
            {
                MainVM.BookmarkRecs.Add(item);
            }

            RefreshBookmarkTabView();
            _bookmarkTabPresenter?.OnReloadCompleted();
        }

        /// <summary>
        /// bookmarkテーブルの読み込み結果を、UI反映しやすい形へ組み替える。
        /// </summary>
        private static BookmarkReloadSnapshot LoadBookmarkReloadSnapshot(
            string dbFullPath,
            string bookmarkFolder
        )
        {
            DataTable loadedBookmarkData = GetData(dbFullPath, "select * from bookmark");
            return new BookmarkReloadSnapshot(
                loadedBookmarkData,
                BuildBookmarkRecordsForReload(loadedBookmarkData, bookmarkFolder)
            );
        }

        // Bookmark の DataRow を UI バインド用 MovieRecords へまとめて変換する。
        internal static MovieRecords[] BuildBookmarkRecordsForReload(
            DataTable bookmarkData,
            string bookmarkFolder
        )
        {
            if (bookmarkData == null)
            {
                return [];
            }

            List<MovieRecords> items = [];
            foreach (DataRow row in bookmarkData.AsEnumerable())
            {
                string movieFullPath = row["movie_path"].ToString();
                string ext = Path.GetExtension(movieFullPath);
                string thumbFile = Path.Combine(bookmarkFolder, movieFullPath);
                string thumbBody = movieFullPath.Split('[')[0];
                string frameS = movieFullPath.Split('(')[1];
                frameS = frameS.Split(')')[0];
                long frame = 0;
                if (frameS != "")
                {
                    frame = Convert.ToInt64(frameS);
                }

                items.Add(
                    new MovieRecords
                    {
                        Movie_Id = (long)row["movie_id"],
                        Movie_Name = $"{row["movie_name"]}{ext}",
                        Movie_Body = thumbBody,
                        Last_Date = ReadDbDateTimeTextOrEmpty(row["last_date"]),
                        File_Date = ReadDbDateTimeTextOrEmpty(row["file_date"]),
                        Regist_Date = ReadDbDateTimeTextOrEmpty(row["regist_date"]),
                        View_Count = (long)row["view_count"],
                        Score = frame,
                        Kana = row["kana"].ToString(),
                        Roma = row["roma"].ToString(),
                        IsExists = true,
                        Ext = ext,
                        ThumbDetail = thumbFile,
                    }
                );
            }

            return items.ToArray();
        }

        // Bookmarkタブ上の削除は、DBと一覧再構築をまとめて処理する。
        public async void DeleteBookmark(object sender, RoutedEventArgs e)
        {
            if (sender is not Button deleteButton)
            {
                return;
            }

            if (deleteButton.DataContext is not MovieRecords item)
            {
                return;
            }

            string dbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            long movieId = item.Movie_Id;
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return;
            }

            await Task.Run(() => DeleteBookmarkInBackground(dbFullPath, movieId));
            if (!AreSameMainDbPath(dbFullPath, MainVM?.DbInfo?.DBFullPath ?? ""))
            {
                return;
            }

            ReloadBookmarkTabData();
        }

        private static void DeleteBookmarkInBackground(string dbFullPath, long movieId)
        {
            DeleteBookmarkTable(dbFullPath, movieId);
        }

        // 再生位置からBookmarkサムネを作り、一覧まで更新する。
        private async void AddBookmark_Click(object sender, RoutedEventArgs e)
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

            timer.Stop();
            uxVideoPlayer.Pause();
            int pos = (int)uxVideoPlayer.Position.TotalSeconds;
            string dbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            string bookmarkFolder = ResolveBookmarkFolderPath();
            string movieFullPath = mv.Movie_Path;
            string movieBody = mv.Movie_Body;

            PlayerArea.Visibility = Visibility.Collapsed;
            PlayerController.Visibility = Visibility.Collapsed;
            uxVideoPlayer.Visibility = Visibility.Collapsed;
            uxVideoPlayer.Stop();
            IsPlaying = false;

            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return;
            }

            BookmarkAddResult result;
            try
            {
                result = await Task.Run(
                    () => PrepareBookmarkAddInBackground(
                        dbFullPath,
                        bookmarkFolder,
                        movieFullPath,
                        movieBody,
                        pos
                    )
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "bookmark",
                    $"add bookmark failed: db='{dbFullPath}' path='{movieFullPath}' err='{ex.GetType().Name}'"
                );
                return;
            }

            if (!AreSameMainDbPath(dbFullPath, MainVM?.DbInfo?.DBFullPath ?? ""))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(result.ThumbFileName))
            {
                return;
            }

            await Task.Delay(10);
            bool thumbCreated = await CreateBookmarkThumbAsync(
                result.MovieFullPath,
                result.ThumbFileName,
                result.PositionSeconds
            );
            if (!thumbCreated)
            {
                DebugRuntimeLog.Write(
                    "bookmark",
                    $"add bookmark thumbnail failed: db='{dbFullPath}' path='{result.MovieFullPath}'"
                );
                return;
            }

            if (!AreSameMainDbPath(dbFullPath, MainVM?.DbInfo?.DBFullPath ?? ""))
            {
                return;
            }

            await Task.Run(() => PersistPreparedBookmarkInBackground(dbFullPath, result));
            ReloadBookmarkTabData();
        }

        private static BookmarkAddResult PrepareBookmarkAddInBackground(
            string dbFullPath,
            string bookmarkFolder,
            string movieFullPath,
            string movieBody,
            int positionSeconds
        )
        {
            if (string.IsNullOrWhiteSpace(dbFullPath) || string.IsNullOrWhiteSpace(movieFullPath))
            {
                throw new ArgumentException("Bookmark source is empty.");
            }

            MovieInfo movieInfo = new(movieFullPath, true);
            int targetFrame = positionSeconds * (int)movieInfo.FPS;
            string timestamp = string.Format($"{DateTime.Now:HH-mm-ss}");
            string thumbBody = $"{movieBody}[({targetFrame}){timestamp}]";
            string thumbFileName = Path.Combine(
                bookmarkFolder,
                $"{thumbBody}.jpg"
            );
            string thumbFolder = Path.GetDirectoryName(thumbFileName) ?? "";
            if (!Path.Exists(thumbFolder))
            {
                Directory.CreateDirectory(thumbFolder);
            }

            movieInfo.MovieName = thumbBody;
            movieInfo.MoviePath = $"{thumbBody}.jpg";
            return new BookmarkAddResult(
                movieFullPath,
                thumbFileName,
                positionSeconds,
                movieInfo
            );
        }

        private static void PersistPreparedBookmarkInBackground(
            string dbFullPath,
            BookmarkAddResult result
        )
        {
            InsertBookmarkTable(dbFullPath, result.MovieInfo);
        }

        private sealed record BookmarkAddResult(
            string MovieFullPath,
            string ThumbFileName,
            int PositionSeconds,
            MovieInfo MovieInfo
        );

        private sealed class BookmarkReloadSnapshot
        {
            public BookmarkReloadSnapshot(DataTable bookmarkData, MovieRecords[] items)
            {
                BookmarkData = bookmarkData;
                Items = items ?? [];
            }

            public DataTable BookmarkData { get; }

            public MovieRecords[] Items { get; }
        }
    }
}
