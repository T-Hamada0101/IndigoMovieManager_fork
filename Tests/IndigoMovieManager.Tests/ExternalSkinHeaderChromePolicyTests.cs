using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ExternalSkinHeaderChromePolicyTests
{
    [Test]
    public void 外部skinでも共通ヘッダーを表示し最小ヘッダーを畳む()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.WebViewSkin.Chrome.cs");
        string refreshSource = GetRepoText("Views", "Main", "MainWindow.WebViewSkin.cs");
        string menuActionSource = GetRepoText("Views", "Main", "MainWindow.MenuActions.cs");
        string xaml = GetRepoText("Views", "Main", "MainWindow.xaml");
        string method = GetMethodBlock(
            source,
            "private void ApplyExternalSkinMinimalChromeVisibility("
        );
        string syncMethod = GetMethodBlock(
            source,
            "private void SyncExternalSkinMinimalSkinSelector("
        );
        string refreshMethod = GetMethodBlock(
            refreshSource,
            "private async Task RefreshExternalSkinHostPresentationAsync("
        );
        string drawerHost = GetXmlElementBlock(
            xaml,
            "<materialDesign:DrawerHost",
            "</materialDesign:DrawerHost>",
            "x:Name=\"MainDrawerHost\""
        );
        string mainHeaderBarTag = GetXmlStartTag(drawerHost, "x:Name=\"MainHeaderBar\"");
        string dockingManagerTag = GetXmlStartTag(xaml, "x:Name=\"uxDockingManager\"");

        Assert.Multiple(() =>
        {
            Assert.That(
                method,
                Does.Contain("MainHeaderStandardChromePanel.Visibility = Visibility.Visible;")
            );
            Assert.That(
                method,
                Does.Contain("ExternalSkinMinimalChromePanel.Visibility = Visibility.Collapsed;")
            );
            Assert.That(
                method,
                Does.Contain("SyncExternalSkinMinimalSkinSelector(true, displaySkinName);")
            );
            Assert.That(
                method,
                Does.Not.Contain("MainHeaderStandardChromePanel.Visibility = Visibility.Collapsed;")
            );
            Assert.That(
                method,
                Does.Not.Contain("ExternalSkinMinimalChromePanel.Visibility = Visibility.Visible;")
            );
            Assert.That(xaml, Does.Contain("x:Name=\"MainHeaderStandardChromePanel\""));
            Assert.That(drawerHost, Does.Contain("<RowDefinition Height=\"48\" />"));
            Assert.That(drawerHost, Does.Contain("x:Name=\"MainHeaderBar\""));
            Assert.That(mainHeaderBarTag, Does.Contain("Grid.Row=\"0\""));
            Assert.That(mainHeaderBarTag, Does.Contain("VerticalAlignment=\"Top\""));
            Assert.That(dockingManagerTag, Does.Contain("Margin=\"0,48,0,0\""));
            Assert.That(xaml, Does.Contain("Height=\"36\""));
            Assert.That(xaml, Does.Contain("<ColumnDefinition Width=\"*\" MinWidth=\"0\" />"));
            Assert.That(xaml, Does.Contain("x:Name=\"ExternalSkinMinimalSkinSelector\""));
            Assert.That(xaml, Does.Contain("Width=\"170\""));
            Assert.That(xaml, Does.Contain("MaxWidth=\"420\""));
            Assert.That(xaml, Does.Contain("TextTrimming=\"CharacterEllipsis\""));
            Assert.That(syncMethod, Does.Contain("GetCachedAvailableSkinDefinitions()"));
            Assert.That(syncMethod, Does.Not.Contain("GetAvailableSkinDefinitions()"));
            Assert.That(refreshMethod, Does.Contain("ResolveExternalSkinDefinitionRefreshMode(reason)"));
            Assert.That(refreshMethod, Does.Contain("definition_mode="));
            Assert.That(menuActionSource, Does.Contain("\"header-reload\""));
            Assert.That(source, Does.Contain("\"minimal-chrome-reload\""));
            Assert.That(source, Does.Contain("\"fallback-notice-retry\""));
        });
    }

    [Test]
    public void 外部skin_refresh_reasonごとにcatalog再確認モードを分ける()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                MainWindow.ResolveExternalSkinDefinitionRefreshModeForTesting("header-reload"),
                Is.EqualTo("CatalogRefresh")
            );
            Assert.That(
                MainWindow.ResolveExternalSkinDefinitionRefreshModeForTesting("fallback-notice-retry"),
                Is.EqualTo("CatalogRefresh")
            );
            Assert.That(
                MainWindow.ResolveExternalSkinDefinitionRefreshModeForTesting("minimal-chrome-reload"),
                Is.EqualTo("CachedSnapshot")
            );
            Assert.That(
                MainWindow.ResolveExternalSkinDefinitionRefreshModeForTesting("dbinfo-Skin"),
                Is.EqualTo("CachedSnapshot")
            );
            Assert.That(
                MainWindow.ResolveExternalSkinDefinitionRefreshModeForTesting("skin-tag-mutation"),
                Is.EqualTo("CachedSnapshot")
            );
        });
    }

    [Test]
    public void 外部skin_refresh_batchではCatalogRefresh系reasonをdbinfoより優先する()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                MainWindow.SelectPreferredExternalSkinHostRefreshReasonForTesting(
                    "dbinfo-DBFullPath",
                    "header-reload"
                ),
                Is.EqualTo("header-reload")
            );
            Assert.That(
                MainWindow.SelectPreferredExternalSkinHostRefreshReasonForTesting(
                    "header-reload",
                    "dbinfo-DBFullPath"
                ),
                Is.EqualTo("header-reload")
            );
            Assert.That(
                MainWindow.SelectPreferredExternalSkinHostRefreshReasonForTesting(
                    "dbinfo-Skin",
                    "fallback-notice-retry"
                ),
                Is.EqualTo("fallback-notice-retry")
            );
            Assert.That(
                MainWindow.SelectPreferredExternalSkinHostRefreshReasonForTesting(
                    "fallback-notice-retry",
                    "dbinfo-ThumbFolder"
                ),
                Is.EqualTo("fallback-notice-retry")
            );
            Assert.That(
                MainWindow.SelectPreferredExternalSkinHostRefreshReasonForTesting(
                    "minimal-chrome-reload",
                    "dbinfo-Skin"
                ),
                Is.EqualTo("dbinfo-Skin")
            );
            Assert.That(
                MainWindow.SelectPreferredExternalSkinHostRefreshReasonForTesting(
                    "header-reload",
                    "minimal-chrome-reload"
                ),
                Is.EqualTo("header-reload")
            );
        });
    }

    [Test]
    public void 外部skin_catalog再確認reasonはAsync経路で行う()
    {
        string refreshSource = GetRepoText("Views", "Main", "MainWindow.WebViewSkin.cs");
        string orchestratorSource = GetRepoText("WhiteBrowserSkin", "WhiteBrowserSkinOrchestrator.cs");
        string mainWindowSkinSource = GetRepoText("WhiteBrowserSkin", "MainWindow.Skin.cs");
        string refreshMethod = GetMethodBlock(
            refreshSource,
            "private async Task RefreshExternalSkinHostPresentationAsync("
        );
        string syncDefinitionMethod = GetMethodBlock(
            refreshSource,
            "private WhiteBrowserSkinDefinition GetCurrentExternalSkinDefinition("
        );
        string asyncDefinitionMethod = GetMethodBlock(
            refreshSource,
            "private async Task<WhiteBrowserSkinDefinition> GetCurrentExternalSkinDefinitionAsync("
        );
        string priorityMethod = GetMethodBlock(
            refreshSource,
            "private static int GetExternalSkinHostRefreshReasonPriority("
        );
        string orchestratorAsyncMethod = GetMethodBlock(
            orchestratorSource,
            "public async Task<WhiteBrowserSkinDefinition> RefreshCurrentSkinDefinitionAsync("
        );

        Assert.Multiple(() =>
        {
            Assert.That(refreshMethod, Does.Contain("await GetCurrentExternalSkinDefinitionAsync("));
            Assert.That(refreshMethod, Does.Contain("definitionRefreshMode"));
            Assert.That(refreshMethod, Does.Not.Contain("GetCurrentExternalSkinDefinition("));
            Assert.That(syncDefinitionMethod, Does.Not.Contain("forceCatalogRefresh"));
            Assert.That(syncDefinitionMethod, Does.Not.Contain("RefreshCurrentSkinDefinition("));
            Assert.That(refreshSource, Does.Contain("\"header-reload\" => ExternalSkinDefinitionRefreshMode.CatalogRefresh"));
            Assert.That(refreshSource, Does.Contain("\"fallback-notice-retry\" => ExternalSkinDefinitionRefreshMode.CatalogRefresh"));
            Assert.That(refreshSource, Does.Contain("_ => ExternalSkinDefinitionRefreshMode.CachedSnapshot"));
            Assert.That(priorityMethod, Does.Contain("ResolveExternalSkinDefinitionRefreshMode(reason)"));
            Assert.That(priorityMethod, Does.Not.Contain("\"header-reload\" => 400"));
            Assert.That(priorityMethod, Does.Not.Contain("\"fallback-notice-retry\" => 400"));
            Assert.That(priorityMethod, Does.Contain("\"minimal-chrome-reload\" => 50"));
            Assert.That(refreshSource, Does.Not.Contain("private WhiteBrowserSkinDefinition RefreshCurrentExternalSkinDefinition("));
            Assert.That(asyncDefinitionMethod, Does.Contain("await RefreshCurrentExternalSkinDefinitionAsync()"));
            Assert.That(refreshSource, Does.Contain("private async Task<WhiteBrowserSkinDefinition> RefreshCurrentExternalSkinDefinitionAsync()"));
            Assert.That(mainWindowSkinSource, Does.Contain("RefreshCurrentSkinDefinitionAsync()"));
            Assert.That(orchestratorAsyncMethod, Does.Contain("await Task.Run(() => WhiteBrowserSkinCatalogService.Load(skinRootPath))"));
            Assert.That(orchestratorAsyncMethod, Does.Contain("availableSkinDefinitions = loadedDefinitions;"));
            Assert.That(orchestratorAsyncMethod, Does.Contain("activeSkinDefinition ="));
            Assert.That(orchestratorAsyncMethod, Does.Not.Contain("ApplySkinByName("));
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

    private static string GetXmlElementBlock(
        string source,
        string startTag,
        string endTag,
        string marker
    )
    {
        int markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.That(markerIndex, Is.GreaterThanOrEqualTo(0), $"{marker} が見つかりません。");

        int start = source.LastIndexOf(startTag, markerIndex, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{marker} を含む {startTag} が見つかりません。");

        int end = source.IndexOf(endTag, markerIndex, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThanOrEqualTo(0), $"{marker} を含む {endTag} が見つかりません。");

        return source.Substring(start, end - start + endTag.Length);
    }

    private static string GetXmlStartTag(string source, string marker)
    {
        int markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.That(markerIndex, Is.GreaterThanOrEqualTo(0), $"{marker} が見つかりません。");

        // x:Name を持つ開始タグだけを切り出し、配置の固定値を局所的に確認する。
        int start = source.LastIndexOf('<', markerIndex);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{marker} の開始タグが見つかりません。");

        int end = source.IndexOf('>', markerIndex);
        Assert.That(end, Is.GreaterThanOrEqualTo(0), $"{marker} の開始タグ終端が見つかりません。");

        return source.Substring(start, end - start + 1);
    }
}
