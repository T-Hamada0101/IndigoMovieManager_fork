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
