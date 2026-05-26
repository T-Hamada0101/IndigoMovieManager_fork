using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ExtDetailUiIoPolicyTests
{
    [Test]
    public void Hyperlink_Click_ファイル存在確認は背景へ逃がす()
    {
        string source = GetExtDetailSourceText();
        string clickMethod = ExtractMethod(source, "private async void Hyperlink_Click(");
        string existsMethod = ExtractMethod(
            source,
            "private static Task<bool> PathExistsInBackgroundAsync("
        );

        Assert.That(clickMethod, Does.Contain("string moviePath = mv.Movie_Path;"));
        Assert.That(clickMethod, Does.Contain("await PathExistsInBackgroundAsync(moviePath)"));
        Assert.That(clickMethod, Does.Contain("Process.Start(\"explorer.exe\", $\"/select,{moviePath}\")"));
        Assert.That(clickMethod, Does.Not.Contain("Path.Exists(mv.Movie_Path)"));
        Assert.That(existsMethod, Does.Contain("Task.Run(() =>"));
        Assert.That(existsMethod, Does.Contain("return Path.Exists(path);"));
    }

    [Test]
    public void DetailThumbnailImage_ContextMenuOpening_存在確認は背景へ逃がす()
    {
        string source = GetExtDetailSourceText();
        string openingMethod = ExtractMethod(
            source,
            "private async void DetailThumbnailImage_ContextMenuOpening("
        );

        Assert.That(openingMethod, Does.Contain("string thumbDetailSnapshot = record.ThumbDetail;"));
        Assert.That(
            openingMethod,
            Does.Contain("await HasDetailThumbnailFileAsync(thumbDetailSnapshot)")
        );
        Assert.That(openingMethod, Does.Contain("ReferenceEquals(imageElement.DataContext, record)"));
        Assert.That(openingMethod, Does.Not.Contain("Path.Exists("));
    }

    [Test]
    public void ConfigureDetailThumbnailFileWatch_存在確認は背景へ逃がして後着を捨てる()
    {
        string source = GetExtDetailSourceText();
        string configureMethod = ExtractMethod(
            source,
            "private async void ConfigureDetailThumbnailFileWatch("
        );
        string pathStateMethod = ExtractMethod(
            source,
            "private static Task<DetailThumbnailWatchPathState> GetDetailThumbnailWatchPathStateAsync("
        );

        int existsIndex = configureMethod.IndexOf(
            "await GetDetailThumbnailWatchPathStateAsync(normalizedTargetPath, directoryPath)",
            StringComparison.Ordinal
        );
        int watcherIndex = configureMethod.IndexOf("new FileSystemWatcher(", StringComparison.Ordinal);

        Assert.That(existsIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(watcherIndex, Is.GreaterThan(existsIndex));
        Assert.That(configureMethod, Does.Contain("int watchRevision = ++_detailThumbnailWatchRevision;"));
        Assert.That(configureMethod, Does.Contain("watchRevision != _detailThumbnailWatchRevision"));
        Assert.That(configureMethod, Does.Contain("ReferenceEquals(_subscribedRecord, subscribedRecordSnapshot)"));
        Assert.That(configureMethod, Does.Not.Contain("Path.Exists("));
        Assert.That(configureMethod, Does.Not.Contain("Directory.Exists("));
        Assert.That(pathStateMethod, Does.Contain("Task.Run(() =>"));
        Assert.That(pathStateMethod, Does.Contain("Path.Exists(targetPath)"));
        Assert.That(pathStateMethod, Does.Contain("Directory.Exists(directoryPath)"));
    }

    [Test]
    public void DetailThumbnailFileWatcher_Changed_存在確認はDispatcherへ戻す前に背景で行う()
    {
        string source = GetExtDetailSourceText();
        string watcherMethod = ExtractMethod(
            source,
            "private async void DetailThumbnailFileWatcher_Changed("
        );

        int existsIndex = watcherMethod.IndexOf(
            "await PathExistsInBackgroundAsync(watchedPathSnapshot)",
            StringComparison.Ordinal
        );
        int dispatcherIndex = watcherMethod.IndexOf("Dispatcher.BeginInvoke(", StringComparison.Ordinal);

        Assert.That(existsIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(dispatcherIndex, Is.GreaterThan(existsIndex));
        Assert.That(watcherMethod, Does.Contain("string watchedPathSnapshot = _watchedDetailThumbnailPath;"));
        Assert.That(watcherMethod, Does.Contain("MovieRecords subscribedRecordSnapshot = _subscribedRecord;"));
        Assert.That(watcherMethod, Does.Contain("ReferenceEquals(_subscribedRecord, subscribedRecordSnapshot)"));
        Assert.That(watcherMethod, Does.Not.Contain("Path.Exists(_watchedDetailThumbnailPath)"));
    }

    private static string GetExtDetailSourceText()
    {
        return GetRepoText("UserControls", "ExtDetail.xaml.cs");
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

    private static string ExtractMethod(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{signature} が見つかりません。");

        int braceStart = source.IndexOf('{', start);
        Assert.That(braceStart, Is.GreaterThanOrEqualTo(0));

        int depth = 0;
        for (int index = braceStart; index < source.Length; index++)
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

        Assert.Fail($"{signature} の終端が見つかりません。");
        return string.Empty;
    }
}
