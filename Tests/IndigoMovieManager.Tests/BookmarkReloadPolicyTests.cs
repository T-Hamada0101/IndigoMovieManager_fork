using System;
using System.Data;
using System.IO;
using NUnit.Framework;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class BookmarkReloadPolicyTests
{
    [Test]
    public void AddBookmark_Click_動画メタ取得とDB登録は背景へ逃がす()
    {
        string source = GetRepoText("BottomTabs", "Bookmark", "MainWindow.BottomTab.Bookmark.cs");
        string clickMethod = GetMethodBlock(source, "private async void AddBookmark_Click(");
        string prepareMethod = GetMethodBlock(
            source,
            "private static BookmarkAddResult PrepareBookmarkAddInBackground("
        );
        string persistMethod = GetMethodBlock(
            source,
            "private static BookmarkPersistResult PersistPreparedBookmarkInBackground("
        );

        Assert.That(clickMethod, Does.Contain("Task.Run("));
        Assert.That(clickMethod, Does.Contain("AreSameMainDbPath("));
        Assert.That(clickMethod, Does.Contain("await CreateBookmarkThumbAsync("));
        Assert.That(clickMethod, Does.Not.Contain("new MovieInfo("));
        Assert.That(clickMethod, Does.Not.Contain("InsertBookmarkTable("));
        Assert.That(prepareMethod, Does.Contain("MovieInfo movieInfo = new("));
        Assert.That(prepareMethod, Does.Contain("Directory.CreateDirectory("));
        Assert.That(prepareMethod, Does.Not.Contain("InsertBookmarkTable("));
        Assert.That(persistMethod, Does.Contain("TryInsertBookmarkTable("));
    }

    [Test]
    public void DeleteBookmark_削除DB書き込みは背景へ逃がす()
    {
        string source = GetRepoText("BottomTabs", "Bookmark", "MainWindow.BottomTab.Bookmark.cs");
        string deleteMethod = GetMethodBlock(source, "public async void DeleteBookmark(");
        string backgroundMethod = GetMethodBlock(
            source,
            "private static BookmarkPersistResult DeleteBookmarkInBackground("
        );

        Assert.That(deleteMethod, Does.Contain("Task.Run("));
        Assert.That(deleteMethod, Does.Contain("AreSameMainDbPath("));
        Assert.That(deleteMethod, Does.Not.Contain("DeleteBookmarkTable("));
        Assert.That(backgroundMethod, Does.Contain("TryDeleteBookmarkTable("));
        Assert.That(backgroundMethod, Does.Not.Match(@"(?<!Try)DeleteBookmarkTable\("));
    }

    [Test]
    public void ReloadBookmarkTabDataCoreAsync_旧DBの後着結果は反映しない()
    {
        string source = GetRepoText("BottomTabs", "Bookmark", "MainWindow.BottomTab.Bookmark.cs");
        string reloadMethod = GetMethodBlock(
            source,
            "private async Task ReloadBookmarkTabDataCoreAsync("
        );

        Assert.That(reloadMethod, Does.Contain("AreSameMainDbPath(dbFullPath"));
        Assert.That(reloadMethod, Does.Contain("bookmarkData = snapshot.BookmarkData;"));
    }

    [Test]
    public void Bookmark保存失敗はdirty_failed_retryableを軽量状態とログで読める()
    {
        PersistenceWriteRequest writeRequest = MainWindow.BuildBookmarkPersistenceWriteRequest(
            "add-db"
        );
        PersistenceWriteResult writeResult = PersistenceWriteResult.FromFailure(
            writeRequest,
            TimeSpan.FromMilliseconds(2.4d),
            PersistenceFailureKind.Bookmark
        );
        MainWindow.BookmarkPersistenceState state =
            MainWindow.BuildBookmarkPersistenceFailureState(
                "add-db",
                "sample.wb",
                12,
                "sample.mp4",
                "SQLiteException"
            );
        string log = MainWindow.BuildBookmarkPersistenceFailureLog(state, writeResult);

        Assert.Multiple(() =>
        {
            Assert.That(state.Dirty, Is.True);
            Assert.That(state.Failed, Is.True);
            Assert.That(state.Retryable, Is.True);
            Assert.That(state.NotifyUi, Is.False);
            Assert.That(state.Operation, Is.EqualTo("add-db"));
            Assert.That(state.FailureReason, Is.EqualTo("SQLiteException"));
            Assert.That(log, Does.Contain("bookmark persist failed:"));
            Assert.That(log, Does.Contain("operation='add-db'"));
            Assert.That(log, Does.Contain("write_kind=background-db-write"));
            Assert.That(log, Does.Contain("write_reason=bookmark-add"));
            Assert.That(log, Does.Contain("queue_key=bookmark-db"));
            Assert.That(log, Does.Contain("write_succeeded=false"));
            Assert.That(log, Does.Contain("failure_kind=bookmark"));
            Assert.That(log, Does.Contain("dirty=true"));
            Assert.That(log, Does.Contain("failed=true"));
            Assert.That(log, Does.Contain("retryable=true"));
            Assert.That(log, Does.Contain("notify_ui=false"));
            Assert.That(log, Does.Contain("reason='SQLiteException'"));
        });
    }

    [Test]
    public void Bookmark保存経路はPersistenceWriteRequestResult語彙を使う()
    {
        string source = GetRepoText("BottomTabs", "Bookmark", "MainWindow.BottomTab.Bookmark.cs");
        string deleteMethod = GetMethodBlock(
            source,
            "private static BookmarkPersistResult DeleteBookmarkInBackground("
        );
        string persistMethod = GetMethodBlock(
            source,
            "private static BookmarkPersistResult PersistPreparedBookmarkInBackground("
        );
        string applyMethod = GetMethodBlock(source, "private void ApplyBookmarkPersistResult(");

        PersistenceWriteRequest addRequest = MainWindow.BuildBookmarkPersistenceWriteRequest(
            "add-db"
        );
        PersistenceWriteRequest deleteRequest = MainWindow.BuildBookmarkPersistenceWriteRequest(
            "delete-db"
        );

        Assert.Multiple(() =>
        {
            Assert.That(addRequest.BuildLogFields(), Does.Contain("write_reason=bookmark-add"));
            Assert.That(
                deleteRequest.BuildLogFields(),
                Does.Contain("write_reason=bookmark-delete")
            );
            Assert.That(
                addRequest.BuildLogFields(),
                Does.Contain("write_kind=background-db-write")
            );
            Assert.That(addRequest.BuildLogFields(), Does.Contain("queue_key=bookmark-db"));
            Assert.That(deleteMethod, Does.Contain("BuildBookmarkPersistenceWriteRequest("));
            Assert.That(persistMethod, Does.Contain("BuildBookmarkPersistenceWriteRequest("));
            Assert.That(deleteMethod, Does.Contain("PersistenceWriteResult.FromSuccess("));
            Assert.That(deleteMethod, Does.Contain("PersistenceWriteResult.FromFailure("));
            Assert.That(persistMethod, Does.Contain("PersistenceWriteResult.FromSuccess("));
            Assert.That(persistMethod, Does.Contain("PersistenceWriteResult.FromFailure("));
            Assert.That(applyMethod, Does.Contain("BuildBookmarkPersistenceSuccessLog("));
            Assert.That(applyMethod, Does.Contain("result.WriteResult"));
        });
    }

    [Test]
    public void Bookmark保存のUI経路はTry入口で失敗状態を受け取る()
    {
        string source = GetRepoText("BottomTabs", "Bookmark", "MainWindow.BottomTab.Bookmark.cs");
        string clickMethod = GetMethodBlock(source, "private async void AddBookmark_Click(");
        string persistMethod = GetMethodBlock(
            source,
            "private static BookmarkPersistResult PersistPreparedBookmarkInBackground("
        );

        Assert.That(clickMethod, Does.Contain("ApplyBookmarkPersistResult("));
        Assert.That(clickMethod, Does.Not.Contain("InsertBookmarkTable("));
        Assert.That(persistMethod, Does.Contain("TryInsertBookmarkTable("));
        Assert.That(persistMethod, Does.Not.Match(@"(?<!Try)InsertBookmarkTable\("));
    }

    [Test]
    public void ReloadBookmarkTabDataCoreAsync_dirty解除は成功反映後に行う()
    {
        string source = GetRepoText("BottomTabs", "Bookmark", "MainWindow.BottomTab.Bookmark.cs");
        string reloadMethod = GetMethodBlock(
            source,
            "private async Task ReloadBookmarkTabDataCoreAsync("
        );

        int loadIndex = reloadMethod.IndexOf("LoadBookmarkReloadSnapshot", StringComparison.Ordinal);
        int refreshIndex = reloadMethod.IndexOf("RefreshBookmarkTabView();", StringComparison.Ordinal);
        int completedIndex = reloadMethod.LastIndexOf(
            "_bookmarkTabPresenter?.OnReloadCompleted();",
            StringComparison.Ordinal
        );

        Assert.That(loadIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(refreshIndex, Is.GreaterThan(loadIndex));
        Assert.That(completedIndex, Is.GreaterThan(refreshIndex));
    }

    [Test]
    public void BookmarkTabView_ObservableCollection通知に任せてItemsRefreshしない()
    {
        string source = GetRepoText("BottomTabs", "Bookmark", "BookmarkTabView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("ObservableCollection"));
            Assert.That(source, Does.Not.Contain(".Items.Refresh()"));
        });
    }

    [Test]
    public void BuildBookmarkRecordsForReload_bookmark行をMovieRecordsへ変換できる()
    {
        DataTable bookmarkData = new();
        bookmarkData.Columns.Add("movie_id", typeof(long));
        bookmarkData.Columns.Add("movie_name", typeof(string));
        bookmarkData.Columns.Add("movie_path", typeof(string));
        bookmarkData.Columns.Add("last_date", typeof(string));
        bookmarkData.Columns.Add("file_date", typeof(string));
        bookmarkData.Columns.Add("regist_date", typeof(string));
        bookmarkData.Columns.Add("view_count", typeof(long));
        bookmarkData.Columns.Add("kana", typeof(string));
        bookmarkData.Columns.Add("roma", typeof(string));

        bookmarkData.Rows.Add(
            10L,
            "sample",
            "sample[(123)12-34-56].jpg",
            "2026/04/17 10:11:12",
            "2026/04/16 09:08:07",
            "2026/04/15 08:07:06",
            5L,
            "さむぷる",
            "sample"
        );

        MovieRecords[] items = MainWindow.BuildBookmarkRecordsForReload(
            bookmarkData,
            @"C:\bookmark-root"
        );

        Assert.That(items, Has.Length.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(items[0].Movie_Id, Is.EqualTo(10L));
            Assert.That(items[0].Movie_Name, Is.EqualTo("sample.jpg"));
            Assert.That(items[0].Movie_Body, Is.EqualTo("sample"));
            Assert.That(items[0].Score, Is.EqualTo(123L));
            Assert.That(
                items[0].ThumbDetail,
                Is.EqualTo(@"C:\bookmark-root\sample[(123)12-34-56].jpg")
            );
            Assert.That(items[0].Kana, Is.EqualTo("さむぷる"));
            Assert.That(items[0].Roma, Is.EqualTo("sample"));
        });
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

        Assert.Fail($"Repository file not found: {Path.Combine(relativePathParts)}");
        return "";
    }

    private static string GetMethodBlock(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{signature} が見つかりません。");

        int bodyStart = source.IndexOf('{', start);
        Assert.That(bodyStart, Is.GreaterThanOrEqualTo(0), $"{signature} の本文開始が見つかりません。");

        int depth = 0;
        for (int index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(start, index - start + 1);
                }
            }
        }

        Assert.Fail($"{signature} の本文終了が見つかりません。");
        return "";
    }
}
