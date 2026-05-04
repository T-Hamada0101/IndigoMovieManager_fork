using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MaterialDesignDrawerHostCompatibilityPolicyTests
{
    [Test]
    public void 互換DrawerHostは主コンテンツの上に左ドロワーを重ねる()
    {
        string source = GetRepoText("Compatibility", "MaterialDesignCompatibility.cs");
        string drawerHostClass = GetClassBlock(source, "public class DrawerHost : ContentControl");
        string rebuildVisual = GetMethodBlock(drawerHostClass, "private void RebuildVisual()");

        int rootGridIndex = rebuildVisual.IndexOf("var root = new Grid();", StringComparison.Ordinal);
        int mainContentIndex = rebuildVisual.IndexOf(
            "root.Children.Add(new ContentPresenter { Content = mainContent });",
            StringComparison.Ordinal
        );
        int drawerCreateIndex = rebuildVisual.IndexOf("drawerContainer = new Border", StringComparison.Ordinal);
        int zIndexIndex = rebuildVisual.IndexOf("Panel.SetZIndex(drawerContainer, 10);", StringComparison.Ordinal);
        int drawerAddIndex = rebuildVisual.IndexOf("root.Children.Add(drawerContainer);", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(rootGridIndex, Is.GreaterThanOrEqualTo(0), "DrawerHostのroot Gridが見つかりません。");
            Assert.That(mainContentIndex, Is.GreaterThan(rootGridIndex), "主コンテンツはroot Gridへ先に追加します。");
            Assert.That(drawerCreateIndex, Is.GreaterThan(mainContentIndex), "左ドロワーは主コンテンツの後に作ります。");
            Assert.That(zIndexIndex, Is.GreaterThan(drawerCreateIndex), "左ドロワーはZIndexで前面に固定します。");
            Assert.That(drawerAddIndex, Is.GreaterThan(zIndexIndex), "左ドロワーはZIndex設定後に追加します。");
            Assert.That(
                rebuildVisual,
                Does.Contain("Child = new ContentPresenter { Content = LeftDrawerContent },")
            );
            Assert.That(rebuildVisual, Does.Contain("HorizontalAlignment = HorizontalAlignment.Left,"));
            Assert.That(rebuildVisual, Does.Contain("VerticalAlignment = VerticalAlignment.Stretch,"));
            Assert.That(rebuildVisual, Does.Contain("base.Content = root;"));
            Assert.That(drawerHostClass, Does.Not.Contain("OverlayBrush"));
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

        Assert.Fail($"{Path.Combine(relativePathParts)} の位置をrepo rootから解決できませんでした。");
        return string.Empty;
    }

    private static string GetClassBlock(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{signature} が見つかりません。");

        return GetBraceBlock(source, signature, start);
    }

    private static string GetMethodBlock(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{signature} が見つかりません。");

        return GetBraceBlock(source, signature, start);
    }

    private static string GetBraceBlock(string source, string signature, int start)
    {
        int bodyStart = source.IndexOf('{', start);
        Assert.That(bodyStart, Is.GreaterThanOrEqualTo(0), $"{signature} の本文開始が見つかりません。");

        // 波かっこの深さだけを追い、対象ブロックを安全に切り出す。
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
        return string.Empty;
    }
}
