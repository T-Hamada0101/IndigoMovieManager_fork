using System.Windows;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MainWindowWatchFolderDropTests
{
    [Test]
    public void CanAcceptWatchFolderDrop_MainDb未選択でも有効フォルダなら受け付ける()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string directoryPath = Directory.CreateDirectory(Path.Combine(tempRoot, "drop")).FullName;

            bool result = MainWindow.CanAcceptWatchFolderDrop("", [directoryPath]);

            Assert.That(result, Is.True);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void CanAcceptWatchFolderDrop_MainDb選択済みかつ有効フォルダなら受け付ける()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string directoryPath = Directory.CreateDirectory(Path.Combine(tempRoot, "drop")).FullName;
            string dbPath = Path.Combine(tempRoot, "sample.wb");

            bool result = MainWindow.CanAcceptWatchFolderDrop(dbPath, [directoryPath]);

            Assert.That(result, Is.True);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void CanAcceptWatchFolderDrop_ファイルだけなら受け付けない()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string filePath = Path.Combine(tempRoot, "sample.txt");
            File.WriteAllText(filePath, "sample");
            string dbPath = Path.Combine(tempRoot, "sample.wb");

            bool result = MainWindow.CanAcceptWatchFolderDrop(dbPath, [filePath]);

            Assert.That(result, Is.False);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void CanAcceptWatchFolderDrop_wbファイルなら受け付ける()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string dbPath = Path.Combine(tempRoot, "drop.wb");
            File.WriteAllText(dbPath, "sample");

            bool result = MainWindow.CanAcceptWatchFolderDrop("", [dbPath]);

            Assert.That(result, Is.True);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void ResolveDroppedMainDbPath_wbだけを切替対象として返す()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string textFile = Path.Combine(tempRoot, "memo.txt");
            string dbPath = Path.Combine(tempRoot, "main.WB");
            File.WriteAllText(textFile, "sample");
            File.WriteAllText(dbPath, "sample");

            string result = MainWindow.ResolveDroppedMainDbPath([textFile, dbPath]);

            Assert.That(result, Is.EqualTo(Path.GetFullPath(dbPath)));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [TestCase(0, "DBを切り替えました: main.wb", Notification.Wpf.NotificationType.Success)]
    [TestCase(1, "既に開いています: main.wb", Notification.Wpf.NotificationType.Information)]
    [TestCase(2, "DBを開けませんでした: main.wb", Notification.Wpf.NotificationType.Error)]
    public void BuildDroppedMainDbSwitchToast_DBドロップ結果に応じた文言を返す(
        int kindValue,
        string expectedMessage,
        Notification.Wpf.NotificationType expectedType
    )
    {
        MainWindow.DroppedMainDbSwitchToastKind kind =
            (MainWindow.DroppedMainDbSwitchToastKind)kindValue;
        (string title, string message, Notification.Wpf.NotificationType type) =
            MainWindow.BuildDroppedMainDbSwitchToast(@"C:\db\main.wb", kind);

        Assert.That(title, Is.EqualTo("DB切替"));
        Assert.That(message, Is.EqualTo(expectedMessage));
        Assert.That(type, Is.EqualTo(expectedType));
    }

    [Test]
    public void QueueDroppedWatchFolders_ドロップ直後のwatchテーブルIOは背景へ逃がす()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.WatchFolderDrop.cs");
        string queueMethod = GetMethodBlock(source, "private void QueueDroppedWatchFolders(");
        string backgroundMethod = GetMethodBlock(
            source,
            "private static DroppedWatchFolderApplyResult ApplyDroppedWatchFoldersInBackground("
        );

        Assert.That(queueMethod, Does.Contain("Task.Run("));
        Assert.That(queueMethod, Does.Contain("Dispatcher.BeginInvoke("));
        Assert.That(queueMethod, Does.Contain("AreSameMainDbPath("));
        Assert.That(queueMethod, Does.Not.Contain("SQLite.InsertWatchTable("));
        Assert.That(queueMethod, Does.Not.Contain("SQLite.GetData("));
        Assert.That(backgroundMethod, Does.Contain("SQLite.GetData("));
        Assert.That(backgroundMethod, Does.Contain("SQLite.InsertWatchTable("));
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_MainWindowWatchFolderDropTests",
            Guid.NewGuid().ToString("N")
        );
        return Directory.CreateDirectory(path).FullName;
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
