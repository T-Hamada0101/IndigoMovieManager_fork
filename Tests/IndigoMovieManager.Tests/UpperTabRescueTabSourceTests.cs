using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UpperTabRescueTabSourceTests
{
    [Test]
    public void Rescue一覧サムネは行サイズのdecode高さを渡す()
    {
        string xaml = GetRepoText("UpperTabs", "Rescue", "RescueTabView.xaml");

        Assert.That(
            xaml,
            Does.Contain(
                "Source=\"{Binding ThumbnailPath, Converter={StaticResource noLockImageConverter}, ConverterParameter=18}\""
            )
        );
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
}
