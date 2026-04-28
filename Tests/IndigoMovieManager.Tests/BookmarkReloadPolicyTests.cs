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
            "private static void PersistPreparedBookmarkInBackground("
        );

        Assert.That(clickMethod, Does.Contain("Task.Run("));
        Assert.That(clickMethod, Does.Contain("AreSameMainDbPath("));
        Assert.That(clickMethod, Does.Contain("await CreateBookmarkThumbAsync("));
        Assert.That(clickMethod, Does.Not.Contain("new MovieInfo("));
        Assert.That(clickMethod, Does.Not.Contain("InsertBookmarkTable("));
        Assert.That(prepareMethod, Does.Contain("MovieInfo movieInfo = new("));
        Assert.That(prepareMethod, Does.Contain("Directory.CreateDirectory("));
        Assert.That(prepareMethod, Does.Not.Contain("InsertBookmarkTable("));
        Assert.That(persistMethod, Does.Contain("InsertBookmarkTable("));
    }

    [Test]
    public void DeleteBookmark_削除DB書き込みは背景へ逃がす()
    {
        string source = GetRepoText("BottomTabs", "Bookmark", "MainWindow.BottomTab.Bookmark.cs");
        string deleteMethod = GetMethodBlock(source, "public async void DeleteBookmark(");
        string backgroundMethod = GetMethodBlock(
            source,
            "private static void DeleteBookmarkInBackground("
        );

        Assert.That(deleteMethod, Does.Contain("Task.Run("));
        Assert.That(deleteMethod, Does.Contain("AreSameMainDbPath("));
        Assert.That(deleteMethod, Does.Not.Contain("DeleteBookmarkTable("));
        Assert.That(backgroundMethod, Does.Contain("DeleteBookmarkTable("));
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
