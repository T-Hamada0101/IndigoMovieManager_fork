using System.Runtime.CompilerServices;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UpperTabSelectionContinuityPolicyTests
{
    [Test]
    public void 詳細更新は現在選択を維持し未選択時だけ先頭選択後に再取得する()
    {
        const string expectedFlow = """
                        selectedMovie = GetSelectedItemByTabIndex();
                        if (selectedMovie == null && selectFirstItem)
                        {
                            SelectFirstItem();
                            selectedMovie = GetSelectedItemByTabIndex();
                        }

                        return selectedMovie != null;
            """;

        Assert.That(ReadSelectionFlowSource(), Does.Contain(expectedFlow));
    }

    [Test]
    public void 選択同期は詳細とタグを同じ動画へ揃える()
    {
        string source = ReadSelectionFlowSource();

        Assert.That(source, Does.Contain("HideExtensionDetail();\n                HideTagEditor();"));
        Assert.That(
            source,
            Does.Contain("ShowExtensionDetail(selectedMovie);\n            // Playerタブの先頭自動選択はSelectionChangedを抑止するため、ここでタグ対象も明示的に同期する。\n            ShowTagEditor(selectedMovie);")
        );
    }

    private static string ReadSelectionFlowSource([CallerFilePath] string sourceFilePath = "")
    {
        string repositoryRoot = Path.GetFullPath(
            Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", "..")
        );
        return File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "UpperTabs",
                "Common",
                "MainWindow.UpperTabs.SelectionFlow.cs"
            )
        );
    }
}
