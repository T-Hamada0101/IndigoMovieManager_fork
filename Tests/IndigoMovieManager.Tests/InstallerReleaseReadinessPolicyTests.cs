using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class InstallerReleaseReadinessPolicyTests
{
    [Test]
    public void Bundleだけをアプリ一覧へ表示する契約を持つ()
    {
        string product = ReadRepoFile("installer", "wix", "Product.wxs");
        string bundle = ReadRepoFile("installer", "wix", "Bundle.wxs");

        Assert.Multiple(() =>
        {
            Assert.That(product, Does.Contain("<Property Id=\"ARPSYSTEMCOMPONENT\" Value=\"1\" />"));
            Assert.That(bundle, Does.Contain("Visible=\"no\""));
        });
    }

    [Test]
    public void スタートメニュー導線と旧layout掃除をMainFeatureへ含める()
    {
        string product = ReadRepoFile("installer", "wix", "Product.wxs");

        Assert.Multiple(() =>
        {
            Assert.That(product, Does.Contain("Id=\"ApplicationStartMenuShortcut\""));
            Assert.That(product, Does.Contain("Advertise=\"no\""));
            Assert.That(product, Does.Contain("<ComponentRef Id=\"ApplicationStartMenuShortcut\" />"));
            Assert.That(product, Does.Contain("Name=\"layout*.xml\" On=\"uninstall\""));
            Assert.That(product, Does.Contain("<ComponentRef Id=\"LegacyDockLayoutCleanup\" />"));
        });
    }

    [Test]
    public void Dpi拡大用テーマはアンインストールボタン幅と専用進行文言を持つ()
    {
        string themePath = Path.Combine(
            FindRepoRoot(),
            "installer",
            "wix",
            "Themes",
            "IndigoHyperlinkTheme.xml"
        );
        XDocument theme = XDocument.Load(themePath);
        XNamespace ns = "http://wixtoolset.org/schemas/v4/thmutil";
        XElement uninstallButton = theme
            .Descendants(ns + "Button")
            .Single(element => (string?)element.Attribute("Name") == "UninstallButton");
        XElement progressPage = theme
            .Descendants(ns + "Page")
            .Single(element => (string?)element.Attribute("Name") == "Progress");

        Assert.Multiple(() =>
        {
            Assert.That((int?)uninstallButton.Attribute("Width"), Is.GreaterThanOrEqualTo(120));
            Assert.That((int?)uninstallButton.Attribute("Height"), Is.GreaterThanOrEqualTo(27));
            Assert.That(progressPage.ToString(), Does.Contain("WixBundleAction = 3 OR WixBundleAction = 4"));
            Assert.That(progressPage.ToString(), Does.Contain("ProgressUninstallHeader"));
            Assert.That(progressPage.ToString(), Does.Contain("ProgressUninstallLabel"));
        });
    }

    [Test]
    public void Bundleはカスタムテーマと日本語アンインストール文言を使う()
    {
        string bundle = ReadRepoFile("installer", "wix", "Bundle.wxs");
        string localization = ReadRepoFile(
            "installer",
            "wix",
            "Localization",
            "Bundle",
            "ja-JP.wxl"
        );

        Assert.Multiple(() =>
        {
            Assert.That(bundle, Does.Contain("Theme=\"hyperlinkLicense\""));
            Assert.That(bundle, Does.Contain("ThemeFile=\"Themes\\IndigoHyperlinkTheme.xml\""));
            Assert.That(localization, Does.Contain("Value=\"アンインストールしています\""));
            Assert.That(localization, Does.Contain("Value=\"削除中:\""));
        });
    }

    private static string ReadRepoFile(params string[] parts)
    {
        return File.ReadAllText(Path.Combine([FindRepoRoot(), .. parts]));
    }

    private static string FindRepoRoot([CallerFilePath] string callerFilePath = "")
    {
        // 呼び出し元から親へたどり、テスト実行場所に依存しないrepo rootを探す。
        DirectoryInfo? current = new(Path.GetDirectoryName(callerFilePath) ?? Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IndigoMovieManager.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        Assert.Fail("repo rootを解決できませんでした。");
        return "";
    }
}
