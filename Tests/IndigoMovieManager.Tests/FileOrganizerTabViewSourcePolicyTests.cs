using System.Runtime.CompilerServices;
using System.Text;
using NUnit.Framework;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class FileOrganizerTabViewSourcePolicyTests
{
    [Test]
    public void 読み取り専用の移動先表示はOneWayでバインドする()
    {
        string source = ReadFileOrganizerTabViewXaml();

        Assert.That(source, Does.Contain("{Binding DisplayFolderPath, Mode=OneWay}"));
        Assert.That(source, Does.Not.Contain("Text=\"{Binding DisplayFolderPath}\""));
        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Text=\"{Binding Movie_Path, Mode=OneWay}\""));
            Assert.That(source, Does.Contain("Text=\"{Binding Dir, Mode=OneWay}\""));
            Assert.That(source, Does.Contain("IsChecked=\"{Binding IsShortcutEnabled, Mode=TwoWay}\""));
            Assert.That(source, Does.Contain("Content=\"全移動\""));
            Assert.That(source, Does.Contain("ToolTip=\"現在のメインタブに表示中のすべての動画を、この登録先へ移動します。\""));
            Assert.That(source, Does.Contain("ショートカットでファイルを移動できます。"));
            Assert.That(source, Does.Contain("左の詳細表示中の動画"));
        });
    }

    [Test]
    public void 代表動画はGrid用サムネイルを表示する()
    {
        string source = ReadFileOrganizerTabViewXaml();

        Assert.Multiple(() =>
        {
            Assert.That(
                source,
                Does.Contain(
                    "Source=\"{Binding ThumbPathGrid, Converter={StaticResource noLockImageConverter}}\""
                )
            );
            Assert.That(
                source,
                Does.Not.Contain(
                    "Source=\"{Binding ThumbPathSmall, Converter={StaticResource noLockImageConverter}}\""
                )
            );
        });
    }

    [Test]
    public void 詳細右下のゴミ箱は表示中1件を既存ファイル削除へ流す()
    {
        string xaml = ReadFileOrganizerTabViewXaml();
        string mainWindowSource = File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                "BottomTabs",
                "FileOrganizer",
                "MainWindow.BottomTab.FileOrganizer.cs"
            ),
            Encoding.UTF8
        );
        string deleteSource = File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                "Views",
                "Main",
                "MainWindow.MenuActions.cs"
            ),
            Encoding.UTF8
        );

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("x:Name=\"DeleteFileButton\""));
            Assert.That(xaml, Does.Contain("HorizontalAlignment=\"Right\""));
            Assert.That(xaml, Does.Contain("VerticalAlignment=\"Bottom\""));
            Assert.That(xaml, Does.Contain("Kind=\"Delete\""));
            Assert.That(xaml, Does.Contain("Click=\"DeleteFileButton_Click\""));
            Assert.That(
                mainWindowSource,
                Does.Contain(
                    "ExecuteDeleteAction(DeleteActionMode.DeleteFileWithChoice, [movie]);"
                )
            );
            Assert.That(deleteSource, Does.Contain("IReadOnlyList<MovieRecords> requestedRecords = null"));
            Assert.That(deleteSource, Does.Contain("requestedRecords.Where(record => record != null).ToList()"));
        });
    }

    [Test]
    public void MainWindowは詳細1件と表示中全件を分け確認後に既存移動へ流す()
    {
        string repositoryRoot = FindRepositoryRoot();
        string source = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "BottomTabs",
                "FileOrganizer",
                "MainWindow.BottomTab.FileOrganizer.cs"
            ),
            Encoding.UTF8
        );

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("_fileOrganizerDisplayedMovie = GetSelectedItemByTabIndex();"));
            Assert.That(source, Does.Contain("MainVM?.FilteredMovieRecs"));
            Assert.That(source, Does.Contain("FileOrganizerMoveConfirmationPolicy.BuildMessage("));
            Assert.That(source, Does.Contain("MessageBoxButton.YesNo"));
            Assert.That(source, Does.Contain("QueueMovieFileMove(targets, destination.FolderPath);"));
            Assert.That(source, Does.Contain("!destination.IsShortcutActive"));
            Assert.That(source, Does.Not.Contain("IsFileOrganizerTabActive"));
            Assert.That(source, Does.Contain("監視フォルダへ追加しますか？"));
            Assert.That(source, Does.Contain("await QueueDroppedWatchFoldersAsync(dbFullPath, [folderPath]);"));
        });
    }

    [Test]
    public void メイン一覧の選択変更はDock活性に関係なく整理タブへ同期する()
    {
        string repositoryRoot = FindRepositoryRoot();
        string selectionSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "Views", "Main", "MainWindow.Selection.cs"),
            Encoding.UTF8
        );

        Assert.That(
            selectionSource.Split("RefreshFileOrganizerDisplayedMovie();").Length - 1,
            Is.GreaterThanOrEqualTo(3)
        );
    }

    private static string FindRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        // 分離artifactsからの実行でも、コンパイル元ソースの位置を正本として戻る。
        DirectoryInfo? current = new(Path.GetDirectoryName(sourceFilePath) ?? "");
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IndigoMovieManager.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("リポジトリルートを検出できませんでした。");
    }

    private static string ReadFileOrganizerTabViewXaml()
    {
        return File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                "BottomTabs",
                "FileOrganizer",
                "FileOrganizerTabView.xaml"
            ),
            Encoding.UTF8
        );
    }
}
