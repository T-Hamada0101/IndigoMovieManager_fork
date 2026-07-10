namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class SearchSelectionContinuityTests
{
    [Test]
    public void ShouldSelectFirstSearchResult_選択が残っている時は先頭選択しない()
    {
        MovieRecords selectedItem = new() { Movie_Id = 1 };

        bool actual = MainWindow.ShouldSelectFirstSearchResult(selectedItem);

        Assert.That(actual, Is.False);
    }

    [Test]
    public void ShouldSelectFirstSearchResult_未選択の時だけ先頭選択する()
    {
        bool actual = MainWindow.ShouldSelectFirstSearchResult(null);

        Assert.That(actual, Is.True);
    }

    [Test]
    public void SearchExecutor_選択連続性helperを結果更新後のcallbackへ接続する()
    {
        string source = File.ReadAllText(GetSearchSourcePath());

        Assert.That(source, Does.Contain("selectFirstItem: SelectFirstSearchResultIfNeeded"));
        Assert.That(source, Does.Contain("ShouldSelectFirstSearchResult(GetSelectedItemByTabIndex())"));
    }

    private static string GetSearchSourcePath([System.Runtime.CompilerServices.CallerFilePath] string sourcePath = "")
    {
        string testsDirectory = Path.GetDirectoryName(sourcePath)!;
        string repositoryRoot = Path.GetFullPath(Path.Combine(testsDirectory, "..", ".."));
        return Path.Combine(repositoryRoot, "Views", "Main", "MainWindow.Search.cs");
    }
}
