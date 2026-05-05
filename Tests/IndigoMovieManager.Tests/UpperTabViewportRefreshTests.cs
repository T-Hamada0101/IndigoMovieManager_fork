using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UpperTabViewportRefreshTests
{
    [Test]
    public void 抑止期限内ならfollowup_scroll_refreshを止める()
    {
        long nowUtcTicks = DateTime.UtcNow.Ticks;
        long suppressUntilUtcTicks = nowUtcTicks + TimeSpan.FromMilliseconds(50).Ticks;

        bool actual = IndigoMovieManager.MainWindow.ShouldSuppressUpperTabFollowupScrollRefresh(
            nowUtcTicks,
            suppressUntilUtcTicks
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void 抑止期限切れならfollowup_scroll_refreshを止めない()
    {
        long nowUtcTicks = DateTime.UtcNow.Ticks;
        long suppressUntilUtcTicks = nowUtcTicks - 1;

        bool actual = IndigoMovieManager.MainWindow.ShouldSuppressUpperTabFollowupScrollRefresh(
            nowUtcTicks,
            suppressUntilUtcTicks
        );

        Assert.That(actual, Is.False);
    }

    [Test]
    public void Empty範囲はpreferredキーsnapshotを公開しない()
    {
        bool actual = IndigoMovieManager.MainWindow.ShouldPublishPreferredMoviePathKeysSnapshot(
            IndigoMovieManager.UpperTabs.Common.UpperTabVisibleRange.Empty
        );

        Assert.That(actual, Is.False);
    }

    [Test]
    public void 可視範囲がある場合だけpreferredキーsnapshotを公開する()
    {
        bool actual = IndigoMovieManager.MainWindow.ShouldPublishPreferredMoviePathKeysSnapshot(
            IndigoMovieManager.UpperTabs.Common.UpperTabVisibleRange.Create(
                firstVisibleIndex: 0,
                lastVisibleIndex: 2,
                totalCount: 10,
                overscanItemCount: 1
            )
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void Preferredキー更新時は画像MultiBinding再評価用revisionを進める()
    {
        string source = GetRepoText("UpperTabs", "Common", "MainWindow.UpperTabs.Viewport.cs");
        string applySnapshotMethod = GetMethodBlock(source, "private void ApplyUpperTabViewportSnapshot(");
        string clearMethod = GetMethodBlock(source, "private void ClearUpperTabVisibleRange()");
        string refreshMethod = GetMethodBlock(
            source,
            "private void RefreshUpperTabPreferredMoviePathKeysRevision()"
        );

        // preferred key は static gate だけでは Binding を起こせないため、Window DP の revision を軽く進める。
        Assert.That(
            source,
            Does.Contain("public static readonly DependencyProperty UpperTabPreferredMoviePathKeysRevisionProperty")
        );
        Assert.That(source, Does.Contain("public int UpperTabPreferredMoviePathKeysRevision"));
        Assert.That(source, Does.Contain("private bool _isUpperTabPreferredMoviePathKeysSnapshotPublished;"));
        Assert.That(applySnapshotMethod, Does.Contain("bool preferredMoviePathKeysChanged"));
        Assert.That(applySnapshotMethod, Does.Contain("bool publishStateChanged"));
        Assert.That(applySnapshotMethod, Does.Contain("UpperTabActivationGate.UpdatePreferredMoviePathKeys(nextPreferredMoviePathKeys);"));
        Assert.That(applySnapshotMethod, Does.Contain("if (publishStateChanged || preferredMoviePathKeysChanged)"));
        Assert.That(applySnapshotMethod, Does.Contain("RefreshUpperTabPreferredMoviePathKeysRevision();"));
        Assert.That(clearMethod, Does.Contain("if (_isUpperTabPreferredMoviePathKeysSnapshotPublished)"));
        Assert.That(clearMethod, Does.Contain("RefreshUpperTabPreferredMoviePathKeysRevision();"));
        Assert.That(refreshMethod, Does.Contain("UpperTabPreferredMoviePathKeysRevision = unchecked("));
        Assert.That(refreshMethod, Does.Contain("UpperTabPreferredMoviePathKeysRevision + 1"));
    }

    [Test]
    public void Player右レール画像MultiBindingはpreferredキーrevisionをtriggerとして渡す()
    {
        string mainWindowXaml = GetRepoText("Views", "Main", "MainWindow.xaml");
        string playerThumbnailImageBinding = GetPlayerThumbnailImageBinding(mainWindowXaml);
        string converterSource = GetRepoText("UpperTabs", "Common", "UpperTabImageSourceConverter.cs");

        // 5番目の Binding は converter の判定値ではなく、preferred key 更新後の再評価 trigger として使う。
        Assert.That(playerThumbnailImageBinding, Does.Contain("<Binding Path=\"ThumbPathGrid\" />"));
        Assert.That(playerThumbnailImageBinding, Does.Contain("<Binding Path=\"IsExists\" />"));
        Assert.That(
            playerThumbnailImageBinding,
            Does.Contain("<Binding Source=\"{x:Reference PlayerThumbnailList}\" Path=\"IsVisible\" />")
        );
        Assert.That(playerThumbnailImageBinding, Does.Contain("<Binding Path=\"Movie_Path\" />"));
        Assert.That(
            playerThumbnailImageBinding,
            Does.Contain("<Binding Source=\"{x:Reference window}\" Path=\"UpperTabPreferredMoviePathKeysRevision\" />")
        );
        Assert.That(converterSource, Does.Contain("object moviePathValue = values.Length > 3 ? values[3] : null;"));
        Assert.That(converterSource, Does.Not.Contain("values[4]"));
    }

    private static string GetPlayerThumbnailImageBinding(string mainWindowXaml)
    {
        int listStart = mainWindowXaml.IndexOf(
            "x:Name=\"PlayerThumbnailList\"",
            StringComparison.Ordinal
        );
        Assert.That(listStart, Is.GreaterThanOrEqualTo(0));

        int converterIndex = mainWindowXaml.IndexOf(
            "Converter=\"{StaticResource upperTabImageSourceConverter}\"",
            listStart,
            StringComparison.Ordinal
        );
        Assert.That(converterIndex, Is.GreaterThan(listStart));

        int bindingEnd = mainWindowXaml.IndexOf("</MultiBinding>", converterIndex, StringComparison.Ordinal);
        Assert.That(bindingEnd, Is.GreaterThan(converterIndex));
        return mainWindowXaml.Substring(converterIndex, bindingEnd - converterIndex);
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
        return string.Empty;
    }
}
